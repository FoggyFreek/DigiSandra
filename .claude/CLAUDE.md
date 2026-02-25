# DigiSandra

Azure AI Scheduling Agent for Microsoft Teams. Interprets meeting requests in Dutch/English, resolves participants via Entra ID, checks calendar availability via Microsoft Graph, resolves conflicts using AI reasoning, and books meetings autonomously — AVG/GDPR-compliant.

## Request Lifecycle
1. User sends natural language message in Teams
2. `/api/messages` → `SchedulingBot` → `SchedulingOrchestrator`
3. OpenAI extracts structured `MeetingIntent` (incl. optional `RecurrenceInfo`)
4. Graph resolves users/groups to `ResolvedParticipant` list
5. Graph `findMeetingTimes` (fallback: `getSchedule`) finds slots
6. `ConflictResolutionService` analyzes conflicts via AI decision matrix
7. Adaptive Card presents options → user selects starting slot
8. Graph creates event — recurring series (`PatternedRecurrence`) when `Recurrence` is set
9. `RequestSummaryDocument` written to Cosmos (duration, slotCount, conflictCount, fallback used, etc.)
10. `FeedbackCard` sent to requester — score 1–5 + optional improvement text
11. Audit log tracks all actions (metadata only)

## Recurring Meetings
- `MeetingIntent.Recurrence` carries `Count`, `Frequency` (`Weekly`/`BiWeekly`/`Monthly`), `IntervalWeeks`
- OpenAI extracts recurrence from phrases like "3 wekelijkse vergaderingen" / "3 weekly meetings"
- `timeWindow` spans the full range; slot-finding returns the best **starting** slot
- `GraphService.CreateEventAsync` builds `PatternedRecurrence` → Graph repeats the event N times
- Single-event requests: `Recurrence` is null, behaviour unchanged

## Conflict Resolution
- No slot → identify lowest-availability person → AI classifies conflict:
  - Informal internal → ProposeReschedule
  - External → Escalate (don't touch)
  - Focus Time → Respect, SuggestAlternativeSlot
  - All-day → SuggestAlternativeSlot
- `AskParticipant` → 1:1 chat, wait up to 4h → timeout → TimedOut

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8, Azure Functions v4 isolated |
| Bot | Bot Framework SDK 4.x |
| AI | Azure OpenAI (GPT-4o) |
| API | Microsoft Graph SDK v5 |
| Database | Azure Cosmos DB (serverless) |
| Identity | Microsoft Entra ID (OAuth 2.0 OBO) |
| IaC | Bicep |
| CI/CD | GitHub Actions |
| Monitoring | Application Insights + Log Analytics |
| Cards | AdaptiveCards SDK 3.x |

## Project Structure

```
src/SchedulingAgent/
├── Program.cs                        # Host builder, DI setup
├── Functions/                        # BotEndpoint, ConflictTimeout, HealthCheck
├── Bot/                              # Teams bot handlers
├── Services/                         # GraphService, OpenAIService, CosmosDbService,
│                                     # ConflictResolutionService, SchedulingOrchestrator
├── Models/
│   ├── MeetingIntent.cs              # Parsed NLP intent
│   ├── SchedulingRequestDocument.cs  # Request lifecycle + tracking flags
│   ├── ConflictResolutionStateDocument.cs
│   ├── AuditLogDocument.cs
│   └── RequestSummaryDocument.cs     # RequestSummaryDocument + FeedbackDocument
├── Cards/
│   ├── MeetingOptionsCard.cs         # Slot selection, confirmation, error
│   ├── DisambiguationCard.cs
│   ├── ConflictNotificationCard.cs
│   └── FeedbackCard.cs               # Post-booking 1–5 star rating + suggestion
├── Prompts/                          # OpenAI prompt templates
└── Extensions/ServiceCollectionExtensions.cs
tests/SchedulingAgent.Tests/
infra/                                # Bicep
.github/workflows/                    # ci.yml, deploy.yml
```

## Observability
- `RequestSummaryDocument` written on every terminal state (Completed/Failed/Cancelled) — co-located in Cosmos partition, 90-day TTL
- `FeedbackDocument` written when requester submits score — same partition, 90-day TTL
- Both summary fields and feedback score are also emitted as structured App Insights log properties for KQL queries:
  ```kusto
  traces
  | where message startswith "Request summary:"
  | project timestamp, requestId = tostring(customDimensions.RequestId),
      outcome = tostring(customDimensions.Outcome),
      durationSec = toint(customDimensions.DurationSeconds),
      slotCount = toint(customDimensions.SlotCount),
      conflictCount = toint(customDimensions.ConflictCount),
      usedFallback = tobool(customDimensions.UsedFallback)
  ```
- Cosmos document types: `SchedulingRequest` (7d TTL), `ConflictResolutionState` (7d TTL), `AuditLog` (7d TTL), `RequestSummary` (90d TTL), `Feedback` (90d TTL)

## Key Design Decisions
- **Primary constructors** for DI; **records** for DTOs
- **Structured OpenAI output**: JSON schema enforcement, temperature 0.1–0.2
- **Single Cosmos container**: partition by `requestId`, TTL 7 days request/audit, 90 days summary/feedback
- **Timer-based conflict expiry**: no complex messaging needed
- **Never log meeting content** — metadata only (request IDs, status, RU charges)
- **Recurring series as one Graph event**: `PatternedRecurrence` on the start slot — not N separate bookings
- **Feedback is non-blocking**: skip action is silent; errors in feedback saving are caught and logged, never shown to user

## Skills

| Skill | Description |
|-------|-------------|
| `coding-standards` | C# conventions, naming, error handling, logging, security, DI |
| `ui-data-patterns` | Adaptive Cards, Cosmos DB design, OpenAI prompt conventions |
| `test-patterns` | xUnit/Moq/FluentAssertions patterns and naming |
| `/run-tests [filter]` | Run the test suite |
| `/deploy [dev\|acc\|prod]` | Deploy via GitHub Actions |
