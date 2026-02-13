using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

namespace ContosoTravelAgent.Host.Services;

public sealed class CompositeMemoryProvider : AIContextProvider
{
    private readonly AIContextProvider[] _providers;
    private readonly ILogger<CompositeMemoryProvider>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeMemoryProvider"/> class.
    /// </summary>
    /// <param name="providers">The collection of providers to chain together.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public CompositeMemoryProvider(
        IEnumerable<AIContextProvider> providers,
        ILoggerFactory? loggerFactory = null)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _logger = loggerFactory?.CreateLogger<CompositeMemoryProvider>();

        if (_providers.Length == 0)
        {
            throw new ArgumentException("At least one provider must be specified.", nameof(providers));
        }
    }

    /// <summary>
    /// Called before agent invocation - allows each provider to inject their context.
    /// </summary>
    public override async ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var allMessages = new List<ChatMessage>();
        var allInstructions = new StringBuilder();

        // Call each provider's InvokingAsync in sequence
        // Each provider can add messages to the context
        foreach (var provider in _providers)
        {
            try
            {
                var providerContext = await provider.InvokingAsync(context, cancellationToken);

                if (providerContext?.Messages?.Any() == true)
                {
                    // Merge messages from each provider
                    allMessages.AddRange(providerContext.Messages);

                    if (_logger?.IsEnabled(LogLevel.Debug) is true)
                    {
                        _logger.LogDebug(
                            "Provider {ProviderType} added {MessageCount} context messages",
                            provider.GetType().Name,
                            providerContext.Messages.Count());
                    }
                }

                if (providerContext?.Instructions?.Any() == true)
                {
                    // Merge messages from each provider
                    allInstructions.Append(providerContext.Instructions);

                    if (_logger?.IsEnabled(LogLevel.Debug) is true)
                    {
                        _logger.LogDebug(
                            "Provider {ProviderType} added {InstructionCount} context instructions",
                            provider.GetType().Name,
                            providerContext.Instructions.Count());
                    }
                }

            }
            catch (Exception ex)
            {
                if (_logger?.IsEnabled(LogLevel.Error) is true)
                {
                    _logger.LogError(
                        ex,
                        "Error invoking provider {ProviderType} during InvokingAsync",
                        provider.GetType().Name);
                }
                // Continue with other providers even if one fails
            }
        }

        var instructions = allInstructions.ToString();
        return new AIContext
        {
            Instructions = instructions,
            Messages = allMessages
        };
    }

    /// <summary>
    /// Called after agent invocation - allows each provider to store/update memories.
    /// </summary>
    public override async ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Call each provider's InvokedAsync in sequence
        // Each provider can process and store the conversation
        foreach (var provider in _providers)
        {
            try
            {
                await provider.InvokedAsync(context, cancellationToken);

                if (_logger?.IsEnabled(LogLevel.Debug) is true)
                {
                    _logger.LogDebug(
                        "Provider {ProviderType} processed InvokedAsync successfully",
                        provider.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                if (_logger?.IsEnabled(LogLevel.Error) is true)
                {
                    _logger.LogError(
                        ex,
                        "Error invoking provider {ProviderType} during InvokedAsync",
                        provider.GetType().Name);
                }
                // Continue with other providers even if one fails
            }
        }
    }

    /// <summary>
    /// Serializes the state of all contained providers.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var providerStates = _providers
            .Select(p => p.Serialize(jsonSerializerOptions))
            .ToArray();

        return JsonSerializer.SerializeToElement(
            new { Providers = providerStates },
            jsonSerializerOptions);
    }
}
