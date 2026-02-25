---
name: project-context
description: DigiSandra project architecture, tech stack, project structure, request lifecycle, and key design decisions. Use when working on this codebase, asking about how the system works, or making architectural decisions.
user-invocable: false
---

# DigiSandra – Azure AI Scheduling Agent

Azure AI Scheduling Agent for Microsoft Teams. Interprets meeting requests via natural language (Dutch/English), resolves participants through Microsoft Entra ID, analyzes calendar availability via Microsoft Graph, proactively resolves scheduling conflicts using AI reasoning, books meetings autonomously in Outlook calendars, and operates AVG/GDPR-compliant within the Azure tenant.

## Architecture

```
User (Teams)
   ↓
Teams Channel
   ↓
Azure Bot Service (Message routing)
   ↓
Azure Functions v4 (Orchestrator + Business Logic)
   ├── Azure OpenAI GPT-4o (Intent parsing + Conflict reasoning)
   ├── Microsoft Graph API (Calendar, Users, Chat, Events)
   └── Cosmos DB (State + Audit trail)
```

**Style**: Event-driven orchestration with AI decision layer

### Request Lifecycle
1. User sends natural language message in Teams
2. Bot endpoint (`/api/messages`) receives the activity
3. `SchedulingBot` delegates to `SchedulingOrchestrator`
4. OpenAI extracts structured `MeetingIntent` from message (incl. optional `RecurrenceInfo`)
5. Graph resolves users/groups to `ResolvedParticipant` list
6. Graph `findMeetingTimes` (fallback: `getSchedule`) finds available slots
7. `ConflictResolutionService` analyzes conflicts via AI decision matrix
8. Adaptive Card presents options to user in Teams
9. User selects starting slot → Graph creates calendar event
10. When `MeetingIntent.Recurrence` is set, event is booked as a recurring series (`PatternedRecurrence`)
11. Audit log tracks all actions (metadata only)

### Recurring Meetings
- `MeetingIntent.Recurrence` is an optional `RecurrenceInfo` record: `Count`, `Frequency`, `IntervalWeeks`
- `RecurrenceFrequency` enum: `Weekly`, `BiWeekly`, `Monthly`
- OpenAI extracts recurrence from natural language: "3 weekly meetings" → `count: 3, frequency: Weekly, intervalWeeks: 1`
- `timeWindow` is set to span the full series range; slot-finding returns the best **starting** slot
- `GraphService.BuildRecurrence()` maps to `PatternedRecurrence` with day-of-week pinned to the selected slot's weekday
- `DayOfWeekObject` (not `DayOfWeek`) is the Graph SDK v5 type for recurrence day values
- Single-event requests: `Recurrence` is null, no `PatternedRecurrence` is set

### Conflict Resolution Flow
- No slot available → identify lowest-availability person
- AI analyzes conflict type against decision matrix:
  - Informal internal → ProposeReschedule
  - External meeting → Escalate (don't touch)
  - Focus Time → Respect, SuggestAlternativeSlot
  - All-day event → SuggestAlternativeSlot
- If `AskParticipant`: create 1:1 chat, send message, wait up to 4h
- Timeout → fallback scenario (mark as TimedOut)

## Tech Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Runtime | .NET 8, Azure Functions v4 isolated | Business logic + API hosting |
| Bot | Bot Framework SDK 4.x | Teams message routing |
| AI | Azure OpenAI (GPT-4o) | NLP intent parsing, conflict reasoning |
| API | Microsoft Graph SDK v5 | Calendar, users, chat, events |
| Database | Azure Cosmos DB (serverless) | State management, audit trail |
| Identity | Microsoft Entra ID | OAuth 2.0 OBO authentication |
| IaC | Bicep | Infrastructure as Code |
| CI/CD | GitHub Actions | Build, test, deploy pipelines |
| Monitoring | Application Insights + Log Analytics | Observability, alerting |
| Cards | AdaptiveCards SDK 3.x | Rich Teams UI |

## Project Structure

```
DigiSandra/
├── SchedulingAgent.sln
├── src/SchedulingAgent/            # Main Azure Functions project
│   ├── Program.cs                  # Host builder, DI setup
│   ├── Functions/                  # Azure Function endpoints
│   │   ├── BotEndpointFunction.cs  # POST /api/messages
│   │   ├── ConflictTimeoutFunction.cs  # Timer: check expired conflicts
│   │   └── HealthCheckFunction.cs  # GET /api/health
│   ├── Bot/                        # Teams bot handlers
│   ├── Services/                   # Business logic layer
│   │   ├── GraphService.cs         # Microsoft Graph API calls
│   │   ├── OpenAIService.cs        # Azure OpenAI integration
│   │   ├── CosmosDbService.cs      # State management
│   │   ├── ConflictResolutionService.cs  # Conflict engine
│   │   └── SchedulingOrchestrator.cs     # Main orchestration
│   ├── Models/                     # Data models and configuration
│   ├── Cards/                      # Adaptive Card builders
│   ├── Prompts/                    # OpenAI prompt templates
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs
├── tests/SchedulingAgent.Tests/    # Unit tests
├── infra/                          # Bicep infrastructure
└── .github/workflows/
    ├── ci.yml                      # PR: build + test + Bicep lint
    └── deploy.yml                  # Deploy to DEV/ACC/PROD
```

## Key Design Decisions

1. **Azure Functions isolated worker** over in-process: better dependency control, .NET 8 native
2. **Primary constructors** for DI: concise, less boilerplate
3. **Records for DTOs**: immutability, value equality, `with` expressions
4. **Cosmos DB serverless**: cost-effective for variable workloads, no capacity planning
5. **Structured OpenAI output**: JSON schema enforcement prevents hallucination
6. **Timer-based conflict expiry**: simple, reliable, no complex messaging needed
7. **Adaptive Cards over plain text**: rich UI, action buttons, structured data display
8. **Single Cosmos container**: partition by requestId, document type discriminator — cost efficient
9. **Recurring series as one Graph event**: `PatternedRecurrence` on the chosen start slot — not N separate bookings; simpler audit trail and cancellation

## Required Graph API Permissions

| Scope | Type | Purpose |
|-------|------|---------|
| Calendars.ReadWrite | Delegated | Read/write calendar events |
| Chat.Create | Delegated | Create 1:1 chats for conflict resolution |
| ChatMessage.Send | Delegated | Send conflict negotiation messages |
| User.Read.All | Application | Resolve users by display name |
| Group.Read.All | Application | Resolve groups and expand members |
