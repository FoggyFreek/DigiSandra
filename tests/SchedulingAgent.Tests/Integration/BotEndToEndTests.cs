using System.Text.Json;
using AdaptiveCards;
using FluentAssertions;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Testing;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Bot;
using SchedulingAgent.Cards;
using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Scenario 7: Full bot-level end-to-end tests.
/// Messages flow through the SchedulingBot → Orchestrator → all services.
/// Validates Adaptive Card output, card action handling, and the complete
/// user interaction cycle from natural language input to booked meeting.
/// </summary>
public sealed class BotEndToEndTests
{
    private readonly MockServiceFactory _mocks = new();
    private readonly SchedulingBot _bot;
    private readonly SchedulingOrchestrator _orchestrator;
    private readonly ConflictResolutionService _conflictService;

    public BotEndToEndTests()
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

        _bot = new SchedulingBot(
            _orchestrator,
            _conflictService,
            Mock.Of<ILogger<SchedulingBot>>());
    }

    // ──────────────────────────────────────────────
    // Full E2E: Sophie sends message → gets Adaptive Card → selects slot → meeting booked
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_SophieBooksOneOnOneWithJan_ViaTeamsMessages()
    {
        // ── Arrange: Set up all services for a clean 1:1 ──
        var intent = new MeetingIntent
        {
            Subject = "1-op-1 met Jan",
            DurationMinutes = 30,
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

        var slot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(15),
            End = TestPersonas.NextTuesday.AddHours(15).AddMinutes(30),
            Confidence = SlotConfidence.Full,
            AvailabilityScore = 1.0,
            Conflicts = []
        };
        _mocks.SetupFindMeetingTimes([slot]);
        _mocks.SetupCreateEvent();

        // ── Act 1: Sophie sends a natural language message ──
        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateMessageActivity(
                "Plan een halfuur met Jan voor dinsdag middag.",
                TestPersonas.Sophie.UserId,
                TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                // Capture all responses
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        // ── Assert 1: Bot sent a typing indicator + Adaptive Card ──
        activities.Should().Contain(a => a.Type == ActivityTypes.Typing,
            "bot should show typing indicator while processing");

        var cardActivity = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message &&
            ((Activity)a).Attachments?.Any(att => att.ContentType == AdaptiveCard.ContentType) == true);
        cardActivity.Should().NotBeNull("bot should respond with an Adaptive Card");

        // ── Assert 1b: Verify Adaptive Card content ──
        var attachment = ((Activity)cardActivity!).Attachments![0];
        attachment.ContentType.Should().Be(AdaptiveCard.ContentType);

        var cardJson = JsonSerializer.Serialize(attachment.Content);
        cardJson.Should().Contain("1-op-1 met Jan", "card should contain the meeting subject");
        cardJson.Should().Contain("30 minuten", "card should show the duration");

        // Verify: All systems were called
        _mocks.IntentExtractions.Should().HaveCount(1);
        _mocks.ResolvedUsers.Should().Contain("Jan");
        _mocks.RequestsCreated.Should().HaveCount(1);
        _mocks.AuditLogs.Should().Contain(a => a.Action == "RequestCreated");

        // ── Act 2: Sophie taps "Optie 1" on the Adaptive Card ──
        var requestId = _mocks.RequestsCreated[0].RequestId;
        var selectionActivities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateCardActionActivity(
                new { action = "selectSlot", requestId, slotIndex = 0 },
                TestPersonas.Sophie.UserId,
                TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    selectionActivities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        // ── Assert 2: Confirmation card sent ──
        var confirmActivity = selectionActivities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message &&
            ((Activity)a).Attachments?.Any(att => att.ContentType == AdaptiveCard.ContentType) == true);
        confirmActivity.Should().NotBeNull("bot should send a confirmation Adaptive Card");

        var confirmJson = JsonSerializer.Serialize(((Activity)confirmActivity!).Attachments![0].Content);
        confirmJson.Should().Contain("gepland", "confirmation card should indicate success");

        // Verify: Event was created
        _mocks.EventsCreated.Should().HaveCount(1);
        _mocks.AuditLogs.Should().Contain(a => a.Action == "MeetingBooked");
    }

    // ──────────────────────────────────────────────
    // E2E: Group meeting with conflicts → card shows conditional slots
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_GroupMeetingWithConflicts_CardShowsConflictInfo()
    {
        var intent = new MeetingIntent
        {
            Subject = "Productlancering review",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = TestPersonas.NextTuesday,
                EndDate = TestPersonas.NextTuesday.AddDays(3)
            },
            Participants =
            [
                new ParticipantReference { Name = "Marketing-team", Type = ParticipantType.Group, IsRequired = true },
                new ParticipantReference { Name = "Pieter", Type = ParticipantType.User, IsRequired = true }
            ],
            Priority = MeetingPriority.High,
            IsOnline = true
        };

        _mocks.SetupIntentExtraction(intent);

        // Two slots: one clean, one with Jan + Pieter blocked
        var cleanSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextThursday.AddHours(14),
            End = TestPersonas.NextThursday.AddHours(15),
            Confidence = SlotConfidence.Full,
            AvailabilityScore = 1.0,
            Conflicts = []
        };

        var conflictedSlot = new ProposedTimeSlot
        {
            Start = TestPersonas.NextTuesday.AddHours(10),
            End = TestPersonas.NextTuesday.AddHours(11),
            Confidence = SlotConfidence.Conditional,
            AvailabilityScore = 0.4,
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
                },
                new SlotConflict
                {
                    UserId = TestPersonas.Pieter.UserId,
                    DisplayName = TestPersonas.Pieter.DisplayName,
                    ConflictingEventSubject = "Code review sessie",
                    ConflictingEventStart = TestPersonas.NextTuesday.AddHours(10),
                    ConflictingEventEnd = TestPersonas.NextTuesday.AddHours(11),
                    IsRecurring = true,
                    Sensitivity = "normal",
                    Importance = "low"
                }
            ]
        };

        _mocks.SetupFindMeetingTimes([cleanSlot, conflictedSlot]);

        // AI analysis for both conflicts
        _mocks.SetupConflictAnalysis(new Dictionary<string, ConflictAnalysis>
        {
            [TestPersonas.Jan.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.AskParticipant,
                Reasoning = "Terugkerend overleg, kan verplaatst worden.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = TestPersonas.NextTuesday.AddHours(13),
                    End = TestPersonas.NextTuesday.AddHours(14)
                },
                BlockedByUserId = TestPersonas.Jan.UserId
            },
            [TestPersonas.Pieter.UserId] = new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.AskParticipant,
                Reasoning = "Code review kan verplaatst worden.",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = TestPersonas.NextTuesday.AddHours(14),
                    End = TestPersonas.NextTuesday.AddHours(15)
                },
                BlockedByUserId = TestPersonas.Pieter.UserId
            }
        });

        _mocks.SetupChatCreation();
        _mocks.SetupCreateEvent();

        // ── Act ──
        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateMessageActivity(
                "Plan een productlancering review van 1 uur met het Marketing-team en Pieter.",
                TestPersonas.Sophie.UserId,
                TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        // ── Assert: Card was sent ──
        var cardActivity = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message &&
            ((Activity)a).Attachments?.Any(att => att.ContentType == AdaptiveCard.ContentType) == true);
        cardActivity.Should().NotBeNull();

        var cardJson = JsonSerializer.Serialize(((Activity)cardActivity!).Attachments![0].Content);

        // Verify card content
        cardJson.Should().Contain("Productlancering review");
        cardJson.Should().Contain("60 minuten");

        // Verify: Both chats created (Jan + Pieter negotiations)
        _mocks.ChatsCreated.Should().HaveCount(2);

        // Verify: Group was resolved
        _mocks.ResolvedGroups.Should().Contain("Marketing-team");

        // Verify: Full audit trail
        _mocks.AuditLogs.Where(a => a.Action == "ConflictNegotiationStarted").Should().HaveCount(2);

        // ── Act 2: Sophie selects the clean slot (Thursday 14:00) ──
        var requestId = _mocks.RequestsCreated[0].RequestId;
        var selectionActivities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateCardActionActivity(
                new { action = "selectSlot", requestId, slotIndex = 0 },
                TestPersonas.Sophie.UserId,
                TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    selectionActivities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        // ── Assert: Meeting booked ──
        _mocks.EventsCreated.Should().HaveCount(1);

        var finalRequest = _mocks.GetStoredRequest(requestId);
        finalRequest.Should().NotBeNull();
        finalRequest!.Status.Should().Be(SchedulingStatus.Completed);
        finalRequest.CreatedEventId.Should().NotBeNullOrEmpty();
    }

    // ──────────────────────────────────────────────
    // E2E: User sends cancel action
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_UserCancels_ReceivesCancellationMessage()
    {
        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateCardActionActivity(
                new { action = "cancel", requestId = "req-cancel-test" },
                TestPersonas.Sophie.UserId,
                TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        var textReply = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message && ((Activity)a).Text?.Contains("geannuleerd") == true);
        textReply.Should().NotBeNull("bot should confirm cancellation in Dutch");
    }

    // ──────────────────────────────────────────────
    // E2E: Empty message → helpful prompt
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_EmptyMessage_ReturnsHelpPrompt()
    {
        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateMessageActivity("", TestPersonas.Sophie.UserId, TestPersonas.Sophie.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        var helpMessage = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message && ((Activity)a).Text is not null);
        helpMessage.Should().NotBeNull();
        ((Activity)helpMessage!).Text.Should().Contain("bericht nodig");
    }

    // ──────────────────────────────────────────────
    // E2E: Conflict response via card action
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_JanAcceptsConflictViaCard_ConfirmationSent()
    {
        // Inject a pending conflict state for Jan
        var requestId = "req-bot-conflict";
        _mocks.InjectConflictState(new ConflictResolutionStateDocument
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
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(3)
        });

        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        await adapter.ProcessActivityAsync(
            CreateCardActionActivity(
                new
                {
                    action = "conflictResponse",
                    requestId,
                    conflictUserId = TestPersonas.Jan.UserId,
                    response = "Accepted"
                },
                TestPersonas.Jan.UserId,
                TestPersonas.Jan.DisplayName),
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        // ── Assert: Jan gets confirmation ──
        var confirmMessage = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message && ((Activity)a).Text?.Contains("verplaatst") == true);
        confirmMessage.Should().NotBeNull("Jan should receive a Dutch confirmation message");

        // ── Assert: State updated ──
        _mocks.ConflictStatesUpdated.Should().HaveCount(1);
        _mocks.ConflictStatesUpdated[0].ResponseReceived.Should().Be(ConflictResponse.Accepted);
        _mocks.ConflictStatesUpdated[0].PendingResponse.Should().BeFalse();

        // ── Assert: Audit logged ──
        _mocks.AuditLogs.Should().Contain(a =>
            a.Action == "ConflictResponseReceived" &&
            a.ActorId == TestPersonas.Jan.UserId);
    }

    // ──────────────────────────────────────────────
    // E2E: Welcome message when user joins
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FullE2E_NewMemberAdded_WelcomeMessageSent()
    {
        var adapter = new TestAdapter(Channels.Msteams);
        var activities = new List<IActivity>();

        var conversationUpdate = new Activity
        {
            Type = ActivityTypes.ConversationUpdate,
            MembersAdded =
            [
                new ChannelAccount(TestPersonas.Sophie.UserId, TestPersonas.Sophie.DisplayName)
            ],
            Recipient = new ChannelAccount("bot-id", "DigiSandra Bot"),
            Conversation = new ConversationAccount(id: "conv-welcome"),
            ChannelId = Channels.Msteams
        };

        await adapter.ProcessActivityAsync(
            conversationUpdate,
            async (turnContext, ct) =>
            {
                turnContext.OnSendActivities(async (ctx, acts, next) =>
                {
                    activities.AddRange(acts);
                    return await next();
                });
                await _bot.OnTurnAsync(turnContext, ct);
            },
            CancellationToken.None);

        var welcome = activities.FirstOrDefault(a =>
            a.Type == ActivityTypes.Message && ((Activity)a).Text is not null);
        welcome.Should().NotBeNull();
        ((Activity)welcome!).Text.Should().Contain("DigiSandra");
        ((Activity)welcome!).Text.Should().Contain("planningsassistent");
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static Activity CreateMessageActivity(string text, string userId, string userName) => new()
    {
        Type = ActivityTypes.Message,
        Text = text,
        From = new ChannelAccount(userId, userName) { Properties = CreateAadProperties(userId) },
        Conversation = new ConversationAccount(id: $"conv-{Guid.NewGuid():N}"),
        ChannelId = Channels.Msteams
    };

    private static Activity CreateCardActionActivity(object value, string userId, string userName) => new()
    {
        Type = ActivityTypes.Message,
        Value = value,
        Text = null,
        From = new ChannelAccount(userId, userName) { Properties = CreateAadProperties(userId) },
        Conversation = new ConversationAccount(id: $"conv-{Guid.NewGuid():N}"),
        ChannelId = Channels.Msteams
    };

    private static Newtonsoft.Json.Linq.JObject CreateAadProperties(string userId) =>
        Newtonsoft.Json.Linq.JObject.FromObject(new { aadObjectId = userId });
}
