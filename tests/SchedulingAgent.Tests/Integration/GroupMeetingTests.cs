using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Scenario 3 & 4: Group meeting flows.
/// Tests group resolution, multi-participant availability,
/// and parallel conflict negotiations with multiple blocked people.
/// </summary>
public sealed class GroupMeetingTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingOrchestrator _orchestrator;
    private readonly ConflictResolutionService _conflictService;

    public GroupMeetingTests()
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
    // Scenario 3: Sophie books a meeting with the Marketing team
    // Thursday afternoon — everyone is free
    // Tests group expansion + multiple participant availability
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GroupMeeting_MarketingTeam_NoConflicts_FullFlow()
    {
        // ── Arrange ──
        var userMessage = "Plan een overleg van 1 uur met het Marketing-team en Pieter voor donderdag middag over de campagnestrategie.";

        var intent = new MeetingIntent
        {
            Subject = "Campagnestrategie Q2",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextThursday,
                EndDate = TestPersonas.NextThursday.AddDays(1),
                PreferredTimeOfDay = TimeOfDayPreference.Afternoon
            },
            Participants =
            [
                new ParticipantReference { Name = "Marketing-team", Type = ParticipantType.Group, IsRequired = true },
                new ParticipantReference { Name = "Pieter", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // Thursday 14:00-15:00 — everyone is free
        var freeSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextThursday.AddHours(14),
            End = TestPersonas.NextThursday.AddHours(15),
            Confidence = SlotConfidence.Full,
            AvailabilityScore = 1.0,
            Conflicts = []
        };
        _mocks.SetupFindMeetingTimes([freeSlot]);
        _mocks.SetupCreateEvent();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-marketing-group",
            userMessage);

        // ── Assert: Group was expanded ──
        _mocks.ResolvedGroups.Should().Contain("Marketing-team");
        _mocks.ResolvedUsers.Should().Contain("Pieter");

        // Participants: Sophie (requester) + Fatima, Jan, Lisa (marketing) + Pieter
        // Jan is in marketing team, so after deduplication we should have 5 unique users
        request.ResolvedParticipants.Should().HaveCount(5);
        request.ResolvedParticipants.Select(p => p.UserId).Should().BeEquivalentTo(
        [
            TestPersonas.Sophie.UserId,
            TestPersonas.Fatima.UserId,
            TestPersonas.Jan.UserId,
            TestPersonas.Lisa.UserId,
            TestPersonas.Pieter.UserId
        ]);

        // Verify: Group members have ResolvedFromGroup set
        request.ResolvedParticipants
            .Where(p => p.ResolvedFromGroup == TestPersonas.MarketingTeamName)
            .Should().HaveCount(3, "Fatima, Jan, Lisa are from Marketing-team");

        // Verify: findMeetingTimes called with all 5 participants
        _mocks.Graph.Verify(g => g.FindMeetingTimesAsync(
            It.Is<List<ResolvedParticipant>>(p => p.Count == 5),
            It.IsAny<TimeWindow>(),
            60,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify: No conflicts
        request.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        request.ProposedSlots[0].Conflicts.Should().BeEmpty();
        _mocks.ConflictAnalyses.Should().BeEmpty();

        // ── Act: Sophie books the slot ──
        var booked = await _orchestrator.HandleSlotSelectionAsync(request.RequestId, 0);

        // ── Assert: Event created with all 5 participants ──
        booked.Status.Should().Be(SchedulingStatus.Completed);
        _mocks.Graph.Verify(g => g.CreateEventAsync(
            TestPersonas.Sophie.UserId,
            "Campagnestrategie Q2",
            freeSlot.Start,
            freeSlot.End,
            It.Is<List<ResolvedParticipant>>(p => p.Count == 5),
            true,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify: Full audit trail
        _mocks.AuditLogs.Select(a => a.Action).Should().Contain("RequestCreated");
        _mocks.AuditLogs.Select(a => a.Action).Should().Contain("MeetingBooked");
    }

    // ──────────────────────────────────────────────
    // Scenario 4: Cross-team meeting on Tuesday 10:00
    // MULTIPLE PEOPLE BLOCKED:
    //   - Jan: "Wekelijks marketingoverleg" (recurring, low) → AskParticipant
    //   - Pieter: "Code review sessie" (recurring, low) → AskParticipant
    //   - Fatima: "Focus Time" 09:00-12:00 → SuggestAlternativeSlot
    // Tests parallel conflict negotiations sent to multiple people
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GroupMeeting_MultipleBlocked_ParallelNegotiations()
    {
        // ── Arrange: Sophie wants both teams on Tuesday 10:00 ──
        var userMessage = "Plan een urgente vergadering van 1 uur met Jan, Pieter en Fatima voor dinsdag ochtend over de productlancering.";

        var intent = new MeetingIntent
        {
            Subject = "Productlancering afstemming",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(1),
                PreferredTimeOfDay = TimeOfDayPreference.Morning
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true },
                new ParticipantReference { Name = "Pieter", Type = ParticipantType.User, IsRequired = true },
                new ParticipantReference { Name = "Fatima", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.Urgent,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // Tuesday 10:00-11:00 — Jan, Pieter, and Fatima all have conflicts
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

        var pieterConflict = new SlotConflict
        {
            UserId = TestPersonas.Pieter.UserId,
            DisplayName = TestPersonas.Pieter.DisplayName,
            ConflictingEventSubject = "Code review sessie",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
            IsRecurring = true,
            Sensitivity = "normal",
            Importance = "low"
        };

        var fatimaConflict = new SlotConflict
        {
            UserId = TestPersonas.Fatima.UserId,
            DisplayName = TestPersonas.Fatima.DisplayName,
            ConflictingEventSubject = "Focus Time",
            ConflictingEventStart = TestPersonas.NextTuesday.AddHours(9),
            ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(12),
            IsRecurring = true,
            Sensitivity = "normal",
            Importance = "normal"
        };

        var tripleConflictSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(10),
            End = TestPersonas.NextTuesday.AddHours(11),
            Confidence = SlotConfidence.Low,
            AvailabilityScore = 0.1,
            Conflicts = [janConflict, pieterConflict, fatimaConflict]
        };

        _mocks.SetupFindMeetingTimes([tripleConflictSlot]);

        // AI decision per user:
        var alternativeSlotTime = TestPersonas.NextTuesday.AddHours(15);
        _mocks.SetupConflictAnalysis(new Dictionary<string, ConflictAnalysis>
        {
            // Jan: recurring low-priority → ask him to move
            [TestPersonas.Jan.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.AskParticipant,
                Reasoning = "Jan heeft een terugkerend intern overleg met lage prioriteit. " +
                            "Bij een urgente vergadering kan gevraagd worden dit te verplaatsen.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = TestPersonas.NextTuesday.AddHours(13),
                    End = TestPersonas.NextTuesday.AddHours(14)
                },
                BlockedByUserId = TestPersonas.Jan.UserId,
                BlockedByEventType = "Recurring internal"
            },
            // Pieter: recurring low-priority → also ask
            [TestPersonas.Pieter.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.AskParticipant,
                Reasoning = "Pieter heeft een terugkerende code review met lage prioriteit. " +
                            "Dit kan verplaatst worden voor een urgente vergadering.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = TestPersonas.NextTuesday.AddHours(14),
                    End = TestPersonas.NextTuesday.AddHours(15)
                },
                BlockedByUserId = TestPersonas.Pieter.UserId,
                BlockedByEventType = "Recurring internal"
            },
            // Fatima: Focus Time → respect it, suggest alternative
            [TestPersonas.Fatima.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = true,
                Strategy = ConflictStrategy.SuggestAlternativeSlot,
                Reasoning = "Fatima heeft Focus Time ingepland. Dit wordt gerespecteerd. " +
                            "Een alternatief tijdslot wordt voorgesteld.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = alternativeSlotTime,
                    End = alternativeSlotTime.AddHours(1)
                },
                BlockedByUserId = TestPersonas.Fatima.UserId,
                BlockedByEventType = "Focus Time"
            }
        });

        _mocks.SetupChatCreation();
        _mocks.SetupCreateEvent();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-multi-conflict",
            userMessage);

        // ── Assert: All 3 conflicts analyzed by OpenAI ──
        _mocks.ConflictAnalyses.Should().HaveCount(3);
        _mocks.ConflictAnalyses.Select(c => c.Conflict.UserId).Should().BeEquivalentTo(
        [
            TestPersonas.Jan.UserId,
            TestPersonas.Pieter.UserId,
            TestPersonas.Fatima.UserId
        ]);

        // All analyses received the Urgent priority
        _mocks.ConflictAnalyses.Should().OnlyContain(c => c.Priority == MeetingPriority.Urgent);

        // ── Assert: Chats created for Jan and Pieter (AskParticipant) ──
        _mocks.ChatsCreated.Should().HaveCount(2);
        _mocks.ChatsCreated.Should().Contain($"chat-{TestPersonas.Jan.UserId}");
        _mocks.ChatsCreated.Should().Contain($"chat-{TestPersonas.Pieter.UserId}");

        // ── Assert: Messages sent to Jan and Pieter ──
        _mocks.MessagesSent.Should().HaveCount(2);

        var janMessage = _mocks.MessagesSent.First(m => m.ChatId == $"chat-{TestPersonas.Jan.UserId}");
        janMessage.Message.Should().Contain("Jan de Vries");
        janMessage.Message.Should().Contain("Productlancering afstemming");
        janMessage.Message.Should().Contain("Wekelijks marketingoverleg");

        var pieterMessage = _mocks.MessagesSent.First(m => m.ChatId == $"chat-{TestPersonas.Pieter.UserId}");
        pieterMessage.Message.Should().Contain("Pieter Jansen");
        pieterMessage.Message.Should().Contain("Productlancering afstemming");
        pieterMessage.Message.Should().Contain("Code review sessie");

        // ── Assert: NO chat created for Fatima (SuggestAlternativeSlot, not AskParticipant) ──
        _mocks.ChatsCreated.Should().NotContain($"chat-{TestPersonas.Fatima.UserId}",
            "Fatima's Focus Time is respected — an alternative slot is suggested instead of negotiating");

        // ── Assert: Conflict states created in Cosmos DB for Jan and Pieter ──
        _mocks.ConflictStatesCreated.Should().HaveCount(2);

        var janState = _mocks.ConflictStatesCreated.First(s => s.ConflictUserId == TestPersonas.Jan.UserId);
        janState.PendingResponse.Should().BeTrue();
        janState.OriginalEventSubject.Should().Be("Wekelijks marketingoverleg");
        janState.ChatId.Should().Be($"chat-{TestPersonas.Jan.UserId}");

        var pieterState = _mocks.ConflictStatesCreated.First(s => s.ConflictUserId == TestPersonas.Pieter.UserId);
        pieterState.PendingResponse.Should().BeTrue();
        pieterState.OriginalEventSubject.Should().Be("Code review sessie");
        pieterState.ChatId.Should().Be($"chat-{TestPersonas.Pieter.UserId}");

        // ── Assert: Alternative slot added for Fatima's Focus Time ──
        // The conflict resolution added an alternative slot at 15:00
        request.ProposedSlots.Should().HaveCountGreaterOrEqualTo(2,
            "original slot + Fatima's alternative should both be present");

        var alternativeSlot = request.ProposedSlots.FirstOrDefault(s =>
            s.Start == alternativeSlotTime);
        alternativeSlot.Should().NotBeNull("Fatima's suggested alternative at 15:00 should be added");
        alternativeSlot!.Confidence.Should().Be(SlotConfidence.Conditional);

        // ── Assert: Audit trail covers all conflict operations ──
        _mocks.AuditLogs.Where(a => a.Action == "ConflictNegotiationStarted").Should().HaveCount(2);
        _mocks.AuditLogs.Should().Contain(a => a.Action == "ConflictsAnalyzed");
        _mocks.AuditLogs.Should().Contain(a => a.Action == "RequestCreated");

        // Verify the full status transition chain
        var statusUpdates = _mocks.RequestsUpdated.Select(r => r.Status).ToList();
        statusUpdates.Should().ContainInConsecutiveOrder(
            SchedulingStatus.ResolvingParticipants,
            SchedulingStatus.CheckingAvailability,
            SchedulingStatus.ResolvingConflicts,
            SchedulingStatus.PendingUserSelection);
    }

    // ──────────────────────────────────────────────
    // Scenario 4b: Meeting with Marketing-team on Wednesday
    // Lisa has all-day conference — group member blocked
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GroupMeeting_TeamMemberAllDayEvent_AlternativeSlotSuggested()
    {
        var intent = new MeetingIntent
        {
            Subject = "Content planning",
            DurationMinutes = 45,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextWednesday,
                EndDate = TestPersonas.NextWednesday.AddDays(1)
            },
            Participants =
            [
                new ParticipantReference { Name = "Marketing-team", Type = ParticipantType.Group, IsRequired = true }
            ],
            Priority = MeetingPriority.Normal,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // Wednesday: Lisa has all-day conference
        var lisaConflict = new SlotConflict
        {
            UserId = TestPersonas.Lisa.UserId,
            DisplayName = TestPersonas.Lisa.DisplayName,
            ConflictingEventSubject = "Marketing Conferentie Amsterdam",
            ConflictingEventStart = TestPersonas.NextWednesday,
            ConflictingEventEnd = TestPersonas.NextWednesday.AddDays(1),
            IsRecurring = false,
            Sensitivity = "normal",
            Importance = "high"
        };

        var conflictedSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextWednesday.AddHours(10),
            End = TestPersonas.NextWednesday.AddHours(10).AddMinutes(45),
            Confidence = SlotConfidence.Conditional,
            AvailabilityScore = 0.5,
            Conflicts = [lisaConflict]
        };

        _mocks.SetupFindMeetingTimes([conflictedSlot]);

        // AI: all-day event → suggest alternative (Thursday)
        var thursdayAlternative = TestPersonas.NextThursday.AddHours(10);
        _mocks.SetupSingleConflictAnalysis(new ConflictAnalysis
        {
            CanAutoResolve = true,
            Strategy = ConflictStrategy.SuggestAlternativeSlot,
            Reasoning = "Lisa is de hele dag op een conferentie. Dit is een all-day event " +
                        "dat niet aangepast kan worden. Een alternatief op donderdag wordt voorgesteld.",
            SuggestedAlternativeSlot = new AlternativeSlot
            {
                Start = thursdayAlternative,
                End = thursdayAlternative.AddMinutes(45)
            },
            BlockedByUserId = TestPersonas.Lisa.UserId,
            BlockedByEventType = "All-day event"
        });

        _mocks.SetupChatCreation();

        // ── Act ──
        var request = await _orchestrator.ProcessSchedulingRequestAsync(
            TestPersonas.Sophie.UserId,
            TestPersonas.Sophie.DisplayName,
            "conv-lisa-conference",
            "Plan content planning met het Marketing-team voor woensdag.");

        // ── Assert: No negotiation for all-day events ──
        _mocks.ChatsCreated.Should().BeEmpty(
            "all-day events should not trigger negotiation");
        _mocks.ConflictStatesCreated.Should().BeEmpty();

        // Assert: Alternative slot was added
        request.ProposedSlots.Should().HaveCountGreaterOrEqualTo(2);
        request.ProposedSlots.Should().Contain(s =>
            s.Start == thursdayAlternative,
            "Thursday alternative should be proposed since Lisa is at a conference on Wednesday");

        // Verify group expansion happened
        _mocks.ResolvedGroups.Should().Contain("Marketing-team");
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Lisa.UserId);
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Fatima.UserId);
        request.ResolvedParticipants.Should().Contain(p => p.UserId == TestPersonas.Jan.UserId);
    }
}
