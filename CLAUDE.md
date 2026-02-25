# DigiSandra – Azure AI Scheduling Agent

## Project Overview
Azure AI Scheduling Agent for Microsoft Teams. Intelligent meeting scheduling assistant that interprets natural language requests, analyzes availability via Microsoft Graph, resolves conflicts proactively, and books meetings autonomously.

## Tech Stack
- **Runtime**: .NET 8, Azure Functions v4 (isolated worker)
- **Bot Framework**: Microsoft Bot Framework SDK 4.x
- **AI**: Azure OpenAI Service (GPT-4o)
- **API**: Microsoft Graph SDK v5
- **Database**: Azure Cosmos DB (NoSQL)
- **Identity**: Microsoft Entra ID (OAuth 2.0 OBO flow)
- **IaC**: Bicep
- **CI/CD**: GitHub Actions

## Project Structure
```
src/SchedulingAgent/          # Main Azure Functions project
  Functions/                  # Azure Function endpoints (HTTP triggers, timers)
  Bot/                        # Teams bot activity handlers
  Services/                   # Business logic services
  Models/                     # Data models, DTOs, enums
  Cards/                      # Adaptive Card builders
  Prompts/                    # OpenAI prompt templates
  Extensions/                 # DI registration, config extensions
tests/SchedulingAgent.Tests/  # Unit and integration tests
infra/                        # Bicep templates
.github/workflows/            # CI/CD pipelines
```

## Coding Standards

### C# Conventions
- Use file-scoped namespaces (`namespace X;`)
- Use primary constructors for DI where possible
- Use `required` keyword for mandatory properties
- Use records for immutable DTOs and models
- Prefer `sealed` classes unless inheritance is intended
- Use expression-bodied members for single-line methods/properties
- Use `CancellationToken` on all async methods
- Async methods must end with `Async` suffix
- Use `ValueTask` over `Task` when method often completes synchronously

### Naming
- Interfaces: `I` prefix (e.g., `IGraphService`)
- Cosmos DB documents: suffix with `Document` (e.g., `SchedulingRequestDocument`)
- DTOs: no suffix, use records
- Constants: `PascalCase` static readonly fields
- Private fields: `_camelCase` with underscore prefix

### Error Handling
- Use typed exceptions for domain errors
- Wrap external service calls in try-catch with structured logging
- Graph API: implement retry with exponential backoff for throttling (429)
- Never swallow exceptions silently
- Use `ILogger<T>` for all logging (structured logging with message templates)

### Security
- Never log meeting content, only metadata
- All Graph calls use delegated permissions via OBO flow
- Validate all user input before processing
- Use parameterized queries for Cosmos DB
- Secrets in Azure Key Vault, referenced via app settings

### Testing
- Use xUnit for test framework
- Use Moq for mocking
- Test class naming: `{ClassUnderTest}Tests`
- Test method naming: `{Method}_{Scenario}_{Expected}`
- Arrange-Act-Assert pattern

### Adaptive Cards
- Build cards programmatically using AdaptiveCards SDK (not JSON strings)
- Card actions must include request context (requestId) in data payload
- All user-facing text must support Dutch (nl-NL) locale

### Cosmos DB
- Partition key: `/requestId`
- Use TTL (7 days default) for automatic cleanup
- Use ETags for optimistic concurrency
- Document types: SchedulingRequest, ConflictResolutionState, AuditLog

## Build & Run
```bash
cd src/SchedulingAgent
dotnet build
func start
```

## Test
```bash
dotnet test
```
