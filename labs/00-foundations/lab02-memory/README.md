# Lab 02: Agent with Memory

## Learning Objectives

- Understand why agents need memory
- Learn how to use `AIContextProvider` for long-term memory
- See how agents can extract and persist user information
- Experience personalized conversations based on stored context

## What You'll Build

A travel assistant agent that remembers the user's name across multiple messages and always addresses them by name. This demonstrates the core concept of agent memory without complexity.

The agent will:
- Ask for the user's name if not known
- Extract and remember the name from conversation
- Address the user by name in all subsequent responses

## Key Concepts

### Why Memory Matters

Without memory, agents are **stateless** - they forget everything between requests. This means:
- Users must repeat information in every message
- No personalization or context building
- Poor user experience for multi-turn conversations

With memory, agents can:
- Remember user preferences and context
- Provide personalized responses
- Build natural, flowing conversations

### AIContextProvider Pattern

`AIContextProvider` is the Microsoft Agent Framework's mechanism for memory:

1. **ProvideAIContextAsync** - Called BEFORE agent responds
   - Injects instructions and context into the agent's prompt
   - Tells agent what it knows about the user

2. **StoreAIContextAsync** - Called AFTER agent responds
   - Extracts new information from the conversation
   - Persists it to session state

3. **ProviderSessionState** - Session-scoped storage
   - Keeps data associated with a specific `AgentSession`
   - Survives across multiple messages in the same session

### Memory Lifecycle

```
User sends message
        ↓
ProvideAIContextAsync ← Adds remembered info to agent's context
        ↓
Agent processes and responds
        ↓
StoreAIContextAsync ← Extracts and saves new info
        ↓
Repeat...
```

## Code Walkthrough

### 1. TravelPreferences Class

Simple data model to store the user's name:

```csharp
internal sealed class TravelPreferences
{
    public string? UserName { get; set; }
}
```

### 2. TravelPreferencesMemory Provider

Extends `AIContextProvider` to implement memory logic:

```csharp
internal sealed class TravelPreferencesMemory : AIContextProvider
{
    private readonly ProviderSessionState<TravelPreferences> _sessionState;
    private readonly IChatClient _chatClient;
    
    // ... initialization ...
}
```

**Key components:**
- `_sessionState` - Stores preferences per session
- `_chatClient` - Used to call LLM for information extraction

### 3. ProvideAIContextAsync - Inject Context

```csharp
protected override ValueTask<AIContext> ProvideAIContextAsync(
    InvokingContext context, 
    CancellationToken cancellationToken = default)
{
    var preferences = this._sessionState.GetOrInitializeState(context.Session);
    
    // Build instructions based on what we know
    if (preferences.UserName is null)
    {
        instructions.AppendLine("Ask the user for their name...");
    }
    else
    {
        instructions.AppendLine($"The user's name is {preferences.UserName}. Always address them by name.");
    }
    
    // Return context that gets injected into agent's prompt
    return new ValueTask<AIContext>(new AIContext
    {
        Instructions = instructions.ToString()
    });
}
```

This tells the agent:
- What to ask for if name is unknown
- To address the user by name once known

### 4. StoreAIContextAsync - Extract & Save

```csharp
protected override async ValueTask StoreAIContextAsync(
    InvokedContext context, 
    CancellationToken cancellationToken = default)
{
    var preferences = this._sessionState.GetOrInitializeState(context.Session);
    
    // Only extract name if we don't have it
    if (preferences.UserName is null && context.RequestMessages.Any(x => x.Role == ChatRole.User))
    {
        // Use LLM to extract structured data from conversation
        var result = await this._chatClient.CompleteAsync<TravelPreferences>(
            context.RequestMessages,
            new ChatOptions()
            {
                Instructions = "Extract the user's name from the conversation if present..."
            });
        
        // Update stored name
        preferences.UserName ??= result.Result.UserName;
        
        // Save to session
        this._sessionState.SaveState(context.Session, preferences);
    }
}
```

Uses the LLM itself to extract the name from natural conversation!

### 5. Adding Memory to Agent

```csharp
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions { ... });

// Add memory provider
agent.AddAIContextProvider(travelMemory);
```

That's it! The agent now has memory.

## Running the Lab

```bash
cd labs/00-foundations/lab02-memory
dotnet run Program.cs
```

## Expected Output

The demo conversation shows:

1. **First message**: User says "Hi there!" → Agent asks for name
2. **User provides name**: "My name is Alex" → Agent acknowledges with name
3. **Subsequent questions**: Agent consistently addresses user as "Alex" in all responses
4. **Memory summary**: Shows the stored name at the end

## Experiment Ideas

1. **Add More Fields**: Extend TravelPreferences to remember
   - Preferred destination type (beach, mountain, city)
   - Budget level (budget, moderate, luxury)
   - Home city for distance calculations

2. **Improve Extraction**: Make name extraction more robust
   - Handle nicknames vs formal names
   - Support titles (Mr., Ms., Dr.)

3. **Add Validation**: Ensure quality
   - Verify name isn't empty or too long
   - Confirm extracted name with user

4. **Persistent Memory**: Save to file or database
   - Serialize TravelPreferences to JSON
   - Load on next run for true long-term memory
   - Associate with user ID for multi-user support

## Key Takeaways

- **AIContextProvider** enables session-scoped memory
- **ProvideAIContextAsync** injects context before agent responds
- **StoreAIContextAsync** extracts and persists info after response
- **LLMs can extract structured data** from natural conversation
- Memory enables **personalized, contextual conversations**

## Next Steps

Continue to [Lab 03: Agent with Tools](../lab03-tools/README.md) to learn how agents can take actions using function calling!

## Troubleshooting

**Agent doesn't remember information:**
- Make sure you're using the same `AgentSession` across messages
- Check that `AddAIContextProvider` was called
- Verify extraction instructions are clear

**Extraction not working:**
- Check the model supports structured output (`CompleteAsync<T>`)
- Make extraction instructions more specific
- Add examples to extraction prompt

**Session state lost:**
- Session state is in-memory - lost when program exits
- For persistence, implement file/database storage
