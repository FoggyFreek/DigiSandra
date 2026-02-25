using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Tests.Integration;

/// <summary>
/// Factory that creates pre-configured mocks for all external dependencies.
/// Tracks every interaction for full verification across all systems.
/// </summary>
public sealed class MockServiceFactory
{
    public Mock<IGraphService> Graph { get; } = new(MockBehavior.Strict);
    public Mock<IOpenAIService> OpenAI { get; } = new(MockBehavior.Strict);
    public Mock<ICosmosDbService> CosmosDb { get; } = new(MockBehavior.Strict);

    // ──────────────────────────────────────────────
    // Interaction trackers
    // ──────────────────────────────────────────────

    public List<string> ResolvedUsers { get; } = [];
    public List<string> ResolvedGroups { get; } = [];
    public List<string> ChatsCreated { get; } = [];
    public List<(string ChatId, string Message)> MessagesSent { get; } = [];
    public List<string> EventsCreated { get; } = [];
    public List<SchedulingRequestDocument> RequestsCreated { get; } = [];
    public List<SchedulingRequestDocument> RequestsUpdated { get; } = [];
    public List<ConflictResolutionStateDocument> ConflictStatesCreated { get; } = [];
    public List<ConflictResolutionStateDocument> ConflictStatesUpdated { get; } = [];
    public List<AuditLogDocument> AuditLogs { get; } = [];
    public List<string> IntentExtractions { get; } = [];
    public List<(SlotConflict Conflict, MeetingPriority Priority)> ConflictAnalyses { get; } = [];

    // ──────────────────────────────────────────────
    // In-memory stores (simulates Cosmos DB)
    // ──────────────────────────────────────────────

    private readonly Dictionary<string, SchedulingRequestDocument> _requests = new();
    private readonly Dictionary<string, ConflictResolutionStateDocument> _conflictStates = new();

    /// <summary>
    /// Configures all mocks with default behavior for the standard persona set.
    /// Call specialized Setup* methods after this for scenario-specific overrides.
    /// </summary>
    public void ConfigureDefaults()
    {
        SetupUserResolution();
        SetupGroupResolution();
        SetupCosmosDbCrud();
    }

    // ──────────────────────────────────────────────
    // Graph: User resolution
    // ──────────────────────────────────────────────

    private void SetupUserResolution()
    {
        var userMap = new Dictionary<string, ResolvedParticipant>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sophie van den Berg"] = TestPersonas.Sophie,
            ["Sophie"] = TestPersonas.Sophie,
            ["Jan de Vries"] = TestPersonas.Jan,
            ["Jan"] = TestPersonas.Jan,
            ["Fatima El Amrani"] = TestPersonas.Fatima,
            ["Fatima"] = TestPersonas.Fatima,
            ["Pieter Jansen"] = TestPersonas.Pieter,
            ["Pieter"] = TestPersonas.Pieter,
            ["Lisa Bakker"] = TestPersonas.Lisa,
            ["Lisa"] = TestPersonas.Lisa,
            ["Daan Visser"] = TestPersonas.Daan,
            ["Daan"] = TestPersonas.Daan
        };

        Graph.Setup(g => g.ResolveUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
            {
                ResolvedUsers.Add(name);
                return userMap.GetValueOrDefault(name);
            });
    }

    // ──────────────────────────────────────────────
    // Graph: Group resolution
    // ──────────────────────────────────────────────

    private void SetupGroupResolution()
    {
        var groupMap = new Dictionary<string, List<ResolvedParticipant>>(StringComparer.OrdinalIgnoreCase)
        {
            [TestPersonas.MarketingTeamName] = TestPersonas.MarketingTeamMembers,
            [TestPersonas.EngineeringTeamName] = TestPersonas.EngineeringTeamMembers
        };

        Graph.Setup(g => g.ResolveGroupMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
            {
                ResolvedGroups.Add(name);
                return groupMap.GetValueOrDefault(name) ?? [];
            });
    }

    // ──────────────────────────────────────────────
    // Graph: Availability
    // ──────────────────────────────────────────────

    public void SetupFindMeetingTimes(List<ProposedTimeSlot> slots)
    {
        Graph.Setup(g => g.FindMeetingTimesAsync(
                It.IsAny<List<ResolvedParticipant>>(),
                It.IsAny<TimeWindow>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(slots);
    }

    public void SetupFindMeetingTimesEmpty()
    {
        Graph.Setup(g => g.FindMeetingTimesAsync(
                It.IsAny<List<ResolvedParticipant>>(),
                It.IsAny<TimeWindow>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    public void SetupGetSchedule(List<ScheduleItem> items)
    {
        Graph.Setup(g => g.GetScheduleAsync(
                It.IsAny<List<string>>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
    }

    // ──────────────────────────────────────────────
    // Graph: Event creation
    // ──────────────────────────────────────────────

    public void SetupCreateEvent(string eventId = "event-001")
    {
        Graph.Setup(g => g.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<List<ResolvedParticipant>>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string organizer, string subject, DateTimeOffset start, DateTimeOffset end,
                List<ResolvedParticipant> attendees, bool online, CancellationToken _) =>
            {
                var id = $"event-{EventsCreated.Count + 1:D3}";
                EventsCreated.Add(id);
                return id;
            });
    }

    // ──────────────────────────────────────────────
    // Graph: Chat (conflict negotiation)
    // ──────────────────────────────────────────────

    public void SetupChatCreation()
    {
        Graph.Setup(g => g.CreateChatAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, CancellationToken _) =>
            {
                var chatId = $"chat-{userId}";
                ChatsCreated.Add(chatId);
                return chatId;
            });

        Graph.Setup(g => g.SendChatMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string chatId, string message, CancellationToken _) =>
            {
                MessagesSent.Add((chatId, message));
                return Task.CompletedTask;
            });
    }

    // ──────────────────────────────────────────────
    // OpenAI: Intent extraction
    // ──────────────────────────────────────────────

    public void SetupIntentExtraction(MeetingIntent intent)
    {
        OpenAI.Setup(o => o.ExtractMeetingIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string message, CancellationToken _) =>
            {
                IntentExtractions.Add(message);
                return intent;
            });
    }

    // ──────────────────────────────────────────────
    // OpenAI: Conflict analysis
    // ──────────────────────────────────────────────

    public void SetupConflictAnalysis(Dictionary<string, ConflictAnalysis> analysisByUserId)
    {
        OpenAI.Setup(o => o.AnalyzeConflictAsync(It.IsAny<SlotConflict>(), It.IsAny<MeetingPriority>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SlotConflict conflict, MeetingPriority priority, CancellationToken _) =>
            {
                ConflictAnalyses.Add((conflict, priority));
                return analysisByUserId.GetValueOrDefault(conflict.UserId)
                    ?? new ConflictAnalysis
                    {
                        CanAutoResolve = false,
                        Strategy = ConflictStrategy.Escalate,
                        Reasoning = "Geen specifieke analyse beschikbaar"
                    };
            });
    }

    public void SetupSingleConflictAnalysis(ConflictAnalysis analysis)
    {
        OpenAI.Setup(o => o.AnalyzeConflictAsync(It.IsAny<SlotConflict>(), It.IsAny<MeetingPriority>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SlotConflict conflict, MeetingPriority priority, CancellationToken _) =>
            {
                ConflictAnalyses.Add((conflict, priority));
                return analysis;
            });
    }

    // ──────────────────────────────────────────────
    // Cosmos DB: CRUD with in-memory store
    // ──────────────────────────────────────────────

    private void SetupCosmosDbCrud()
    {
        CosmosDb.Setup(c => c.CreateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) =>
            {
                doc.ETag = Guid.NewGuid().ToString();
                _requests[doc.RequestId] = doc;
                RequestsCreated.Add(doc);
                return doc;
            });

        CosmosDb.Setup(c => c.GetRequestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string requestId, CancellationToken _) =>
                _requests.GetValueOrDefault(requestId));

        CosmosDb.Setup(c => c.UpdateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) =>
            {
                doc.ETag = Guid.NewGuid().ToString();
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                _requests[doc.RequestId] = doc;
                RequestsUpdated.Add(doc);
                return doc;
            });

        CosmosDb.Setup(c => c.CreateConflictStateAsync(It.IsAny<ConflictResolutionStateDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConflictResolutionStateDocument doc, CancellationToken _) =>
            {
                doc.ETag = Guid.NewGuid().ToString();
                _conflictStates[doc.Id] = doc;
                ConflictStatesCreated.Add(doc);
                return doc;
            });

        CosmosDb.Setup(c => c.GetConflictStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string requestId, string userId, CancellationToken _) =>
                _conflictStates.Values.FirstOrDefault(s => s.RequestId == requestId && s.ConflictUserId == userId));

        CosmosDb.Setup(c => c.UpdateConflictStateAsync(It.IsAny<ConflictResolutionStateDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConflictResolutionStateDocument doc, CancellationToken _) =>
            {
                doc.ETag = Guid.NewGuid().ToString();
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                _conflictStates[doc.Id] = doc;
                ConflictStatesUpdated.Add(doc);
                return doc;
            });

        CosmosDb.Setup(c => c.GetExpiredConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                _conflictStates.Values
                    .Where(s => s.PendingResponse && s.ExpirationTime < DateTimeOffset.UtcNow)
                    .ToList());

        CosmosDb.Setup(c => c.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns((AuditLogDocument doc, CancellationToken _) =>
            {
                AuditLogs.Add(doc);
                return Task.CompletedTask;
            });
    }

    // ──────────────────────────────────────────────
    // Inject expired conflicts for timeout testing
    // ──────────────────────────────────────────────

    public void InjectConflictState(ConflictResolutionStateDocument state)
    {
        _conflictStates[state.Id] = state;
    }

    public SchedulingRequestDocument? GetStoredRequest(string requestId) =>
        _requests.GetValueOrDefault(requestId);

    public ConflictResolutionStateDocument? GetStoredConflictState(string id) =>
        _conflictStates.GetValueOrDefault(id);

    public List<ConflictResolutionStateDocument> GetAllConflictStates() =>
        _conflictStates.Values.ToList();
}
