---
name: ui-data-patterns
description: Adaptive Cards conventions, Cosmos DB design patterns, and OpenAI prompt conventions for DigiSandra. Use when building cards, working with Cosmos DB, or writing OpenAI prompts.
user-invocable: false
---

# DigiSandra UI & Data Patterns

## Adaptive Cards

- Build cards programmatically using `AdaptiveCards` SDK — never raw JSON strings
- All card action data payloads must include `requestId` for state tracking
- User-facing text in Dutch (nl-NL) locale
- Card schema version: 1.5
- Use `AdaptiveSubmitAction` with typed data objects
- Card builders live in `src/SchedulingAgent/Cards/`

## Cosmos DB Design

- **Partition key**: `/requestId` — all documents for a request co-located
- **TTL**: 7 days default (604800 seconds) — GDPR right to be forgotten
- **Document types**: `SchedulingRequest`, `ConflictResolutionState`, `AuditLog`
- **Concurrency**: ETag-based optimistic concurrency on all updates
- **Indexing**: Composite index on `documentType` + `createdAt` for queries
- **Connection mode**: Direct (for lower latency)
- Document classes use `Document` suffix: `SchedulingRequestDocument`

## OpenAI Prompt Conventions

- System prompts written in Dutch to match user interaction language
- Structured JSON output using `ChatResponseFormat.CreateJsonSchemaFormat()`
- Low temperature (0.1–0.2) for deterministic extraction
- Prompt templates stored as static classes in `src/SchedulingAgent/Prompts/`
- Schema validation enforced — reject hallucinated outputs
- `IntentExtractionPrompt.cs` handles NLP intent parsing, including optional `recurrence` extraction
- `ConflictResolutionPrompt.cs` handles conflict analysis

### Intent Extraction — Recurrence
- `recurrence` is an optional object in the JSON schema: `{ count, frequency, intervalWeeks }`
- `frequency` values: `"Weekly"`, `"BiWeekly"`, `"Monthly"` (match `RecurrenceFrequency` enum exactly)
- Single-meeting requests: `recurrence` field is omitted (null) — not `{ count: 1 }`
- `timeWindow` must always span the **full series range** so slot-finding covers all occurrences
- Example: "3 wekelijkse vergaderingen de komende maanden" → `count: 3, frequency: "Weekly", intervalWeeks: 1`, `timeWindow.endDate` ~3 weeks out
