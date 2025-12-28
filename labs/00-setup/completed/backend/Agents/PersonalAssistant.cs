using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AIAgent.Host.Agents;

public static class PersonalAssistant
{
    public static async Task<Microsoft.Agents.AI.AIAgent> CreateAsync(IChatClient chatClient, AppConfig appConfig,
        Microsoft.AspNetCore.Http.Json.JsonOptions jsonOptions)
    {
        var baseAgent = new ChatClientAgent(
            chatClient,
            instructions: """
            You are a helpful personal AI assistant.
            
            Your role is to:
            - Help users with their daily tasks
            - Provide friendly and conversational responses
            - Be polite and professional
            
            Introduce yourself as "AI-Me" - the user's personal digital assistant.
            """,
            name: "PersonalAssistant");

        return await Task.FromResult(baseAgent);
    }
}
