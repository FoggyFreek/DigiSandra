using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Scenario 6: High/Urgent priority meetings overriding lower priority events.
/// Tests how the AI decision matrix behaves differently based on request priority,
/// and verifies that conflict strategies change accordingly.
/// </summary>
public sealed class PriorityOverrideTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingOrchestrator _orchestrator;
    private readonly ConflictResolutionService _conflictService;

    public PriorityOverrideTests()
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
    // Scenario 6a: Urgent meeting — Jan's recurring overleg
    // can be proposed for reschedule (AI gets Urgent priority context)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UrgentMeeting_RecurringLowPriorityConflict_ProposeReschedule()
    {
        var intent = new MeetingIntent
        {
            Subject = "Incident response P1",
            DurationMinutes = 30,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true },
                new ParticipantReference { Name = "Daan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Urgent,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        var janConflict = new SlotConflict
        {
            UserId = TestPersonas.Jan.UserId,
            DisplayName = TestPersonas.Jan.DisplayName,
            ConflictingEventSubject = "Wekelijks marketingoverleg",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
            IsRecurring = true,
            Sensitivity = "normal",
            Importance = "low"
        };

        var daunConflict = new SlotConflict
        {
            UserId = TestPersonas.Daan.UserId,
            DisplayName = TestPersonas.Daan.DisplayName,
            ConflictingEventSubject = "Tandarts",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
            IsRecurring = false,
            Sensitivity = "private",
            Importance = "normal"
        };

        _mocks.SetupFindMeetingTimes([
            new ProposedTimeSlot
            {
                Start = TestPersonas.NextTuesday.AddHours(10),
                End = TestPersonas.NextTuesday.AddHours(10).AddMinutes(30),
                Confidence = SlotConfidence.Low,
                AvailabilityScore = 0.2,
                Conflicts = [janConflict, daunConflict]
            }
        ]);

        // AI: Urgent overrides Jan's low-priority recurring → ProposeReschedule
        // AI: Daan's private appointment → Escalate (even for urgent)
        _mocks.SetupConflictAnalysis(new Dictionary<string, ConflictAnalysis>
        {
            [TestPersonas.Jan.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = true,
                Strategy = ConflictStrategy.ProposeReschedule,
                Reasoning = "Urgent incident response. Jan's wekelijks overleg heeft lage " +
                            "prioriteit en is terugkerend. Kan verplaatst worden.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = TestPersonas.NextTuesday.AddHours(15),
                    End = TestPersonas.NextTuesday.AddHours(16)
                },
                BlockedByUserId = TestPersonas.Jan.UserId,
                BlockedByEventType = "Recurring internal"
            },
            [TestPersonas.Daan.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.Escalate,
                Reasoning = "Daan heeft een privé-afspraak (tandarts). Ondanks de urgentie " +
                            "van het verzoek kan een privé-afspraak niet automatisch worden gewijzigd.",
                BlockedByUserId = TestPersonas.Daan.UserId,
                BlockedByEventType = "Private appointment"
            }
        });

        _mocks.SetupChatCreation();
        _mocks.SetupCreateEvent();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-urgent-p1",
            "URGENT: Plan een incident response call van 30 min met Jan en Daan voor dinsdag 10 uur.");

        // ── Assert: Both conflicts analyzed with Urgent priority ──
        _mocks.ConflictAnalyses.Should().HaveCount(2);
        _mocks.ConflictAnalyses.Should().OnlyContain(c => c.Priority == MeetingPriority.Urgent,
            "the AI should receive the Urgent priority context for both conflicts");

        // ── Assert: Jan gets a chat (ProposeReschedule triggers negotiation) ──
        _mocks.ChatsCreated.Should().Contain($"chat-{TestPersonas.Jan.UserId}");

        var janMessage = _mocks.MessagesSent.FirstOrDefault(m =>
            m.ChatId == $"chat-{TestPersonas.Jan.UserId}");
        janMessage.Message.Should().Contain("Incident response P1");

        // ── Assert: Daan does NOT get a chat (Escalate = no auto-action) ──
        _mocks.ChatsCreated.Should().NotContain($"chat-{TestPersonas.Daan.UserId}",
            "private appointments should never be auto-negotiated, even for urgent requests");

        // Verify: Conflict state only for Jan (not Daan)
        _mocks.ConflictStatesCreated.Should().HaveCount(1);
        _mocks.ConflictStatesCreated[0].ConflictUserId.Should().Be(TestPersonas.Jan.UserId);
    }

    // ──────────────────────────────────────────────
    // Scenario 6b: Low priority meeting — same conflict
    // AI should suggest alternative instead of asking to move
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LowPriorityMeeting_SameConflict_SuggestsAlternativeInstead()
    {
        var intent = new MeetingIntent
        {
            Subject = "Informeel koffie-overleg",
            DurationMinutes = 30,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(3) // Wider window
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Low,
            IsOnline = false
        };

        _mocks.SetupIntentExtraction(intent);

        var janConflict = new SlotConflict
        {
            UserId = TestPersonas.Jan.UserId,
            DisplayName = TestPersonas.Jan.DisplayName,
            ConflictingEventSubject = "Wekelijks marketingoverleg",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
            IsRecurring = true,
            Sensitivity = "normal",
            Importance = "low"
        };

        _mocks.SetupFindMeetingTimes([
            new ProposedTimeSlot
            {
                Start = TestPersonas.NextTuesday.AddHours(10),
                End = TestPersonas.NextTuesday.AddHours(10).AddMinutes(30),
                Confidence = SlotConfidence.Conditional,
                AvailabilityScore = 0.5,
                Conflicts = [janConflict]
            }
        ]);

        // AI: Low priority request → always suggest alternative, never ask to move
        var alternativeTime = TestPersonas.NextWednesday.AddHours(11);
        _mocks.SetupSingleConflictAnalysis(new ConflictAnalysis
        {
            CanAutoResolve = true,
            Strategy = ConflictStrategy.SuggestAlternativeSlot,
            Reasoning = "Dit is een laagprioritair verzoek. In plaats van Jan's overleg " +
                        "te verplaatsen, wordt een alternatief tijdslot voorgesteld.",
            SuggestedAlternativeSlot = new AlternativeSlot
            {
                Start = alternativeTime,
                End = alternativeTime.AddMinutes(30)
            },
            BlockedByUserId = TestPersonas.Jan.UserId,
            BlockedByEventType = "Recurring internal"
        });

        _mocks.SetupChatCreation();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-low-priority",
            "Plan een informeel koffie-momentje met Jan ergens volgende week.");

        // ── Assert: Priority Low was passed to the AI ──
        _mocks.ConflictAnalyses.Should().HaveCount(1);
        _mocks.ConflictAnalyses[0].Priority.Should().Be(MeetingPriority.Low);

        // ── Assert: NO negotiation initiated for low priority ──
        _mocks.ChatsCreated.Should().BeEmpty(
            "low priority meetings should never initiate conflict negotiation");
        _mocks.ConflictStatesCreated.Should().BeEmpty();

        // ── Assert: Alternative slot was added ──
        request.ProposedSlots.Should().Contain(s => s.Start == alternativeTime);

        // Verify: Meeting is not online (Sophie specified physical)
        request.Intent.IsOnline.Should().BeFalse();
    }

    // ──────────────────────────────────────────────
    // Scenario 6c: Fatima's confidential meeting with directeur
    // Even Urgent cannot override it
    // ──────────────────────────────────────────────

    [Fact]
    public async Task UrgentMeeting_ConfidentialEvent_NeverOverridden()
    {
        var intent = new MeetingIntent
        {
            Subject = "Spoed: beveiligingsincident",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1)
            },
            Participants =
            [
                new ParticipantReference { Name = "Fatima", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Urgent,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        var fatimaConflict = new SlotConflict
        {
            UserId = TestPersonas.Fatima.UserId,
            DisplayName = TestPersonas.Fatima.DisplayName,
            ConflictingEventSubject = "1:1 met directeur",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(14),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(15),
            IsRecurring = false,
            Sensitivity = "confidential",
            Importance = "high"
        };

        _mocks.SetupFindMeetingTimes([
            new ProposedTimeSlot
            {
                Start = TestPersonas.NextTuesday.AddHours(14),
                End = TestPersonas.NextTuesday.AddHours(15),
                Confidence = SlotConfidence.Low,
                AvailabilityScore = 0.1,
                Conflicts = [fatimaConflict]
            }
        ]);

        // AI: Confidential + high importance → always Escalate
        _mocks.SetupSingleConflictAnalysis(new ConflictAnalysis
        {
            CanAutoResolve = false,
            Strategy = ConflictStrategy.Escalate,
            Reasoning = "Fatima heeft een vertrouwelijke afspraak met hoge prioriteit. " +
                        "Deze kan onder geen enkele omstandigheid automatisch worden gewijzigd.",
            BlockedByUserId = TestPersonas.Fatima.UserId,
            BlockedByEventType = "Confidential meeting"
        });

        _mocks.SetupChatCreation();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-urgent-confidential",
            "SPOED: Plan een beveiligingsincident call met Fatima voor dinsdag 14:00.");

        // ── Assert: No negotiation for confidential meetings ──
        _mocks.ChatsCreated.Should().BeEmpty();
        _mocks.ConflictStatesCreated.Should().BeEmpty();

        // The conflict is preserved for the user to see
        request.ProposedSlots[0].Conflicts.Should().HaveCount(1);
        request.ProposedSlots[0].Conflicts[0].Sensitivity.Should().Be("confidential");
    }
}
