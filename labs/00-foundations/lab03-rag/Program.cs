// Lab 03: Travel Assistant with RAG (Retrieval Augmented Generation)
// Learn how to use TextSearchProvider with Microsoft Agent Framework to add RAG capabilities

// This sample shows how to build a travel visa assistant using retrieval augmented generation (RAG).
// The TextSearchProvider searches visa policy documents before each model invocation and injects the relevant information into the model context.
// This uses a simple in-memory vector store with embeddings and cosine similarity for semantic search.

// Add NuGet package references
#:package Azure.AI.OpenAI@2.1.0
#:package Azure.Identity@1.18.0
#:package Microsoft.Agents.AI@1.0.0-rc2
#:package Microsoft.Agents.AI.Abstractions@1.0.0-rc2
#:package Microsoft.Extensions.AI@10.3.0
#:package Microsoft.Extensions.AI.OpenAI@10.3.0
#:package DotNetEnv@3.1.1

using Azure.AI.OpenAI;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ClientModel;

// Configure JSON serialization for Azure SDK compatibility with .NET 10
AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);

Console.WriteLine("=== Lab 03: Travel Assistant with RAG ===\n");

// Step 1: Load environment variables
LoadEnv();

// Step 2: Create chat client and embedding generator
var (chatClient, embeddingGenerator) = CreateClients();
if (chatClient == null || embeddingGenerator == null) return;

// Step 3: Load visa policy documents into vector store
var workspaceRoot = Directory.GetCurrentDirectory();
var japanVisaPolicyPath = Path.Combine(workspaceRoot, "data", "visa-policy-japan.md");
var schengenVisaPolicyPath = Path.Combine(workspaceRoot, "data", "visa-policy-schengen.md");

TextSearchStore textSearchStore = new(embeddingGenerator);
await UploadDocumentationFromFileAsync(japanVisaPolicyPath, "Japan Visa Policy", textSearchStore, 2000, 200);
await UploadDocumentationFromFileAsync(schengenVisaPolicyPath, "Schengen Area Visa Policy", textSearchStore, 2000, 200);

// Step 4: Create search adapter for TextSearchProvider
Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>> SearchAdapter = async (text, ct) =>
{
    var searchResults = await textSearchStore.SearchAsync(text, 5, ct);
    return searchResults.Select(r => new TextSearchProvider.TextSearchResult
    {
        SourceName = r.SourceName,
        SourceLink = r.SourceLink,
        Text = r.Text ?? string.Empty,
        RawRepresentation = r
    });
};

// Step 5: Configure TextSearchProvider options
TextSearchProviderOptions textSearchOptions = new()
{
    // Run the search prior to every model invocation
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    // Use up to 5 recent messages when searching so that searches
    // still produce valuable results even when the user is referring
    // back to previous messages in their request
    RecentMessageMemoryLimit = 5
};

// Step 6: Create agent with RAG capabilities
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name = "TravelAssistant",
    ChatOptions = new()
    {
        Instructions = "You are a helpful travel assistant. Use the provided travel knowledge to give accurate recommendations and cite the source document when available.",
        Tools = []
    },
    AIContextProviders = [new TextSearchProvider(SearchAdapter, textSearchOptions)]
});

Console.WriteLine("Agent created successfully\n");

// Step 7: Run conversation
AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine("User: Do I need a visa to visit Japan if I'm a US citizen?\n");
var response1 = await agent.RunAsync("Do I need a visa to visit Japan if I'm a US citizen?", session);
Console.WriteLine($"Agent: {response1.Text}\n");
Console.WriteLine(new string('-', 60) + "\n");

// ==================== Helper Methods ====================

(IChatClient?, IEmbeddingGenerator<string, Embedding<float>>?) CreateClients()
{
    var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_SERVICES_ENDPOINT");
    var azureApiKey = Environment.GetEnvironmentVariable("AZURE_AI_SERVICES_KEY");
    var modelName = Environment.GetEnvironmentVariable("AZURE_TEXT_MODEL_NAME") ?? "gpt-4o";
    var embeddingModelName = Environment.GetEnvironmentVariable("AZURE_EMBEDDING_MODEL_NAME") ?? "text-embedding-ada-002";

    var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    var githubModelId = Environment.GetEnvironmentVariable("GITHUB_TEXT_MODEL_ID") ?? "gpt-4o";
    var githubEmbeddingModelId = Environment.GetEnvironmentVariable("GITHUB_EMBEDDING_MODEL_ID") ?? "text-embedding-ada-002";
    var githubBaseUrl = Environment.GetEnvironmentVariable("GITHUB_MODELS_BASE_URL") ?? "https://models.inference.ai.azure.com";

    if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureApiKey))
    {
        Console.WriteLine($"Using Azure OpenAI ({modelName})\n");
        var azureClient = new AzureOpenAIClient(new Uri(azureEndpoint), new ApiKeyCredential(azureApiKey));
        return (
            azureClient.GetChatClient(modelName).AsIChatClient(),
            azureClient.GetEmbeddingClient(embeddingModelName).AsIEmbeddingGenerator()
        );
    }
    else if (!string.IsNullOrEmpty(githubToken))
    {
        Console.WriteLine($"Using GitHub Models ({githubModelId})\n");
        var githubClient = new AzureOpenAIClient(new Uri(githubBaseUrl), new ApiKeyCredential(githubToken));
        return (
            githubClient.GetChatClient(githubModelId).AsIChatClient(),
            githubClient.GetEmbeddingClient(githubEmbeddingModelId).AsIEmbeddingGenerator()
        );
    }
    else
    {
        Console.WriteLine("[ERROR] No API credentials found");
        Console.WriteLine("Please set AZURE_AI_SERVICES_ENDPOINT and AZURE_AI_SERVICES_KEY or GITHUB_TOKEN\n");
        return (null, null);
    }
}

void LoadEnv()
{
    var currentDir = Directory.GetCurrentDirectory();
    for (int i = 0; i < 10 && currentDir != null; i++)
    {
        var azureYamlPath = Path.Combine(currentDir, "azure.yaml");
        if (File.Exists(azureYamlPath))
        {
            var envPath = Path.Combine(currentDir, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                return;
            }
        }
        currentDir = Directory.GetParent(currentDir)?.FullName;
    }
}

static async Task UploadDocumentationFromFileAsync(
    string filePath,
    string sourceName,
    TextSearchStore store,
    int chunkSize,
    int overlap)
{
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"[ERROR] File not found: {filePath}\n");
        return;
    }

    var markdown = await File.ReadAllTextAsync(filePath);
    var documents = new List<TextSearchDocument>();
    for (int i = 0; i < markdown.Length; i += chunkSize)
    {
        var text = markdown.Substring(i, Math.Min(chunkSize + overlap, markdown.Length - i));

        documents.Add(new TextSearchDocument
        {
            SourceId = $"{sourceName}-chunk-{documents.Count}",
            SourceName = sourceName,
            SourceLink = filePath,
            Text = text
        });
    }

    await store.UpsertDocumentsAsync(documents);
}

// ==================== Helper Classes ====================

// Simple document model for text search
public class TextSearchDocument
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceLink { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public ReadOnlyMemory<float> Embedding { get; set; }
}

// A simple in-memory store that uses embeddings for semantic search
public class TextSearchStore
{
    private readonly List<TextSearchDocument> _documents = new();
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public TextSearchStore(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task UpsertDocumentsAsync(IEnumerable<TextSearchDocument> documents)
    {
        foreach (var doc in documents)
        {
            // Generate embedding for the document text
            var embeddingResult = await _embeddingGenerator.GenerateAsync(doc.Text);
            doc.Embedding = embeddingResult.Vector;
            _documents.Add(doc);
        }
    }

    public Task<IEnumerable<TextSearchDocument>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        // For this simplified version, generate embedding for the query and find closest matches
        return SearchWithEmbeddingAsync(query, topK, cancellationToken);
    }

    private async Task<IEnumerable<TextSearchDocument>> SearchWithEmbeddingAsync(string query, int topK, CancellationToken cancellationToken)
    {
        if (_documents.Count == 0)
            return Enumerable.Empty<TextSearchDocument>();

        // Generate embedding for the query
        var queryEmbeddingResult = await _embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken);
        var queryEmbedding = queryEmbeddingResult.Vector;

        // Calculate cosine similarity for each document and return top K
        var results = _documents
            .Select(doc => new
            {
                Document = doc,
                Score = CosineSimilarity(queryEmbedding.Span, doc.Embedding.Span)
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Document);

        return results;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> vector1, ReadOnlySpan<float> vector2)
    {
        float dot = 0, mag1 = 0, mag2 = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            dot += vector1[i] * vector2[i];
            mag1 += vector1[i] * vector1[i];
            mag2 += vector2[i] * vector2[i];
        }
        return dot / (MathF.Sqrt(mag1) * MathF.Sqrt(mag2));
    }
}
