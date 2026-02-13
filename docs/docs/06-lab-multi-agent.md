# Lab 6: Specialist Team - Multi-Agent Systems

In this lab, you'll transform your single-agent travel assistant into a multi-agent system with specialized agents working together.

Each agent will have deep expertise in one area while seamlessly collaborating to deliver a better user experience.

By the end of this lab, you will:

- ✅ Build a multi-agent system with specialized agents for different tasks
- ✅ Implement intelligent routing between agents
- ✅ Maintain context across agent handoffs

---

## The Problem: A Single Agent Trying to Do Everything

Your current travel agent handles everything - from recommending destinations to searching flights. While this works, it has limitations:

**1. Complex Instructions**: The agent's system prompt contains detailed workflows for multiple domains, making it difficult to maintain and optimize.

**2. Conflicting Priorities**: Instructions for different tasks can interfere with each other, leading to suboptimal performance.

**3. Limited Scalability**: Adding new capabilities (hotels, car rentals, activities) makes the agent increasingly complex.

**4. No Specialization**: A generalist agent cannot match the depth of expertise that specialized agents can provide.

```text

👤 User: I want to plan a trip to New Zealand for hiking

```

```text

🤖 Single Agent: [Processing with 200+ lines of instructions covering destinations, 
                 flights, and all other tasks]
                 
    ❌ Complex system prompts
    ❌ Harder to maintain
    ❌ Less specialized expertise

```

---

## Sample Interaction: What You'll Build

With a multi-agent system, specialized agents handle specific tasks while a triage agent routes requests intelligently:


```text

👤 User: Can you search for flights to Wellington next month?
```

```text

🔀 Triage Agent → Routes to FlightSearchAgent

```

```text
🔧 Flight Search Agent calls:
    • GetUserContext() - retrieves home city
    • ValidateTravelDates(startDate=2026-02-05, endDate=2026-02-26)
    • SearchFlights(origin=Melbourne, destination=Auckland, ...)
```

```text

🤖 Flight Search Agent: I found a flight option for your trip from Melbourne to Wellington, leaving on Friday, February 9th, and returning after two weeks:

        Flight: Qantas Flight QF107
        Departure: Friday, February 9th, 11:30 AM
        Arrival: Friday, February 9th, 6:15 PM
        Duration: 6h 45m (Direct)
        Price: $1,580 AUD
        Amenities: WiFi, in-flight entertainment, vegetarian meals, power outlets, USB charging

        Would you like to see more options or proceed with this one?

```

```text
🔀 Flight Search Agent → Hands back to Triage Agent
```

---

## Instructions

### Step 1: Understand the Workflow Structure

The multi-agent system uses **handoff-based routing**: a central triage agent routes user requests to specialized agents based on intent. Then, specialists hand back to triage when their work is complete.

```csharp
var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
    // Triage → Specialists
    .WithHandoff(triageAgent, tripAdvisorAgent, "routing condition...")
    .WithHandoff(triageAgent, flightSearchAgent, "routing condition...")
    
    // Specialists → Triage (when work complete)
    .WithHandoff(tripAdvisorAgent, triageAgent, "completion condition...")
    .WithHandoff(flightSearchAgent, triageAgent, "completion condition...")
    .Build();
```

### Step 2: Locate the Multi-Agent Source Code

Navigate to the workflow agents directory:

```text

📁 src/backend/Agents/Workflow/
   ├── ContosoTravelWorkflowAgentFactory.cs  (Orchestrator)
   ├── TriageAgentFactory.cs                 (Routing coordinator)
   ├── TripAdvisorAgentFactory.cs            (Destination specialist)
   ├── FlightSearchAgentFactory.cs           (Flight specialist)

```

### Step 3: Review the Triage Agent

Open [Agents/Workflow/TriageAgentFactory.cs](../../src/backend/Agents/Workflow/TriageAgentFactory.cs):

```csharp
public static AIAgent Create(
    IChatClient chatClient,
    IHttpContextAccessor httpContextAccessor,
    JsonSerializerOptions jsonSerializerOptions)
{
    return chatClient.CreateAIAgent(new ChatClientAgentOptions
    {
        Name = "triage_agent",
        Description = "Routes travel requests to the appropriate specialist agent",
        ChatOptions = new()
        {
            Instructions = """
            ## Your Role
            - You route ALL requests to the appropriate specialist agents
            - Specialists complete their work and return control to YOU
            - You then decide which specialist should handle the next step
            - You coordinate the entire travel planning workflow
            
            ## Important
            - Route immediately based on user intent - don't answer travel questions yourself
            - Make transitions seamless - the user should experience one continuous conversation
            - Specialists will return to you automatically when their work is complete
            """
        }
    });
}
```

### Step 4: Review Specialist Agents

Each specialist agent is focused on one domain:

**Trip Advisor Agent** - [TripAdvisorAgentFactory.cs](../../src/backend/Agents/Workflow/TripAdvisorAgentFactory.cs)

```csharp
Name = "trip_advisor_agent"
Description = "Expert trip advisor who recommends travel destinations..."
Tools = [
    GetCurrentDate,
    CalculateDateDifference,
    ValidateTravelDates,
    GetUserContext  // Profile and preferences
]
```

**Flight Search Agent** - [FlightSearchAgentFactory.cs](../../src/backend/Agents/Workflow/FlightSearchAgentFactory.cs)

```csharp
Name = "flight_search_agent"
Description = "Searches and recommends flights for a chosen destination..."
Tools = [
    GetUserContext,
    GetCurrentDate,
    ValidateTravelDates,
    SearchFlights  // External flight API
]
```

### Step 5: Enable Multi-Agent System

Open [Program.cs](../../src/backend/Program.cs) and review the agent registration. Replace the following line to use the multi-agent workflow:

```csharp
// Change this line:
app.MapAGUI("/agent/contoso_travel_bot", travelBot);
// To this:
app.MapAGUI("/agent/contoso_travel_bot", travelBotWorkflowAgent);
```

---

## Key Implementation Details

### 1. Handoff Routing Conditions

The workflow defines when to route between agents:

```csharp
// Triage → Trip Advisor: User needs destination recommendations
.WithHandoff(triageAgent, tripAdvisorAgent,
    "User needs expert trip advisor to recommend travel destinations based on " +
    "their preferences, budget, and interests. User asks generic travel questions " +
    "or wants personalized destination recommendations.")

// Triage → Flight Search: Destination selected, need flights
.WithHandoff(triageAgent, flightSearchAgent,
    "Destination has been selected and user wants to search for flights, " +
    "discuss travel dates, or asks about flight options, prices, or schedules.")

```

### 2. Return Conditions

The specialized agents focus on their tasks and return control to the triage agent for further routing. Each agent has access to the complete chat history and can decide when to hand back control.

```csharp
// Trip Advisor → Triage: Destination chosen
.WithHandoff(tripAdvisorAgent, triageAgent,
    "After providing personalized destination recommendations and successfully " +
    "helping user choose and confirm their ideal vacation destination.")

// Flight Search → Triage: Flights presented
.WithHandoff(flightSearchAgent, triageAgent,
    "After presenting flight options and discussing travel dates with the user, " +
    "once they have identified suitable flights.")

// Calendar → Triage: Event added
.WithHandoff(calendarAgent, triageAgent,
    "After successfully adding the trip to the user's calendar with reminders, " +
    "completing the trip planning process.")
```

---

## Test Your Implementation

1. Refer to the **Running the Application Locally** section in the [Setup Instructions](00-setup_instructions.md#running-the-application-locally) and start running both the backend and frontend.

    If the application is already running, restart the backend server by stopping it (Ctrl + C) and running `dotnet run` again in the `src/backend` directory.

2. **Test the multi-agent workflow**:

   **Prompt 1**: Test Trip Advisor routing

   ```text

    Hi! I want to plan my next vacation
   
   ```
   
   *Expected Response*:
   
   - Triage routes to Trip Advisor Agent
   - Agent calls GetUserContext(), GetCurrentDate()
   - Provides 3-4 destination recommendations
   - Has natural conversation about options and suggests a destination

   **Prompt 2**: Test Flight Search routing

   ```text
   
   Can you search for flights to Wellington next month?
   
   ```

---

## Next Steps

Congratulations! You've built a sophisticated multi-agent system with specialized agents collaborating to deliver an excellent user experience.

This architecture provides a solid foundation for building complex, scalable AI applications.

👉 **[Finishing Up](finishing-up.md)**
