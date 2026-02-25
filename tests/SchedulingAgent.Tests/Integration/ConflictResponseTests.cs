using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Scenario 5: Conflict response handling.
/// Tests the full lifecycle after negotiation messages are sent:
/// accept, decline, and timeout responses across multiple participants.
/// </summary>
public sealed class ConflictResponseTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly ConflictResolutionService _conflictService;

    public ConflictResponseTests()
    {
        _mocks.ConfigureDefaults();

        _conflictService = new ConflictResolutionService(
            _mocks.OpenAI.Object,
            _mocks.Graph.Object,
            _mocks.CosmosDb.Object,
            Options.Create(new ConflictResolutionOptions { TimeoutHours = 4, MaxRetries = 3 }),
            Mock.Of<ILogger<ConflictResolutionService>>());
    }

    // ──────────────────────────────────────────────
    // Scenario 5a: Jan accepts the reschedule request
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictResponse_JanAccepts_StateUpdated_AuditLogged()
    {
        // ── Arrange: Jan has a pending conflict resolution ──
        var requestId = "req-conflict-accept";
        var conflictState = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Jan.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Jan.UserId,
            ConflictUserName = TestPersonas.Jan.DisplayName,
            ChatId = $"chat-{TestPersonas.Jan.UserId}",
            OriginalEventSubject = "Wekelijks marketingoverleg",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(13),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(14),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(4)
        };
        _mocks.InjectConflictState(conflictState);

        // ── Act: Jan responds "ja" → Accepted ──
        await _conflictService.HandleConflictResponseAsync(
            requestId, TestPersonas.Jan.UserId, ConflictResponse.Accepted);

        // ── Assert: State updated ──
        _mocks.ConflictStatesUpdated.Should().HaveCount(1);
        var updated = _mocks.ConflictStatesUpdated[0];
        updated.PendingResponse.Should().BeFalse();
        updated.ResponseReceived.Should().Be(ConflictResponse.Accepted);
        updated.ConflictUserId.Should().Be(TestPersonas.Jan.UserId);

        // Verify: Audit log records the acceptance
        _mocks.AuditLogs.Should().HaveCount(1);
        var auditLog = _mocks.AuditLogs[0];
        auditLog.Action.Should().Be("ConflictResponseReceived");
        auditLog.ActorId.Should().Be(TestPersonas.Jan.UserId);
        auditLog.ActorType.Should().Be(ActorType.User);
        auditLog.Details["response"].Should().Be("Accepted");
    }

    // ──────────────────────────────────────────────
    // Scenario 5b: Pieter declines the reschedule request
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictResponse_PieterDeclines_StateUpdated_AuditLogged()
    {
        var requestId = "req-conflict-decline";
        var conflictState = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Pieter.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Pieter.UserId,
            ConflictUserName = TestPersonas.Pieter.DisplayName,
            ChatId = $"chat-{TestPersonas.Pieter.UserId}",
            OriginalEventSubject = "Code review sessie",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(14),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(15),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(4)
        };
        _mocks.InjectConflictState(conflictState);

        // ── Act ──
        await _conflictService.HandleConflictResponseAsync(
            requestId, TestPersonas.Pieter.UserId, ConflictResponse.Declined);

        // ── Assert ──
        _mocks.ConflictStatesUpdated.Should().HaveCount(1);
        var updated = _mocks.ConflictStatesUpdated[0];
        updated.PendingResponse.Should().BeFalse();
        updated.ResponseReceived.Should().Be(ConflictResponse.Declined);

        _mocks.AuditLogs.Should().HaveCount(1);
        _mocks.AuditLogs[0].Details["response"].Should().Be("Declined");
    }

    // ──────────────────────────────────────────────
    // Scenario 5c: Both Jan and Pieter have expired conflicts
    // Timer function processes them as timed out
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictTimeout_MultipleExpired_AllMarkedTimedOut()
    {
        // ── Arrange: Both Jan and Pieter's conflicts have expired ──
        var requestId = "req-multi-timeout";

        var janExpired = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Jan.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Jan.UserId,
            ConflictUserName = TestPersonas.Jan.DisplayName,
            ChatId = $"chat-{TestPersonas.Jan.UserId}",
            OriginalEventSubject = "Wekelijks marketingoverleg",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(13),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(14),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(-1) // Expired 1 hour ago
        };

        var pieterExpired = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Pieter.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Pieter.UserId,
            ConflictUserName = TestPersonas.Pieter.DisplayName,
            ChatId = $"chat-{TestPersonas.Pieter.UserId}",
            OriginalEventSubject = "Code review sessie",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(14),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(15),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(-2) // Expired 2 hours ago
        };

        _mocks.InjectConflictState(janExpired);
        _mocks.InjectConflictState(pieterExpired);

        // ── Act: Timer function processes expired conflicts ──
        await _conflictService.ProcessExpiredConflictsAsync();

        // ── Assert: Both marked as TimedOut ──
        _mocks.ConflictStatesUpdated.Should().HaveCount(2);

        var janUpdated = _mocks.ConflictStatesUpdated.First(s => s.ConflictUserId == TestPersonas.Jan.UserId);
        janUpdated.PendingResponse.Should().BeFalse();
        janUpdated.ResponseReceived.Should().Be(ConflictResponse.TimedOut);

        var pieterUpdated = _mocks.ConflictStatesUpdated.First(s => s.ConflictUserId == TestPersonas.Pieter.UserId);
        pieterUpdated.PendingResponse.Should().BeFalse();
        pieterUpdated.ResponseReceived.Should().Be(ConflictResponse.TimedOut);

        // ── Assert: Audit logs for both timeouts ──
        _mocks.AuditLogs.Should().HaveCount(2);
        _mocks.AuditLogs.Should().OnlyContain(a => a.Action == "ConflictResolutionTimedOut");
        _mocks.AuditLogs.Select(a => a.Details["conflictUserId"]).Should().BeEquivalentTo(
        [
            TestPersonas.Jan.UserId,
            TestPersonas.Pieter.UserId
        ]);
    }

    // ──────────────────────────────────────────────
    // Scenario 5d: Mixed responses — Jan accepts, Pieter times out
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictResponse_MixedResponses_JanAcceptsPieterTimesOut()
    {
        var requestId = "req-mixed-responses";

        // Jan's conflict (still active)
        var janState = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Jan.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Jan.UserId,
            ConflictUserName = TestPersonas.Jan.DisplayName,
            ChatId = $"chat-{TestPersonas.Jan.UserId}",
            OriginalEventSubject = "Wekelijks marketingoverleg",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(13),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(14),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(3) // Still valid
        };

        // Pieter's conflict (expired)
        var pieterState = new ConflictResolutionStateDocument
        {
            Id = $"{requestId}-conflict-{TestPersonas.Pieter.UserId}",
            RequestId = requestId,
            ConflictUserId = TestPersonas.Pieter.UserId,
            ConflictUserName = TestPersonas.Pieter.DisplayName,
            ChatId = $"chat-{TestPersonas.Pieter.UserId}",
            OriginalEventSubject = "Code review sessie",
            OriginalEventStart = TestPersonas.NextTuesday.AddHours(10),
            OriginalEventEnd = TestPersonas.NextTuesday.AddHours(11),
            ProposedNewStart = TestPersonas.NextTuesday.AddHours(14),
            ProposedNewEnd = TestPersonas.NextTuesday.AddHours(15),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(-1) // Expired
        };

        _mocks.InjectConflictState(janState);
        _mocks.InjectConflictState(pieterState);

        // ── Act 1: Jan accepts ──
        await _conflictService.HandleConflictResponseAsync(
            requestId, TestPersonas.Jan.UserId, ConflictResponse.Accepted);

        // ── Act 2: Timer processes expired (only Pieter) ──
        await _conflictService.ProcessExpiredConflictsAsync();

        // ── Assert: Jan accepted ──
        var janUpdated = _mocks.ConflictStatesUpdated.First(s => s.ConflictUserId == TestPersonas.Jan.UserId);
        janUpdated.ResponseReceived.Should().Be(ConflictResponse.Accepted);

        // ── Assert: Pieter timed out ──
        var pieterUpdated = _mocks.ConflictStatesUpdated.First(s => s.ConflictUserId == TestPersonas.Pieter.UserId);
        pieterUpdated.ResponseReceived.Should().Be(ConflictResponse.TimedOut);

        // ── Assert: Correct audit trail for both ──
        _mocks.AuditLogs.Should().HaveCount(2);
        _mocks.AuditLogs.Should().Contain(a =>
            a.Action == "ConflictResponseReceived" &&
            a.ActorId == TestPersonas.Jan.UserId &&
            a.Details["response"] == "Accepted");
        _mocks.AuditLogs.Should().Contain(a =>
            a.Action == "ConflictResolutionTimedOut" &&
            a.Details["conflictUserId"] == TestPersonas.Pieter.UserId);
    }

    // ──────────────────────────────────────────────
    // Scenario 5e: Response for non-existent conflict → error
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConflictResponse_NonExistentState_ThrowsInvalidOperation()
    {
        await _conflictService
            .Invoking(s => s.HandleConflictResponseAsync("req-nonexistent", "user-ghost", ConflictResponse.Accepted))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
