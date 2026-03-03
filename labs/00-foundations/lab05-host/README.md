# Lab 05: Hosting Agents as Web Services

In this lab, you will learn how to host an AI agent as a web service using ASP.NET Core, making it accessible via HTTP endpoints.

You will create a web API that exposes your agent through OpenAI-compatible endpoints, allowing it to be consumed by various clients including web applications, mobile apps, or other services.

By the end of this lab, you will:

- HOST an agent as an ASP.NET Core web service
- EXPOSE agents via OpenAI-compatible API endpoints
- CONFIGURE CORS and middleware for web access
- IMPLEMENT health checks and service discovery endpoints
- USE AGUI for interactive agent development and testing
- UNDERSTAND the difference between console apps and hosted services

## Project Structure

```
lab05-host/
├── Program.cs                      # Main application entry point
├── Lab05Host.csproj               # Project file with dependencies
├── appsettings.json               # App configuration
├── appsettings.Development.json   # Development configuration
├── Tools/
│   ├── DateTimeTools.cs          # Date and time utilities
│   └── FlightSearchTools.cs      # Flight search functionality
└── README.md                      # This file
```

## Concepts

### Why Host Agents as Web Services?

While console applications are great for learning and testing, production AI agents typically need to be:

- **Accessible remotely** - Multiple clients can interact with the agent over HTTP
- **Scalable** - Can handle multiple concurrent requests
- **Integrated** - Can be part of larger web applications or microservice architectures
- **Monitored** - Easy to add logging, metrics, and health checks
- **Standard** - OpenAI-compatible endpoints work with existing tools and SDKs

### Key Components

#### 1. ASP.NET Core Web Host
The foundation for hosting HTTP services in .NET. Provides:
- HTTP server (Kestrel)
- Configuration system
- Logging infrastructure
- Middleware pipeline

#### 2. OpenAI-Compatible Endpoint
The `MapOpenAIChatCompletions()` method exposes your agent at `/v1/chat/completions`, making it compatible with:
- OpenAI client libraries
- LangChain, LlamaIndex, and other frameworks
- Custom clients expecting OpenAI API format

#### 3. AGUI (Agent Development UI)
The `MapAGUI()` method creates an interactive web UI at a custom endpoint, providing:
- Real-time chat interface for testing
- Tool invocation visibility
- Session management
- Development and debugging capabilities

## Architecture

```
┌─────────────────────────────────────────────────────┐
│            ASP.NET Core Web Host                     │
│                                                      │
│  ┌────────────────┐      ┌────────────────┐        │
│  │  Health Check  │      │   AGUI UI      │        │
│  │   /health      │      │ /agent/travel  │        │
│  └────────────────┘      └────────────────┘        │
│                                                      │
│  ┌───────────────────────────────────────────┐     │
│  │     OpenAI Compatible Endpoint            │     │
│  │       /v1/chat/completions                │     │
│  │                                            │     │
│  │  ┌──────────────────────────────────┐   │     │
│  │  │         Travel Agent              │   │     │
│  │  │  - Instructions                   │   │     │
│  │  │  - Tools (Date, Flight Search)    │   │     │
│  │  │  - Session Management             │   │     │
│  │  └──────────────────────────────────┘   │     │
│  │              ↓                            │     │
│  │  ┌──────────────────────────────────┐   │     │
│  │  │       IChatClient                │   │     │
│  │  │  (Azure OpenAI / GitHub Models)  │   │     │
│  │  └──────────────────────────────────┘   │     │
│  └───────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────┘
```

## Instructions

### Step 1: Navigate to the Lab Folder

```bash
cd labs/00-foundations/lab05-host
```

### Step 2: Understand the Code Structure

The Program.cs file demonstrates the full lifecycle of hosting an agent:

1. **Environment Setup** - Load configuration from .env
2. **Web Application Builder** - Create ASP.NET Core host with logging and CORS
3. **Chat Client Creation** - Initialize Azure OpenAI or GitHub Models client
4. **Tool Creation** - Define tools the agent can use
5. **Agent Creation** - Create agent with instructions and tools
6. **Endpoint Mapping** - Expose agent via HTTP endpoints (health, AGUI, OpenAI API)
7. **Web Server Start** - Run and listen for requests

### Step 3: Review the Tools

Open [Tools/DateTimeTools.cs](Tools/DateTimeTools.cs) and [Tools/FlightSearchTools.cs](Tools/FlightSearchTools.cs) to see:
- How tools are organized in separate files
- Static methods with proper descriptions
- JSON-serialized return values

### Step 4: Run the Web Service

```bash
dotnet run
```

You should see output indicating the service has started:

```
=== Lab 05: Hosting Agents as Web Services ===

[INFO] Loaded environment from: ...\.env
[INFO] Using Azure OpenAI (or GitHub Models)
[INFO] Created 3 tools for the agent
[INFO] Travel Assistant Agent created successfully

======================================================================
HOST STARTED - Agent is now available at the following endpoints:
  Root:         http://localhost:5000/
  Health Check: http://localhost:5000/health
  Agent UI:     http://localhost:5000/agent/travel
  OpenAI API:   http://localhost:5000/v1/chat/completions
======================================================================

Press Ctrl+C to shut down
```

### Step 5: Test the Service

#### Option A: Using the AGUI Web Interface

1. Open your browser to: http://localhost:5000/agent/travel
2. You'll see an interactive chat interface
3. Try asking: "Find me flights from Melbourne to Tokyo for next Friday"
4. Observe the agent using tools in real-time

#### Option B: Using the Health Check

Open your browser or use curl:

```bash
curl http://localhost:5000/health
```

Response:
```json
{
  "status": "healthy",
  "timestamp": "2026-03-03T10:30:00Z",
  "service": "Travel Assistant API"
}
```

#### Option C: Using the OpenAI-Compatible API

Use curl to send a chat completion request:

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "messages": [
      {
        "role": "user",
        "content": "What is today's date and find flights from Sydney to London?"
      }
    ]
  }'
```

#### Option D: Using OpenAI Python Client

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="not-needed"  # Local host doesn't require key
)

response = client.chat.completions.create(
    model="not-used",  # Model is determined by the backend
    messages=[
        {"role": "user", "content": "Find me flights from Melbourne to Tokyo"}
    ]
)

print(response.choices[0].message.content)
```

### Step 6: Observe Tool Invocations

When you interact with the agent (via AGUI or API), notice:

- The agent automatically determines which tools to use
- Tool calls happen server-side (client doesn't need to know about tools)
- Results are seamlessly integrated into the conversation
- Multiple tools can be chained together

### Step 7: Stop the Service

Press `Ctrl+C` in the terminal to gracefully shut down the web service.

## Key Takeaways

1. **Simple Setup**: Creating an agent-powered web service is straightforward with ASP.NET Core and the Agent Framework.

2. **OpenAI Compatibility**: By exposing an OpenAI-compatible endpoint, your agent can work with any client that supports the OpenAI API format.

3. **Development UI**: AGUI provides a powerful interface for testing and debugging agents during development.

4. **Tool Organization**: Tools are organized in separate files for better maintainability.

5. **Production Ready**: This pattern scales to production with added middleware for authentication, rate limiting, logging, and monitoring.

## What's Next?

This lab shows the foundation of hosting agents as services. In a production environment, you would typically add:

- **Authentication & Authorization** - Secure your endpoints with API keys or OAuth
- **Rate Limiting** - Prevent abuse and manage costs
- **Request Validation** - Ensure proper input format
- **Comprehensive Logging** - Track usage and diagnose issues with Application Insights
- **Metrics & Monitoring** - Observe performance and health
- **Session Persistence** - Store conversation history across requests (see main backend app)
- **Load Balancing** - Distribute traffic across multiple instances

These concepts are explored in the main backend application (`src/backend/`).

## Common Issues

### Port Already in Use

If port 5000 is already in use, you can specify a different port:

```bash
dotnet run --urls http://localhost:5001
```

### Environment Variables Not Found

Make sure your `.env` file exists in the workspace root with the required variables:
- `AZURE_AI_SERVICES_ENDPOINT` and `AZURE_AI_SERVICES_KEY` for Azure OpenAI
- OR `GITHUB_TOKEN` and `USE_GITHUB_MODELS=true` for GitHub Models

### Tool Not Being Called

If the agent doesn't use tools as expected:
- Check the tool descriptions are clear and specific
- Ensure the user query clearly requires the tool's functionality
- Review the agent's instructions for conflicting guidance

## Additional Resources

- [ASP.NET Core Fundamentals](https://learn.microsoft.com/aspnet/core/fundamentals/)
- [OpenAI API Reference](https://platform.openai.com/docs/api-reference/chat)
- [Microsoft Agent Framework Documentation](https://github.com/microsoft/agents)
