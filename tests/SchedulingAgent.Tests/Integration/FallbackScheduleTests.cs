using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Tests the getSchedule fallback flow when findMeetingTimes returns no results.
/// Verifies the slot-finding algorithm scans work hours and detects conflicts
/// from raw schedule data.
/// </summary>
public sealed class FallbackScheduleTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingOrchestrator _orchestrator;
    private readonly ConflictResolutionService _conflictService;

    public FallbackScheduleTests()
    {
        _mocks.ConfigureDefaults();

        _conflictService = new ConflictResolutionService(
            _mocks.OpenAI.Object,
            _mocks.Graph.Object,
            _mocks.CosmosDb.Object,
            Options.Create(new ConflictResolutionOptions { TimeoutHours = 4, MaxRetries = 3 }),
            Mock.Of<ILogger<ConflictResolutionService>>());

        _orchestrator = new SchedulingOrchestrator(
            _mocks.OpenAI.Object,
            _mocks.Graph.Object,
            _mocks.CosmosDb.Object,
            _conflictService,
            Mock.Of<ILogger<SchedulingOrchestrator>>());
    }

    // ──────────────────────────────────────────────
    // findMeetingTimes returns empty → falls back to getSchedule
    // Builds slots from raw schedule data
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Fallback_FindMeetingTimesEmpty_UsesGetSchedule_FindsSlots()
    {
        var intent = new MeetingIntent
        {
            Subject = "Strategie sessie",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextThursday
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true },
                new ParticipantReference { Name = "Pieter", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // findMeetingTimes returns nothing
        _mocks.SetupFindMeetingTimesEmpty();

        // getSchedule returns Jan and Pieter's calendars
        _mocks.SetupGetSchedule([
            TestPersonas.JanWeeklyMarketing,     // Tue 10:00-11:00
            TestPersonas.JanExternalLunch,        // Tue 12:00-13:00
            TestPersonas.PieterCodeReview,        // Tue 10:00-11:00
            TestPersonas.PieterSprintPlanning     // Wed 09:00-10:30
        ]);

        _mocks.SetupCreateEvent();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-fallback",
            "Plan een strategie sessie van 1 uur met Jan en Pieter voor deze week.");

        // ── Assert: Fallback was used ──
        _mocks.Graph.Verify(g => g.FindMeetingTimesAsync(
            It.IsAny<List<ResolvedParticipant>>(),
            It.IsAny<TimeWindow>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mocks.Graph.Verify(g => g.GetScheduleAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once,
            "getSchedule should be called as fallback");

        // ── Assert: Slots were generated ──
        request.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        request.ProposedSlots.Should().NotBeEmpty("the fallback algorithm should find available slots");

        // The best slots should have high availability scores (no/few conflicts)
        var bestSlot = request.ProposedSlots.OrderByDescending(s => s.AvailabilityScore).First();
        bestSlot.AvailabilityScore.Should().BeGreaterOrEqualTo(0.7,
            "the algorithm should prioritize slots with fewer conflicts");

        // ── Assert: Slots should avoid 10:00-11:00 Tue (both blocked) ──
        // The 10:00 slot should have a lower score than other slots
        var morningSlot = request.ProposedSlots.FirstOrDefault(s =>
            s.Start.Hour == 10 && s.Start.Day == TestPersonas.NextTuesday.Day);
        if (morningSlot is not null)
        {
            morningSlot.Conflicts.Should().NotBeEmpty(
                "Tuesday 10:00 conflicts with both Jan and Pieter's events");
        }
    }

    // ──────────────────────────────────────────────
    // findMeetingTimes empty + getSchedule returns busy all day
    // Should still return what's available (no empty result)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Fallback_HeavilyBookedCalendars_StillFindsSlots()
    {
        var intent = new MeetingIntent
        {
            Subject = "Quick sync",
            DurationMinutes = 30,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextThursday
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);
        _mocks.SetupFindMeetingTimesEmpty();

        // Jan is busy Tue 9-17 with back-to-back meetings
        var busySchedule = Enumerable.Range(9, 8).Select(hour => new ScheduleItem
        {
            UserId = TestPersonas.Jan.UserId,
            DisplayName = TestPersonas.Jan.DisplayName,
            Start = TestPersonas.NextTuesday.AddHours(hour),
            End = TestPersonas.NextTuesday.AddHours(hour + 1),
            Status = "busy",
            Subject = $"Meeting {hour}:00",
            IsRecurring = false
        }).ToList();

        _mocks.SetupGetSchedule(busySchedule);
        _mocks.SetupCreateEvent();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-busy-fallback",
            "Plan een snelle sync van 30 min met Jan.");

        // ── Assert: Slots are returned (even if all have conflicts) ──
        request.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        request.ProposedSlots.Should().NotBeEmpty(
            "the algorithm should always return at least some options, even with conflicts");

        // Wednesday slots should have no conflicts (Jan is only busy Tuesday)
        var wednesdaySlots = request.ProposedSlots.Where(s =>
            s.Start.Day == TestPersonas.NextWednesday.Day);
        if (wednesdaySlots.Any())
        {
            wednesdaySlots.Should().Contain(s => s.Conflicts.Count == 0,
                "Wednesday should have free slots since Jan's busy schedule is only on Tuesday");
        }
    }
}
