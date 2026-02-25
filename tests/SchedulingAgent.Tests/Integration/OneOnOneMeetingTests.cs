using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Scenario 1 & 2: One-on-one meeting flows.
/// Tests the full pipeline from intent extraction through booking,
/// including conflict negotiation for blocked participants.
/// </summary>
public sealed class OneOnOneMeetingTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingOrchestrator _orchestrator;
    private readonly ConflictResolutionService _conflictService;

    public OneOnOneMeetingTests()
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
    // Scenario 1: Sophie books a 1:1 with Jan — no conflicts
    // Sophie is free, Jan's slot on Tuesday 15:00 is open
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OneOnOne_NoConflict_SophieAndJan_FullFlowThroughBooking()
    {
        // ── Arrange: Sophie asks for a 1:1 with Jan next Tuesday afternoon ──
        var userMessage = "Plan een 1-op-1 meeting van 30 minuten met Jan voor volgende week dinsdag middag.";

        var intent = new MeetingIntent
        {
            Subject = "1-op-1 Sophie en Jan",
            DurationMinutes = 30,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1),
                PreferredTimeOfDay = TimeOfDayPreference.Afternoon
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // Tuesday 15:00-15:30 — both are free
        var freeSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(15),
            End = TestPersonas.NextTuesday.AddHours(15).AddMinutes(30),
            Confidence = SlotConfidence.Full,
            AvailabilityScore = 1.0,
            Conflicts = []
        };
        _mocks.SetupFindMeetingTimes([freeSlot]);
        _mocks.SetupCreateEvent();

        // ── Act: Process the request ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-sophie-jan-1on1",
            userMessage);

        // ── Assert: Request is ready for user selection ──
        request.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        request.ProposedSlots.Should().HaveCount(1);
        request.ProposedSlots[0].Confidence.Should().Be(SlotConfidence.Full);
        request.ProposedSlots[0].Conflicts.Should().BeEmpty();

        // Verify: OpenAI was called to parse intent
        _mocks.IntentExtractions.Should().HaveCount(1);
        _mocks.IntentExtractions[0].Should().Be(userMessage);

        // Verify: Graph resolved Jan as user
        _mocks.ResolvedUsers.Should().Contain("Jan");

        // Verify: Participants include Sophie (requester) + Jan
        request.ResolvedParticipants.Should().HaveCount(2);
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Sophie.UserId);
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Jan.UserId);

        // Verify: Graph findMeetingTimes was called
        _mocks.Graph.Verify(g => g.FindMeetingTimesAsync(
            It.Is<List<ResolvedParticipant>>(p => p.Count == 2),
            It.IsAny<TimeWindow>(),
            30,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify: No conflicts analyzed (slot was clean)
        _mocks.ConflictAnalyses.Should().BeEmpty();

        // Verify: No chats created (no conflict negotiation needed)
        _mocks.ChatsCreated.Should().BeEmpty();
        _mocks.MessagesSent.Should().BeEmpty();

        // Verify: Cosmos DB created the request + audit log
        _mocks.RequestsCreated.Should().HaveCount(1);
        _mocks.AuditLogs.Should().Contain(a => a.Action == "RequestCreated");

        // Verify: Status transitions happened in order
        var statusUpdates = _mocks.RequestsUpdated.Select(r => r.Status).ToList();
        // First update should be CheckingAvailability (ResolvingParticipants is updated but not captured yet)
        statusUpdates.Should().ContainInOrder(
            SchedulingStatus.CheckingAvailability,
            SchedulingStatus.PendingUserSelection);

        // ── Act: Sophie selects the slot ──
        var booked = await _orchestrator.HandleSlotSelectionAsync(request.RequestId, 0);

        // ── Assert: Meeting is booked ──
        booked.Status.Should().Be(SchedulingStatus.Completed);
        booked.SelectedSlotIndex.Should().Be(0);
        booked.CreatedEventId.Should().StartWith("event-");

        // Verify: Graph createEvent was called with correct parameters
        _mocks.EventsCreated.Should().HaveCount(1);
        _mocks.Graph.Verify(g => g.CreateEventAsync(
            TestPersonas.Sophie.UserId,
            "1-op-1 Sophie en Jan",
            freeSlot.Start,
            freeSlot.End,
            It.Is<List<ResolvedParticipant>>(p => p.Count == 2),
            true,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify: Audit trail includes booking
        _mocks.AuditLogs.Should().Contain(a => a.Action == "MeetingBooked");
        var bookingLog = _mocks.AuditLogs.First(a => a.Action == "MeetingBooked");
        bookingLog.ActorId.Should().Be(TestPersonas.Sophie.UserId);
        bookingLog.Details.Should().ContainKey("eventId");
    }

    // ──────────────────────────────────────────────
    // Scenario 2: Sophie books a 1:1 with Jan — Jan is blocked
    // Jan has "Wekelijks marketingoverleg" Tue 10:00-11:00
    // The AI suggests asking Jan to move it
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OneOnOne_JanBlocked_ConflictNegotiationInitiated()
    {
        // ── Arrange: Sophie wants to meet Jan Tuesday 10:00 — but Jan is busy ──
        var userMessage = "Plan een overleg van 1 uur met Jan voor dinsdag 10 uur over kwartaalcijfers.";

        var intent = new MeetingIntent
        {
            Subject = "Kwartaalcijfers bespreking",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.High,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // findMeetingTimes returns a slot with a conflict on Jan
        var conflictedSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(10),
            End = TestPersonas.NextTuesday.AddHours(11),
            Confidence = SlotConfidence.Conditional,
            AvailabilityScore = 0.5,
            Conflicts =
            [
                new SlotConflict
                {
                    UserId = TestPersonas.Jan.UserId,
                    DisplayName = TestPersonas.Jan.DisplayName,
                    ConflictingEventSubject = "Wekelijks marketingoverleg",
                    ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
                    ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
                    IsRecurring = true,
                    Sensitivity = "normal",
                    Importance = "low"
                }
            ]
        };
        _mocks.SetupFindMeetingTimes([conflictedSlot]);

        // AI decides to ask Jan (recurring low-importance internal meeting)
        var alternativeTime = TestPersonas.NextTuesday.AddHours(14);
        _mocks.SetupSingleConflictAnalysis(new ConflictAnalysis
        {
            CanAutoResolve = false,
            Strategy = ConflictStrategy.AskParticipant,
            Reasoning = "Jan heeft een terugkerend intern overleg met lage prioriteit. " +
                        "We kunnen vragen of hij dit kan verplaatsen.",
            SuggestedAlternativeSlot = new AlternativeSlot
            {
                Start = alternativeTime,
                End = alternativeTime.AddHours(1)
            },
            BlockedByUserId = TestPersonas.Jan.UserId,
            BlockedByEventType = "Recurring internal"
        });

        _mocks.SetupChatCreation();
        _mocks.SetupCreateEvent();

        // ── Act: Process the request ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-sophie-jan-conflict",
            userMessage);

        // ── Assert: Request reaches user selection (with conflict info) ──
        request.Status.Should().Be(SchedulingStatus.PendingUserSelection);

        // Verify: OpenAI analyzed the conflict
        _mocks.ConflictAnalyses.Should().HaveCount(1);
        _mocks.ConflictAnalyses[0].Conflict.UserId.Should().Be(TestPersonas.Jan.UserId);
        _mocks.ConflictAnalyses[0].Priority.Should().Be(MeetingPriority.High);

        // Verify: A chat was created with Jan for negotiation
        _mocks.ChatsCreated.Should().HaveCount(1);
        _mocks.ChatsCreated[0].Should().Be($"chat-{TestPersonas.Jan.UserId}");

        // Verify: A conflict message was sent to Jan in Dutch
        _mocks.MessagesSent.Should().HaveCount(1);
        _mocks.MessagesSent[0].ChatId.Should().Be($"chat-{TestPersonas.Jan.UserId}");
        _mocks.MessagesSent[0].Message.Should().Contain("Jan de Vries");
        _mocks.MessagesSent[0].Message.Should().Contain("Kwartaalcijfers bespreking");
        _mocks.MessagesSent[0].Message.Should().Contain("Wekelijks marketingoverleg");

        // Verify: Conflict state saved to Cosmos DB
        _mocks.ConflictStatesCreated.Should().HaveCount(1);
        var conflictState = _mocks.ConflictStatesCreated[0];
        conflictState.ConflictUserId.Should().Be(TestPersonas.Jan.UserId);
        conflictState.PendingResponse.Should().BeTrue();
        conflictState.OriginalEventSubject.Should().Be("Wekelijks marketingoverleg");
        conflictState.RequestId.Should().Be(request.RequestId);

        // Verify: Audit trail
        _mocks.AuditLogs.Should().Contain(a => a.Action == "ConflictNegotiationStarted");
        _mocks.AuditLogs.Should().Contain(a => a.Action == "ConflictsAnalyzed");
        var negotiationLog = _mocks.AuditLogs.First(a => a.Action == "ConflictNegotiationStarted");
        negotiationLog.Details["conflictUserId"].Should().Be(TestPersonas.Jan.UserId);
        negotiationLog.Details["chatId"].Should().Be($"chat-{TestPersonas.Jan.UserId}");

        // Verify: Status went through ResolvingConflicts
        var statusUpdates = _mocks.RequestsUpdated.Select(r => r.Status).ToList();
        statusUpdates.Should().Contain(SchedulingStatus.ResolvingConflicts);
    }

    // ──────────────────────────────────────────────
    // Scenario 2b: Jan is blocked with an external meeting — AI escalates
    // Sophie asks for Tue 12:00 but Jan has "Lunch met klant"
    // ──────────────────────────────────────────────

    [Fact]
    public async Task OneOnOne_JanExternalMeeting_AiEscalates_NoNegotiation()
    {
        var intent = new MeetingIntent
        {
            Subject = "Budget review",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        var conflictedSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(12),
            End = TestPersonas.NextTuesday.AddHours(13),
            Confidence = SlotConfidence.Low,
            AvailabilityScore = 0.3,
            Conflicts =
            [
                new SlotConflict
                {
                    UserId = TestPersonas.Jan.UserId,
                    DisplayName = TestPersonas.Jan.DisplayName,
                    ConflictingEventSubject = "Lunch met klant Acme Corp",
                    ConflictingEventStart = TestPersonas.NextTuesday.AddHours(12),
                    ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(13),
                    IsRecurring = false,
                    Sensitivity = "private",
                    Importance = "high"
                }
            ]
        };
        _mocks.SetupFindMeetingTimes([conflictedSlot]);

        // AI decides to escalate — external high-importance meeting
        _mocks.SetupSingleConflictAnalysis(new ConflictAnalysis
        {
            CanAutoResolve = false,
            Strategy = ConflictStrategy.Escalate,
            Reasoning = "Jan heeft een externe afspraak met hoge prioriteit. " +
                        "Dit kan niet automatisch worden gewijzigd.",
            BlockedByUserId = TestPersonas.Jan.UserId,
            BlockedByEventType = "External meeting"
        });

        _mocks.SetupChatCreation(); // Should NOT be called

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-external-conflict",
            "Plan een budget review met Jan voor dinsdag lunch.");

        // ── Assert: No chat created — AI escalated, doesn't negotiate ──
        _mocks.ChatsCreated.Should().BeEmpty(
            "external high-importance meetings should not trigger automated negotiation");
        _mocks.MessagesSent.Should().BeEmpty();
        _mocks.ConflictStatesCreated.Should().BeEmpty();

        // Verify: Conflict was analyzed by OpenAI
        _mocks.ConflictAnalyses.Should().HaveCount(1);
        _mocks.ConflictAnalyses[0].Conflict.ConflictingEventSubject.Should().Be("Lunch met klant Acme Corp");

        // The slot still has the conflict attached — user sees it in the card
        request.ProposedSlots.Should().HaveCount(1);
        request.ProposedSlots[0].Conflicts.Should().HaveCount(1);
    }
}
