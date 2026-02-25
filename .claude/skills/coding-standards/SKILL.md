---
name: coding-standards
description: C# coding conventions, naming rules, error handling, logging, security requirements, and dependency injection patterns for DigiSandra. Use when writing or reviewing code in this project.
user-invocable: false
---

# DigiSandra Coding Standards

## C# Conventions

- File-scoped namespaces: `namespace SchedulingAgent.Services;`
- Primary constructors for DI: `public sealed class MyService(IDep dep)`
- `required` keyword for mandatory properties on models
- Records for immutable DTOs and value objects
- `sealed` on all classes unless inheritance is specifically needed
- Expression-bodied members for single-line methods/properties
- `CancellationToken ct = default` on all async method signatures
- Async suffix on all async methods: `DoSomethingAsync()`

## Naming Conventions

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

## Error Handling

- Typed exceptions for domain errors (`InvalidOperationException` with context)
- All external service calls (Graph, OpenAI, Cosmos) wrapped in try-catch
- Graph API throttling (429): retry with exponential backoff
- Cosmos DB conflicts: ETag-based optimistic concurrency checks
- Never swallow exceptions silently — always log and rethrow or handle
- Structured logging only: `logger.LogError(ex, "Message {Param}", value)`

## Logging Rules

- Use `ILogger<T>` everywhere — no static loggers
- Structured message templates with named parameters
- **NEVER** log meeting content, attendee lists, or message body
- **DO** log: request IDs, status transitions, error details, RU charges
- Log levels: Debug for service internals, Information for flow, Warning for degraded, Error for failures

## Security Requirements

- OAuth 2.0 OBO flow for all Graph API calls (delegated permissions)
- Secrets stored in Azure Key Vault, referenced via app settings
- TLS 1.2+ enforced on all endpoints
- Cosmos DB data encrypted at rest
- No meeting content in logs — metadata only (subject length, participant count)
- Input validation before passing to Graph/OpenAI

## Dependency Injection

- All services registered in `ServiceCollectionExtensions.AddSchedulingAgent()`
- Configuration bound to strongly-typed Options classes
- Singletons for: CosmosClient, GraphServiceClient, AzureOpenAIClient, all services
- Transient for: IBot (per-request bot instance)
- External clients (Graph, OpenAI, Cosmos) use `DefaultAzureCredential`
