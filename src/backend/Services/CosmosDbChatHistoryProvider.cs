using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

public sealed class CosmosDbChatHistoryProvider : AIContextProvider, IDisposable
{
    private const string DefaultContextPrompt = "## Chat History\nConsider the following previous messages:";
    private const int DefaultMaxResults = 10;

    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly string _partitionKeyPath;
    private readonly int _maxResults;
    private readonly string _contextPrompt;
    private readonly bool _enableSensitiveTelemetryData;
    private readonly ILogger<CosmosDbChatHistoryProvider>? _logger;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ChatHistoryMemoryProviderScope _storageScope;
    private readonly ChatHistoryMemoryProviderScope _searchScope;

    private Container? _container;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _containerInitialized;
    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosDbChatHistoryProvider"/> class.
    /// </summary>
    /// <param name="cosmosClient">The Cosmos DB client.</param>
    /// <param name="databaseName">The name of the database.</param>
    /// <param name="containerName">The name of the container for storing chat history.</param>
    /// <param name="partitionKeyPath">The partition key path for the container (e.g., "/ApplicationId").</param>
    /// <param name="storageScope">Values to scope the chat history storage with.</param>
    /// <param name="searchScope">Optional values to scope the chat history search with. Defaults to <paramref name="storageScope"/> if not provided.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="embeddingGenerator">The embedding generator to use for creating vector embeddings.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CosmosDbChatHistoryProvider(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        string partitionKeyPath,
        ChatHistoryMemoryProviderScope storageScope,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ChatHistoryMemoryProviderScope? searchScope = null,
        ChatHistoryMemoryProviderOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        this._cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        this._databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
        this._containerName = containerName ?? throw new ArgumentNullException(nameof(containerName));
        this._partitionKeyPath = partitionKeyPath ?? throw new ArgumentNullException(nameof(partitionKeyPath));
        this._storageScope = storageScope ?? throw new ArgumentNullException(nameof(storageScope));
        this._embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        this._searchScope = searchScope ?? new ChatHistoryMemoryProviderScope(storageScope);

        options ??= new ChatHistoryMemoryProviderOptions();
        this._maxResults = options.MaxResults ?? DefaultMaxResults;
        this._contextPrompt = options.ContextPrompt ?? DefaultContextPrompt;
        this._enableSensitiveTelemetryData = options.EnableSensitiveTelemetryData;
        this._logger = loggerFactory?.CreateLogger<CosmosDbChatHistoryProvider>();
    }

    /// <inheritdoc />
    public override async ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Store the latest user message(s) from the request
            var latestUserMessage = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User);

            if (latestUserMessage != null && !string.IsNullOrWhiteSpace(latestUserMessage.Text))
            {
                var container = await this.EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

                // Generate embedding for the user message
                var contents = new List<string> { latestUserMessage.Text ?? string.Empty };
                var embeddings = await this._embeddingGenerator.GenerateAsync
                    (contents, cancellationToken: cancellationToken).ConfigureAwait(false);
                
                var queryEmbedding = embeddings[0].Vector.ToArray();

                // Store the user message
                var itemToStore = new ChatHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Role = latestUserMessage.Role.ToString(),
                    ApplicationId = this._storageScope?.ApplicationId ?? "default",
                    AgentId = this._storageScope?.AgentId ?? "default",
                    UserId = this._storageScope?.UserId ?? "default",
                    ThreadId = this._storageScope?.ThreadId ?? "default",
                    Content = latestUserMessage.Text ?? string.Empty,
                    ContentEmbedding = queryEmbedding,
                    CreatedAt = latestUserMessage.CreatedAt ?? DateTimeOffset.UtcNow
                };

                await container.CreateItemAsync(
                    itemToStore,
                    new PartitionKey(itemToStore.ApplicationId),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                // Perform vector search to find similar past conversations
                // Results are ranked by similarity (lower distance = more relevant)
                var vectorSearchQuery = $@"
                    SELECT TOP {this._maxResults} c.Role, c.Content, c.CreatedAt,
                           VectorDistance(c.ContentEmbedding, @queryEmbedding) AS SimilarityScore
                    FROM c
                    WHERE c.ApplicationId = @applicationId AND c.UserId = @userId
                    ORDER BY VectorDistance(c.ContentEmbedding, @queryEmbedding)
                ";

                var queryDef = new QueryDefinition(vectorSearchQuery)
                    .WithParameter("@queryEmbedding", queryEmbedding)
                    .WithParameter("@applicationId", this._searchScope.ApplicationId)
                    .WithParameter("@userId", this._searchScope.UserId);

                var searchIterator = container.GetItemQueryIterator<ChatHistorySearchResult>(queryDef);
                var relevantMessages = new List<string>();

                while (searchIterator.HasMoreResults)
                {
                    var response = await searchIterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var item in response)
                    {
                        var role = item.Role ?? "unknown";
                        var content = item.Content ?? string.Empty;
                        
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            relevantMessages.Add($"[{role}] {content}");
                        }
                    }
                }

                // Return relevant context if found
                if (relevantMessages.Count > 0)
                {
                    var contextText = $"{this._contextPrompt}\n{string.Join("\n", relevantMessages)}";
                    
                    if (this._logger?.IsEnabled(LogLevel.Trace) is true)
                    {
                        this._logger.LogTrace(
                            "CosmosDbChatHistoryProvider: Retrieved {Count} relevant messages. ApplicationId: '{ApplicationId}', AgentId: '{AgentId}', ThreadId: '{ThreadId}', UserId: '{UserId}'.",
                            relevantMessages.Count,
                            this._searchScope.ApplicationId,
                            this._searchScope.AgentId,
                            this._searchScope.ThreadId,
                            this.SanitizeLogData(this._searchScope.UserId));
                    }

                    return new AIContext
                    {
                        Messages = [new ChatMessage(ChatRole.System, contextText)]
                    };
                }
            }

            return new AIContext();
        }
        catch (Exception ex)
        {
            if (this._logger?.IsEnabled(LogLevel.Error) is true)
            {
                this._logger.LogError(
                    ex,
                    "CosmosDbChatHistoryProvider: Failed to store user messages to Cosmos DB due to error. ApplicationId: '{ApplicationId}', AgentId: '{AgentId}', ThreadId: '{ThreadId}', UserId: '{UserId}'.",
                    this._searchScope.ApplicationId,
                    this._searchScope.AgentId,
                    this._searchScope.ThreadId,
                    this.SanitizeLogData(this._searchScope.UserId));
            }

            return new AIContext();
        }
    }

    public override async ValueTask InvokedAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        // Only store if invocation was successful
        if (context.InvokeException != null)
        {
            return;
        }

        try
        {
            var container = await this.EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);

            // Only store response messages (user messages were already stored in InvokingAsync)
            var responseMessages = (context.ResponseMessages ?? [])
                .Where(message => !string.IsNullOrWhiteSpace(message.Text))
                .ToList();

            if (responseMessages.Count > 0)
            {
                // Generate embeddings for response messages first
                var contents = responseMessages.Select(m => m.Text ?? string.Empty).ToList();
                var embeddings = await this._embeddingGenerator.GenerateAsync(contents, cancellationToken: cancellationToken).ConfigureAwait(false);

                // Create items with embeddings in single iteration
                var itemsToStore = responseMessages
                    .Select((message, index) => new ChatHistoryItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Role = message.Role.ToString(),
                        ApplicationId = this._storageScope?.ApplicationId ?? "default",
                        AgentId = this._storageScope?.AgentId ?? "default",
                        UserId = this._storageScope?.UserId ?? "default",
                        ThreadId = this._storageScope?.ThreadId ?? "default",
                        Content = message.Text ?? string.Empty,
                        ContentEmbedding = embeddings[index].Vector.ToArray(),
                        CreatedAt = message.CreatedAt ?? DateTimeOffset.UtcNow
                    })
                    .ToList();

                // Store each message
                foreach (var item in itemsToStore)
                {
                    await container.CreateItemAsync(
                        item,
                        new PartitionKey(item.ApplicationId),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            if (this._logger?.IsEnabled(LogLevel.Error) is true)
            {
                this._logger.LogError(
                    ex,
                    "CosmosDbChatHistoryProvider: Failed to store response messages to Cosmos DB due to error. ApplicationId: '{ApplicationId}', AgentId: '{AgentId}', ThreadId: '{ThreadId}', UserId: '{UserId}'.",
                    this._storageScope.ApplicationId,
                    this._storageScope.AgentId,
                    this._storageScope.ThreadId,
                    this.SanitizeLogData(this._storageScope.UserId));
            }
        }
    }

    /// <summary>
    /// Ensures the container exists in Cosmos DB, creating it if necessary.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Cosmos DB container.</returns>
    private async Task<Container> EnsureContainerExistsAsync(CancellationToken cancellationToken = default)
    {
        if (this._containerInitialized && this._container != null)
        {
            return this._container;
        }

        await this._initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._containerInitialized && this._container != null)
            {
                return this._container;
            }

            var database = this._cosmosClient.GetDatabase(this._databaseName);

            // Define container properties with the specified partition key
            var containerProperties = new ContainerProperties
            {
                Id = this._containerName,
                PartitionKeyPaths = new List<string> { this._partitionKeyPath }
            };

            // Create container if it doesn't exist
            var containerResponse = await database.CreateContainerIfNotExistsAsync(
                containerProperties,
                throughput: 400, // Minimum throughput
                cancellationToken: cancellationToken).ConfigureAwait(false);

            this._container = containerResponse.Container;
            this._containerInitialized = true;

            return this._container;
        }
        finally
        {
            this._initializationLock.Release();
        }
    }

    /// <inheritdoc/>
    private void Dispose(bool disposing)
    {
        if (!this._disposedValue)
        {
            if (disposing)
            {
                this._initializationLock.Dispose();
            }

            this._disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var state = new CosmosDbChatHistoryProviderState
        {
            StorageScope = this._storageScope,
            SearchScope = this._searchScope,
            DatabaseName = this._databaseName,
            ContainerName = this._containerName
        };

        return JsonSerializer.SerializeToElement(state, jsonSerializerOptions);
    }

    private string? SanitizeLogData(string? data) => this._enableSensitiveTelemetryData ? data : "<redacted>";

    /// <summary>
    /// Represents the result of a chat history vector search query.
    /// </summary>
    internal sealed class ChatHistorySearchResult
    {
        [JsonPropertyName("Role")]
        public string? Role { get; set; }

        [JsonPropertyName("Content")]
        public string? Content { get; set; }

        [JsonPropertyName("CreatedAt")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("SimilarityScore")]
        public double SimilarityScore { get; set; }
    }

    /// <summary>
    /// Represents a chat history item stored in Cosmos DB.
    /// </summary>
    internal sealed class ChatHistoryItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("ApplicationId")]
        public string ApplicationId { get; set; } = string.Empty;

        [JsonPropertyName("AgentId")]
        public string AgentId { get; set; } = string.Empty;

        [JsonPropertyName("UserId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("ThreadId")]
        public string ThreadId { get; set; } = string.Empty;

        [JsonPropertyName("Content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("ContentEmbedding")]
        public float[]? ContentEmbedding { get; set; }

        [JsonPropertyName("CreatedAt")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    internal sealed class CosmosDbChatHistoryProviderState
    {
        public ChatHistoryMemoryProviderScope? StorageScope { get; set; }
        public ChatHistoryMemoryProviderScope? SearchScope { get; set; }
        public string? DatabaseName { get; set; }
        public string? ContainerName { get; set; }
    }
}
