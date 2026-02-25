using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public sealed class ConflictResolutionService(
    IOpenAIService openAIService,
    IGraphService graphService,
    ICosmosDbService cosmosDbService,
    IOptions<ConflictResolutionOptions> conflictOptions,
    ILogger<ConflictResolutionService> logger) : IConflictResolutionService
{
    public async Task<List<ProposedTimeSlot>> ResolveConflictsAsync(
        SchedulingRequestDocument request,
        List<ProposedTimeSlot> slots,
        CancellationToken ct = default)
    {
        logger.LogInformation("Resolving conflicts for request {RequestId}", request.RequestId);

        var resolvedSlots = new List<ProposedTimeSlot>();

        foreach (var slot in slots)
        {
            if (slot.Conflicts.Count == 0)
            {
                resolvedSlots.Add(slot);
                continue;
            }

            var slotConflicts = new List<SlotConflict>();

            foreach (var conflict in slot.Conflicts)
            {
                var analysis = await openAIService.AnalyzeConflictAsync(
                    conflict, request.Intent.Priority, ct);

                logger.LogInformation(
                    "Conflict analysis for {User}: strategy={Strategy}, canAutoResolve={CanAutoResolve}",
                    conflict.DisplayName, analysis.Strategy, analysis.CanAutoResolve);

                switch (analysis.Strategy)
                {
                    case ConflictStrategy.AskParticipant:
                        await InitiateConflictNegotiationAsync(request, conflict, analysis, ct);
                        slotConflicts.Add(conflict);
                        break;

                    case ConflictStrategy.SuggestAlternativeSlot when analysis.SuggestedAlternativeSlot is not null:
                        resolvedSlots.Add(new ProposedTimeSlot
                        {
                            Start = analysis.SuggestedAlternativeSlot.Start,
                            End = analysis.SuggestedAlternativeSlot.End,
                            Confidence = SlotConfidence.Conditional,
                            AvailabilityScore = 0.7,
                            Conflicts = []
                        });
                        break;

                    case ConflictStrategy.ProposeReschedule:
                        await InitiateConflictNegotiationAsync(request, conflict, analysis, ct);
                        slotConflicts.Add(conflict);
                        break;

                    default:
                        slotConflicts.Add(conflict);
                        break;
                }
            }

            resolvedSlots.Add(slot with { Conflicts = slotConflicts });
        }

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = request.RequestId,
            Action = "ConflictsAnalyzed",
            ActorId = "system",
            ActorType = ActorType.System,
            Details = new Dictionary<string, string>
            {
                ["totalSlots"] = slots.Count.ToString(),
                ["resolvedSlots"] = resolvedSlots.Count.ToString()
            }
        }, ct);

        return resolvedSlots;
    }

    public async Task InitiateConflictNegotiationAsync(
        SchedulingRequestDocument request,
        SlotConflict conflict,
        ConflictAnalysis analysis,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Initiating conflict negotiation with {User} for request {RequestId}",
            conflict.DisplayName, request.RequestId);

        var chatId = await graphService.CreateChatAsync(conflict.UserId, ct);

        var message = BuildConflictMessage(
            conflict.DisplayName,
            request.Intent.Subject,
            conflict.ConflictingEventSubject,
            conflict.ConflictingEventStart,
            analysis.SuggestedAlternativeSlot);

        await graphService.SendChatMessageAsync(chatId, message, ct);

        var state = new ConflictResolutionStateDocument
        {
            Id = $"{request.RequestId}-conflict-{conflict.UserId}",
            RequestId = request.RequestId,
            ConflictUserId = conflict.UserId,
            ConflictUserName = conflict.DisplayName,
            ChatId = chatId,
            OriginalEventSubject = conflict.ConflictingEventSubject,
            OriginalEventStart = conflict.ConflictingEventStart,
            OriginalEventEnd = conflict.ConflictingEventEnd,
            ProposedNewStart = analysis.SuggestedAlternativeSlot?.Start ?? conflict.ConflictingEventStart,
            ProposedNewEnd = analysis.SuggestedAlternativeSlot?.End ?? conflict.ConflictingEventEnd,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(conflictOptions.Value.TimeoutHours)
        };

        await cosmosDbService.CreateConflictStateAsync(state, ct);

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = request.RequestId,
            Action = "ConflictNegotiationStarted",
            ActorId = "system",
            ActorType = ActorType.Bot,
            Details = new Dictionary<string, string>
            {
                ["conflictUserId"] = conflict.UserId,
                ["chatId"] = chatId
            }
        }, ct);
    }

    public async Task HandleConflictResponseAsync(
        string requestId,
        string conflictUserId,
        ConflictResponse response,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Handling conflict response for request {RequestId}, user {UserId}: {Response}",
            requestId, conflictUserId, response);

        var state = await cosmosDbService.GetConflictStateAsync(requestId, conflictUserId, ct)
            ?? throw new InvalidOperationException(
                $"Conflict state not found for request {requestId}, user {conflictUserId}");

        state.PendingResponse = false;
        state.ResponseReceived = response;

        await cosmosDbService.UpdateConflictStateAsync(state, ct);

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            Action = "ConflictResponseReceived",
            ActorId = conflictUserId,
            ActorType = ActorType.User,
            Details = new Dictionary<string, string>
            {
                ["response"] = response.ToString()
            }
        }, ct);
    }

    public async Task ProcessExpiredConflictsAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Processing expired conflict resolutions");

        var expired = await cosmosDbService.GetExpiredConflictsAsync(ct);

        foreach (var state in expired)
        {
            logger.LogWarning(
                "Conflict resolution expired for request {RequestId}, user {UserId}",
                state.RequestId, state.ConflictUserId);

            state.PendingResponse = false;
            state.ResponseReceived = ConflictResponse.TimedOut;

            await cosmosDbService.UpdateConflictStateAsync(state, ct);

            await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
            {
                Id = Guid.NewGuid().ToString(),
                RequestId = state.RequestId,
                Action = "ConflictResolutionTimedOut",
                ActorId = "system",
                ActorType = ActorType.System,
                Details = new Dictionary<string, string>
                {
                    ["conflictUserId"] = state.ConflictUserId
                }
            }, ct);
        }
    }

    private static string BuildConflictMessage(
        string userName,
        string meetingSubject,
        string conflictEventSubject,
        DateTimeOffset conflictStart,
        AlternativeSlot? alternative)
    {
        var message = $"Hoi {userName}, je hebt een conflict voor een belangrijke afspraak over '{meetingSubject}'. " +
                      $"Zou je jouw afspraak '{conflictEventSubject}' van {conflictStart:dddd d MMMM HH:mm} " +
                      "kunnen verplaatsen";

        if (alternative is not null)
        {
            message += $" naar {alternative.Start:dddd d MMMM HH:mm}?";
        }
        else
        {
            message += " naar een ander moment?";
        }

        message += "\n\nReageer met 'ja' om te verplaatsen of 'nee' om te weigeren.";

        return message;
    }
}
