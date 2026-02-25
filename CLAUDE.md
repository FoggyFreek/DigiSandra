# DigiSandra – Azure AI Scheduling Agent

## Project Overview

Azure AI Scheduling Agent for Microsoft Teams. An intelligent scheduling assistant that:
- Interprets meeting requests via natural language (Dutch/English)
- Resolves participants through Microsoft Entra ID
- Analyzes calendar availability via Microsoft Graph
- Proactively resolves scheduling conflicts using AI reasoning
- Books meetings autonomously in Outlook calendars
- Operates AVG/GDPR-compliant within the Azure tenant

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
4. OpenAI extracts structured `MeetingIntent` from message
5. Graph resolves users/groups to `ResolvedParticipant` list
6. Graph `findMeetingTimes` (fallback: `getSchedule`) finds available slots
7. `ConflictResolutionService` analyzes conflicts via AI decision matrix
8. Adaptive Card presents options to user in Teams
9. User selects slot → Graph creates calendar event
10. Audit log tracks all actions (metadata only)

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
├── SchedulingAgent.sln             # Solution file
├── CLAUDE.md                       # This file - coding standards & guidance
├── README.md                       # User-facing documentation
├── .gitignore
│
├── src/SchedulingAgent/            # Main Azure Functions project
│   ├── Program.cs                  # Host builder, DI setup
│   ├── host.json                   # Functions host configuration
│   ├── local.settings.json         # Local dev settings (gitignored)
│   ├── SchedulingAgent.csproj      # Project file with dependencies
│   │
│   ├── Functions/                  # Azure Function endpoints
│   │   ├── BotEndpointFunction.cs  # POST /api/messages - bot entry point
│   │   ├── ConflictTimeoutFunction.cs  # Timer: check expired conflicts (every 15min)
│   │   └── HealthCheckFunction.cs  # GET /api/health
│   │
│   ├── Bot/                        # Teams bot handlers
│   │   ├── SchedulingBot.cs        # Main activity handler (messages + card actions)
│   │   └── AdapterWithErrorHandler.cs  # Bot adapter with error logging
│   │
│   ├── Services/                   # Business logic layer
│   │   ├── IGraphService.cs + GraphService.cs           # Microsoft Graph API calls
│   │   ├── IOpenAIService.cs + OpenAIService.cs         # Azure OpenAI integration
│   │   ├── ICosmosDbService.cs + CosmosDbService.cs     # State management
│   │   ├── IConflictResolutionService.cs + ConflictResolutionService.cs  # Conflict engine
│   │   └── ISchedulingOrchestrator.cs + SchedulingOrchestrator.cs       # Main orchestration
│   │
│   ├── Models/                     # Data models and configuration
│   │   ├── MeetingIntent.cs        # Parsed NLP intent (record)
│   │   ├── SchedulingRequestDocument.cs  # Cosmos DB: request lifecycle
│   │   ├── ConflictResolutionStateDocument.cs  # Cosmos DB: conflict state
│   │   ├── AuditLogDocument.cs     # Cosmos DB: audit trail
│   │   ├── ConflictAnalysis.cs     # AI conflict analysis result
│   │   └── Configuration.cs        # Options classes for DI
│   │
│   ├── Cards/                      # Adaptive Card builders
│   │   ├── MeetingOptionsCard.cs   # Slot selection + confirmation + error cards
│   │   └── ConflictNotificationCard.cs  # Conflict resolution card
│   │
│   ├── Prompts/                    # OpenAI prompt templates
│   │   ├── IntentExtractionPrompt.cs    # System prompt + JSON schema for intent
│   │   └── ConflictResolutionPrompt.cs  # System prompt + JSON schema for conflicts
│   │
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs  # DI registration for all services
│
├── tests/SchedulingAgent.Tests/    # Unit tests
│   ├── Services/
│   │   ├── SchedulingOrchestratorTests.cs
│   │   └── ConflictResolutionServiceTests.cs
│   └── Cards/
│       └── MeetingOptionsCardTests.cs
│
├── infra/                          # Bicep infrastructure
│   ├── main.bicep                  # Root template (orchestrates modules)
│   ├── main.bicepparam             # Parameter file
│   └── modules/
│       ├── functionApp.bicep       # Function App + Storage + Plan
│       ├── cosmosDb.bicep          # Cosmos DB account + database + container
│       ├── openAi.bicep            # Azure OpenAI + GPT-4o deployment
│       ├── botService.bicep        # Bot Service + Teams channel
│       └── monitoring.bicep        # App Insights + Log Analytics + Alerts
│
└── .github/workflows/
    ├── ci.yml                      # PR validation: build + test + Bicep lint
    └── deploy.yml                  # Deploy to DEV/ACC/PROD
```

## Coding Standards

### C# Conventions
- File-scoped namespaces: `namespace SchedulingAgent.Services;`
- Primary constructors for DI: `public sealed class MyService(IDep dep)`
- `required` keyword for mandatory properties on models
- Records for immutable DTOs and value objects
- `sealed` on all classes unless inheritance is specifically needed
- Expression-bodied members for single-line methods/properties
- `CancellationToken ct = default` on all async method signatures
- Async suffix on all async methods: `DoSomethingAsync()`

### Naming Conventions
| Element | Convention | Example |
|---------|-----------|---------|
| Interface | `I` prefix | `IGraphService` |
| Cosmos DB document | `Document` suffix | `SchedulingRequestDocument` |
| DTO / value object | Record, no suffix | `MeetingIntent`, `ProposedTimeSlot` |
| Configuration | `Options` suffix | `CosmosDbOptions` |
| Constants | PascalCase static readonly | `SectionName` |
| Private fields | `_camelCase` | `_logger`, `_container` |
| Test class | `{ClassUnderTest}Tests` | `SchedulingOrchestratorTests` |
| Test method | `{Method}_{Scenario}_{Expected}` | `ProcessSchedulingRequestAsync_ValidRequest_ReturnsProposedSlots` |

### Error Handling
- Typed exceptions for domain errors (`InvalidOperationException` with context)
- All external service calls (Graph, OpenAI, Cosmos) wrapped in try-catch
- Graph API throttling (429): retry with exponential backoff
- Cosmos DB conflicts: ETag-based optimistic concurrency checks
- Never swallow exceptions silently — always log and rethrow or handle
- Structured logging only: `logger.LogError(ex, "Message {Param}", value)`

### Logging Rules
- Use `ILogger<T>` everywhere — no static loggers
- Structured message templates with named parameters
- **NEVER** log meeting content, attendee lists, or message body
- **DO** log: request IDs, status transitions, error details, RU charges
- Log levels: Debug for service internals, Information for flow, Warning for degraded, Error for failures

### Security Requirements
- OAuth 2.0 OBO flow for all Graph API calls (delegated permissions)
- Secrets stored in Azure Key Vault, referenced via app settings
- TLS 1.2+ enforced on all endpoints
- Cosmos DB data encrypted at rest
- No meeting content in logs — metadata only (subject length, participant count)
- Input validation before passing to Graph/OpenAI

### Dependency Injection
- All services registered in `ServiceCollectionExtensions.AddSchedulingAgent()`
- Configuration bound to strongly-typed Options classes
- Singletons for: CosmosClient, GraphServiceClient, AzureOpenAIClient, all services
- Transient for: IBot (per-request bot instance)
- External clients (Graph, OpenAI, Cosmos) use `DefaultAzureCredential`

### Adaptive Cards
- Build cards programmatically using `AdaptiveCards` SDK — never raw JSON strings
- All card action data payloads must include `requestId` for state tracking
- User-facing text in Dutch (nl-NL) locale
- Card schema version: 1.5
- Use `AdaptiveSubmitAction` with typed data objects

### Cosmos DB Design
- **Partition key**: `/requestId` — all documents for a request co-located
- **TTL**: 7 days default (604800 seconds) — GDPR right to be forgotten
- **Document types**: `SchedulingRequest`, `ConflictResolutionState`, `AuditLog`
- **Concurrency**: ETag-based optimistic concurrency on all updates
- **Indexing**: Composite index on `documentType` + `createdAt` for queries
- **Connection mode**: Direct (for lower latency)

### OpenAI Prompts
- System prompts written in Dutch to match user interaction language
- Structured JSON output using `ChatResponseFormat.CreateJsonSchemaFormat()`
- Low temperature (0.1-0.2) for deterministic extraction
- Prompt templates stored as static classes in `/Prompts`
- Schema validation enforced — reject hallucinated outputs

## Testing

### Framework & Tools
- **xUnit** for test framework
- **Moq** for mocking interfaces
- **FluentAssertions** for readable assertions
- **coverlet** for code coverage

### Test Patterns
- Arrange-Act-Assert pattern in all tests
- One assertion concern per test (multiple assertions ok if same concern)
- Mock all external dependencies via interfaces
- Test both happy path and error scenarios
- Test boundary conditions (empty lists, null responses, timeout)

### Running Tests
```bash
# All tests
dotnet test

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Specific test class
dotnet test --filter "FullyQualifiedName~SchedulingOrchestratorTests"
```

## Environments & Deployment

### Environments
| Environment | Purpose | Hosting |
|-------------|---------|---------|
| DEV | Development & testing | Consumption Plan |
| ACC | Acceptance / staging | Consumption Plan |
| PROD | Production | Consumption Plan |

### GitHub Actions Workflows
- **ci.yml**: Runs on PR to `main` — build, test, Bicep validation
- **deploy.yml**: Runs on push to `main` or manual dispatch — deploy infra + app

### Required GitHub Secrets
```
AZURE_CLIENT_ID          # Service principal for OIDC auth
AZURE_TENANT_ID          # Entra ID tenant
AZURE_SUBSCRIPTION_ID    # Azure subscription
AZURE_RESOURCE_GROUP     # Target resource group
BOT_APP_ID               # Bot Framework app registration
BOT_APP_PASSWORD          # Bot Framework app secret
GRAPH_CLIENT_ID          # Graph API app registration
GRAPH_CLIENT_SECRET      # Graph API client secret
```

### Local Development
```bash
# Prerequisites: .NET 8 SDK, Azure Functions Core Tools v4

# Build
dotnet build

# Run locally
cd src/SchedulingAgent
func start

# Run tests
dotnet test
```

### Required Graph API Permissions
| Scope | Type | Purpose |
|-------|------|---------|
| Calendars.ReadWrite | Delegated | Read/write calendar events |
| Chat.Create | Delegated | Create 1:1 chats for conflict resolution |
| ChatMessage.Send | Delegated | Send conflict negotiation messages |
| User.Read.All | Application | Resolve users by display name |
| Group.Read.All | Application | Resolve groups and expand members |

## Key Design Decisions

1. **Azure Functions isolated worker** over in-process: better dependency control, .NET 8 native
2. **Primary constructors** for DI: concise, less boilerplate
3. **Records for DTOs**: immutability, value equality, `with` expressions
4. **Cosmos DB serverless**: cost-effective for variable workloads, no capacity planning
5. **Structured OpenAI output**: JSON schema enforcement prevents hallucination
6. **Timer-based conflict expiry**: simple, reliable, no complex messaging needed
7. **Adaptive Cards over plain text**: rich UI, action buttons, structured data display
8. **Single Cosmos container**: partition by requestId, document type discriminator — cost efficient
