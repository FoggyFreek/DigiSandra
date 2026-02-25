using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Tests for participant disambiguation: ambiguous name resolution, round-trip card flow,
/// and pipeline resumption after disambiguation.
/// </summary>
public sealed class ParticipantDisambiguationTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingOrchestrator _orchestrator;

    // Two "Jan" candidates to trigger disambiguation
    private static readonly ResolvedParticipant JanDeVries = new()
    {
        UserId = "user-jan-devries",
        DisplayName = "Jan de Vries",
        Email = "jan.devries@contoso.nl",
        IsRequired = true
    };

    private static readonly ResolvedParticipant JanPietersen = new()
    {
        UserId = "user-jan-pietersen",
        DisplayName = "Jan Pietersen",
        Email = "jan.pietersen@contoso.nl",
        IsRequired = true
    };

    public ParticipantDisambiguationTests()
    {
        _mocks.ConfigureDefaults();

        _orchestrator = new SchedulingOrchestrator(
            _mocks.OpenAI.Object,
            _mocks.Graph.Object,
            _mocks.CosmosDb.Object,
            Mock.Of<IConflictResolutionService>(),
            Mock.Of<ILogger<SchedulingOrchestrator>>());
    }

    // ──────────────────────────────────────────────
    // Test 1: Ambiguous name → AwaitingDisambiguation, no FindMeetingTimes called
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Disambiguation_SingleAmbiguous_CardSentAndRequestPaused()
    {
        // ── Arrange ──
        var intent = new MeetingIntent
        {
            Subject = "Project bespreking",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(5)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);
        _mocks.SetupAmbiguousUser("Jan", [JanDeVries, JanPietersen]);

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-disambiguation-1",
            "Plan een bespreking met Jan voor volgende week");

        // ── Assert: request paused for disambiguation ──
        request.Status.Should().Be(SchedulingStatus.AwaitingDisambiguation);

        request.PendingDisambiguations.Should().HaveCount(1);
        var item = request.PendingDisambiguations![0];
        item.RequestedName.Should().Be("Jan");
        item.IsRequired.Should().BeTrue();
        item.Candidates.Should().HaveCount(2);
        item.Candidates.Should().Contain(c => c.UserId == JanDeVries.UserId);
        item.Candidates.Should().Contain(c => c.UserId == JanPietersen.UserId);

        // Sophie (requester) already resolved — still present
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Sophie.UserId);

        // FindMeetingTimes must never have been called
        _mocks.Graph.Verify(g => g.FindMeetingTimesAsync(
            It.IsAny<List<ResolvedParticipant>>(),
            It.IsAny<TimeWindow>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Cosmos DB: request created and updated to AwaitingDisambiguation
        _mocks.RequestsCreated.Should().HaveCount(1);
        var statusUpdates = _mocks.RequestsUpdated.Select(r => r.Status).ToList();
        statusUpdates.Should().Contain(SchedulingStatus.AwaitingDisambiguation);
    }

    // ──────────────────────────────────────────────
    // Test 2: User picks a candidate → scheduling resumes
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Disambiguation_UserSelectsCandidate_SchedulingResumes()
    {
        // ── Arrange: first, trigger disambiguation ──
        var intent = new MeetingIntent
        {
            Subject = "Kwartaalreview",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(5)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);
        _mocks.SetupAmbiguousUser("Jan", [JanDeVries, JanPietersen]);

        var ambiguousRequest = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-disambiguation-2",
            "Plan een kwartaalreview met Jan voor volgende week");

        ambiguousRequest.Status.Should().Be(SchedulingStatus.AwaitingDisambiguation);

        // ── Arrange: set up for pipeline resumption ──
        var freeSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextWednesday.AddHours(10),
            End = TestPersonas.NextWednesday.AddHours(11),
            Confidence = SlotConfidence.Full,
            AvailabilityScore = 1.0,
            Conflicts = []
        };
        _mocks.SetupFindMeetingTimes([freeSlot]);
        _mocks.SetupCreateEvent();

        // ── Act: user selects Jan de Vries ──
        var selections = new Dictionary<string, string> { ["Jan"] = JanDeVries.UserId };
        var resumed = await _orchestrator.HandleDisambiguationResponseAsync(
            ambiguousRequest.RequestId, selections);

        // ── Assert: scheduling resumed to PendingUserSelection ──
        resumed.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        resumed.ProposedSlots.Should().HaveCount(1);
        resumed.PendingDisambiguations.Should().BeNull();

        // Correct attendees: Sophie + Jan de Vries (not Jan Pietersen)
        resumed.ResolvedParticipants.Should().HaveCount(2);
        resumed.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Sophie.UserId);
        resumed.ResolvedParticipants.Should().Contain(p => p.UserId == JanDeVries.UserId);
        resumed.ResolvedParticipants.Should().NotContain(p => p.UserId == JanPietersen.UserId);

        // FindMeetingTimes called exactly once (during resumption, not during initial resolution)
        _mocks.Graph.Verify(g => g.FindMeetingTimesAsync(
            It.Is<List<ResolvedParticipant>>(p => p.Count == 2),
            It.IsAny<TimeWindow>(),
            60,
            It.IsAny<CancellationToken>()), Times.Once);

        // ── Act: book the meeting ──
        var booked = await _orchestrator.HandleSlotSelectionAsync(resumed.RequestId, 0);

        booked.Status.Should().Be(SchedulingStatus.Completed);
        booked.CreatedEventId.Should().StartWith("event-");
        _mocks.EventsCreated.Should().HaveCount(1);

        // Verify audit trail
        _mocks.AuditLogs.Should().Contain(a => a.Action == "MeetingBooked");
    }
}
