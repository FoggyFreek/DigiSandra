---
name: test-patterns
description: Test framework, patterns, and conventions for DigiSandra. Use when writing or reviewing tests.
user-invocable: false
---

# DigiSandra Test Patterns

## Framework & Tools

- **xUnit** for test framework
- **Moq** for mocking interfaces
- **FluentAssertions** for readable assertions
- **coverlet** for code coverage
- Tests live in `tests/SchedulingAgent.Tests/`

## Test Patterns

- Arrange-Act-Assert pattern in all tests
- One assertion concern per test (multiple assertions ok if same concern)
- Mock all external dependencies via interfaces
- Test both happy path and error scenarios
- Test boundary conditions (empty lists, null responses, timeout)

### Mocking `IGraphService.CreateEventAsync`
The method has 8 parameters — always include `It.IsAny<RecurrenceInfo?>()` as arg 7:
```csharp
graphService.Setup(s => s.CreateEventAsync(
    It.IsAny<string>(), It.IsAny<string>(),
    It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
    It.IsAny<List<ResolvedParticipant>>(), It.IsAny<bool>(),
    It.IsAny<RecurrenceInfo?>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync("event-001");
```

## Naming

| Element | Convention |
|---------|-----------|
| Test class | `{ClassUnderTest}Tests` |
| Test method | `{Method}_{Scenario}_{Expected}` |

Example: `ProcessSchedulingRequestAsync_ValidRequest_ReturnsProposedSlots`
