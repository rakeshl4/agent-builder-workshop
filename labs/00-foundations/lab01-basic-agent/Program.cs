// Lab 01: Basic Agent
// Learn how to create and run a simple AI agent using Microsoft Agent Framework

// Add NuGet package references using file-based app syntax (#:package Name@Version)
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

Console.WriteLine("=== Lab 01: Basic Agent ===\n");

// Step 1: Load environment variables from the workspace root .env file
LoadEnv();

// Step 2: Create chat client
var chatClient = CreateChatClient();
if (chatClient == null) return;

// Step 3: Create a simple agent
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name = "TravelAssistant",
    ChatOptions = new()
    {
        Instructions = "You are a helpful travel assistant that provides travel recommendations and information. " +
                      "Be friendly, informative, and concise in your responses.",
        Tools = []
    }
});

Console.WriteLine("[SUCCESS] Agent created successfully\n");

// Step 4: Run the agent with multi-turn conversation
try
{
    // Create a new session for the conversation.
    AgentSession session = await agent.CreateSessionAsync();

    // First message
    Console.WriteLine("User: What are some must-visit places in Australia?\n");
    var response1 = await agent.RunAsync("What are some must-visit places in Australia?", session);
    Console.WriteLine($"Agent: {response1}\n");

    // Second message - follow-up question to demonstrate multi-turn chat
    Console.WriteLine("User: Which one would you recommend for families with kids?\n");
    var response2 = await agent.RunAsync("Which one would you recommend for families with kids?", session);
    Console.WriteLine($"Agent: {response2}");
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Failed to get response: {ex.Message}");
    Console.WriteLine($"Details: {ex}");
}

Console.WriteLine("\n" + new string('=', 60));


// ==================== Helper Methods ====================

IChatClient? CreateChatClient()
{
    var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_SERVICES_ENDPOINT");
    var azureApiKey = Environment.GetEnvironmentVariable("AZURE_AI_SERVICES_KEY");
    var modelName = Environment.GetEnvironmentVariable("AZURE_TEXT_MODEL_NAME") ?? "gpt-4o";

    var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    var githubModelId = Environment.GetEnvironmentVariable("GITHUB_TEXT_MODEL_ID") ?? "gpt-4o";
    var githubBaseUrl = Environment.GetEnvironmentVariable("GITHUB_MODELS_BASE_URL") ?? "https://models.inference.ai.azure.com";

    if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureApiKey))
    {
        Console.WriteLine($"Using Azure OpenAI ({modelName})\n");
        var azureClient = new AzureOpenAIClient(new Uri(azureEndpoint), new ApiKeyCredential(azureApiKey));
        return azureClient.GetChatClient(modelName).AsIChatClient();
    }
    else if (!string.IsNullOrEmpty(githubToken))
    {
        Console.WriteLine($"Using GitHub Models ({githubModelId})\n");
        var githubClient = new AzureOpenAIClient(new Uri(githubBaseUrl), new ApiKeyCredential(githubToken));
        return githubClient.GetChatClient(githubModelId).AsIChatClient();
    }
    else
    {
        Console.WriteLine("ERROR: No valid credentials found.");
        Console.WriteLine("Configure AZURE_AI_SERVICES_ENDPOINT + AZURE_AI_SERVICES_KEY or GITHUB_TOKEN");
        return null;
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