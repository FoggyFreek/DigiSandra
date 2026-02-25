using Microsoft.Extensions.Logging;
using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public sealed class SchedulingOrchestrator(
    IOpenAIService openAIService,
    IGraphService graphService,
    ICosmosDbService cosmosDbService,
    IConflictResolutionService conflictService,
    ILogger<SchedulingOrchestrator> logger) : ISchedulingOrchestrator
{
    public async Task<SchedulingRequestDocument> ProcessSchedulingRequestAsync(
        string requesterId,
        string requesterName,
        string conversationId,
        string userMessage,
        CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString();
        logger.LogInformation("Processing scheduling request {RequestId} from {Requester}", requestId, requesterName);

        // Step 1: Parse intent via OpenAI
        var intent = await openAIService.ExtractMeetingIntentAsync(userMessage, ct);
        logger.LogInformation("Intent extracted: subject={Subject}, duration={Duration}min, participants={Count}",
            intent.Subject, intent.DurationMinutes, intent.Participants.Count);

        var request = new SchedulingRequestDocument
        {
            Id = requestId,
            RequestId = requestId,
            RequesterId = requesterId,
            RequesterName = requesterName,
            ConversationId = conversationId,
            Intent = intent,
            Status = SchedulingStatus.ParsingIntent
        };

        request = await cosmosDbService.CreateRequestAsync(request, ct);

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            Action = "RequestCreated",
            ActorId = requesterId,
            ActorType = ActorType.User,
            Details = new Dictionary<string, string>
            {
                ["subject"] = intent.Subject,
                ["participantCount"] = intent.Participants.Count.ToString()
            }
        }, ct);

        // Step 2: Resolve participants via Entra ID / Graph
        request.Status = SchedulingStatus.ResolvingParticipants;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        var (resolvedList, ambiguousList) = await ResolveAllParticipantsAsync(intent.Participants, ct);

        // Add the requester as first participant if not already included
        if (resolvedList.All(p => p.UserId != requesterId))
        {
            resolvedList.Insert(0, new ResolvedParticipant
            {
                UserId = requesterId,
                DisplayName = requesterName,
                Email = requesterName,
                IsRequired = true
            });
        }

        // Early exit: disambiguation required before proceeding
        if (ambiguousList.Count > 0)
        {
            request.PendingDisambiguations = ambiguousList;
            request.ResolvedParticipants = resolvedList;
            request.Status = SchedulingStatus.AwaitingDisambiguation;
            request.DisambiguationRequired = true;
            return await cosmosDbService.UpdateRequestAsync(request, ct);
        }

        request.ResolvedParticipants = resolvedList;

        if (resolvedList.Count < 2)
        {
            logger.LogWarning("Could not resolve enough participants for request {RequestId}", requestId);
            request.Status = SchedulingStatus.Failed;
            return await cosmosDbService.UpdateRequestAsync(request, ct);
        }

        return await ResumeFromCheckingAvailabilityAsync(request, resolvedList, ct);
    }

    public async Task<SchedulingRequestDocument> HandleDisambiguationResponseAsync(
        string requestId,
        Dictionary<string, string> selections,
        CancellationToken ct = default)
    {
        logger.LogInformation("Handling disambiguation response for request {RequestId}", requestId);

        var request = await cosmosDbService.GetRequestAsync(requestId, ct)
            ?? throw new InvalidOperationException($"Request {requestId} not found");

        if (request.Status != SchedulingStatus.AwaitingDisambiguation)
        {
            throw new InvalidOperationException(
                $"Request {requestId} is in status {request.Status}, expected AwaitingDisambiguation");
        }

        var selectedParticipants = new List<ResolvedParticipant>();
        foreach (var disambiguation in request.PendingDisambiguations ?? [])
        {
            if (selections.TryGetValue(disambiguation.RequestedName, out var userId))
            {
                var candidate = disambiguation.Candidates.FirstOrDefault(c => c.UserId == userId);
                if (candidate is not null)
                    selectedParticipants.Add(candidate with { IsRequired = disambiguation.IsRequired });
            }
        }

        var allParticipants = (request.ResolvedParticipants ?? [])
            .Concat(selectedParticipants)
            .GroupBy(p => p.UserId)
            .Select(g => g.First())
            .ToList();

        request.PendingDisambiguations = null;

        if (allParticipants.Count < 2)
        {
            logger.LogWarning("Not enough participants after disambiguation for request {RequestId}", requestId);
            request.ResolvedParticipants = allParticipants;
            request.Status = SchedulingStatus.Failed;
            return await cosmosDbService.UpdateRequestAsync(request, ct);
        }

        return await ResumeFromCheckingAvailabilityAsync(request, allParticipants, ct);
    }

    public async Task<SchedulingRequestDocument> HandleSlotSelectionAsync(
        string requestId,
        int slotIndex,
        CancellationToken ct = default)
    {
        logger.LogInformation("Handling slot selection for request {RequestId}, slot {SlotIndex}",
            requestId, slotIndex);

        var request = await cosmosDbService.GetRequestAsync(requestId, ct)
            ?? throw new InvalidOperationException($"Request {requestId} not found");

        if (request.Status != SchedulingStatus.PendingUserSelection)
        {
            throw new InvalidOperationException(
                $"Request {requestId} is in status {request.Status}, expected PendingUserSelection");
        }

        if (slotIndex < 0 || slotIndex >= request.ProposedSlots.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex),
                $"Slot index {slotIndex} is out of range (0-{request.ProposedSlots.Count - 1})");
        }

        request.SelectedSlotIndex = slotIndex;
        request.Status = SchedulingStatus.Booking;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        var selectedSlot = request.ProposedSlots[slotIndex];

        // Book the meeting via Graph API
        var eventId = await graphService.CreateEventAsync(
            request.RequesterId,
            request.Intent.Subject,
            selectedSlot.Start,
            selectedSlot.End,
            request.ResolvedParticipants,
            request.Intent.IsOnline,
            request.Intent.Recurrence,
            ct);

        request.CreatedEventId = eventId;
        request.Status = SchedulingStatus.Completed;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            Action = "MeetingBooked",
            ActorId = request.RequesterId,
            ActorType = ActorType.User,
            Details = new Dictionary<string, string>
            {
                ["eventId"] = eventId,
                ["slotStart"] = selectedSlot.Start.ToString("o"),
                ["slotEnd"] = selectedSlot.End.ToString("o")
            }
        }, ct);

        logger.LogInformation("Meeting booked for request {RequestId}, event {EventId}", requestId, eventId);

        await SaveRequestSummaryAsync(request, ct);

        return request;
    }

    public async Task HandleFeedbackAsync(
        string requestId,
        string requesterId,
        int score,
        string? improvementSuggestion,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Feedback received for request {RequestId}: score={Score}, hasSuggestion={HasSuggestion}",
            requestId, score, improvementSuggestion is not null);

        var feedback = new FeedbackDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            RequesterId = requesterId,
            Score = score,
            ImprovementSuggestion = improvementSuggestion
        };

        await cosmosDbService.SaveFeedbackAsync(feedback, ct);

        await cosmosDbService.CreateAuditLogAsync(new AuditLogDocument
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            Action = "FeedbackReceived",
            ActorId = requesterId,
            ActorType = ActorType.User,
            Details = new Dictionary<string, string>
            {
                ["score"] = score.ToString(),
                ["hasSuggestion"] = (improvementSuggestion is not null).ToString()
            }
        }, ct);
    }

    private async Task SaveRequestSummaryAsync(SchedulingRequestDocument request, CancellationToken ct)
    {
        var durationSeconds = (int)(DateTimeOffset.UtcNow - request.CreatedAt).TotalSeconds;
        var conflictCount = request.ProposedSlots.Sum(s => s.Conflicts.Count);

        var summary = new RequestSummaryDocument
        {
            Id = $"{request.RequestId}-summary",
            RequestId = request.RequestId,
            RequesterId = request.RequesterId,
            RequesterName = request.RequesterName,
            Outcome = request.Status,
            DurationSeconds = durationSeconds,
            SlotCount = request.ProposedSlots.Count,
            ConflictCount = conflictCount,
            UsedScheduleFallback = request.UsedScheduleFallback,
            DisambiguationRequired = request.DisambiguationRequired,
            ParticipantCount = request.ResolvedParticipants.Count,
            IsRecurring = request.Intent.Recurrence is not null
        };

        await cosmosDbService.SaveRequestSummaryAsync(summary, ct);

        logger.LogInformation(
            "Request summary: RequestId={RequestId} Outcome={Outcome} DurationSeconds={DurationSeconds} " +
            "SlotCount={SlotCount} ConflictCount={ConflictCount} ParticipantCount={ParticipantCount} " +
            "UsedFallback={UsedFallback} DisambiguationRequired={DisambiguationRequired} IsRecurring={IsRecurring}",
            summary.RequestId, summary.Outcome, summary.DurationSeconds,
            summary.SlotCount, summary.ConflictCount, summary.ParticipantCount,
            summary.UsedScheduleFallback, summary.DisambiguationRequired, summary.IsRecurring);
    }

    private async Task<SchedulingRequestDocument> ResumeFromCheckingAvailabilityAsync(
        SchedulingRequestDocument request,
        List<ResolvedParticipant> participants,
        CancellationToken ct)
    {
        request.ResolvedParticipants = participants;
        request.Status = SchedulingStatus.CheckingAvailability;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        var proposedSlots = await graphService.FindMeetingTimesAsync(
            participants, request.Intent.TimeWindow, request.Intent.DurationMinutes, ct);

        // Fallback to getSchedule if findMeetingTimes returns no results
        if (proposedSlots.Count == 0)
        {
            logger.LogInformation("Using getSchedule fallback for request {RequestId}", request.RequestId);
            request.UsedScheduleFallback = true;
            var scheduleItems = await graphService.GetScheduleAsync(
                participants.Select(p => p.Email).ToList(),
                request.Intent.TimeWindow.StartDate,
                request.Intent.TimeWindow.EndDate,
                ct);

            proposedSlots = FindAvailableSlots(scheduleItems, request.Intent.TimeWindow, request.Intent.DurationMinutes);
        }

        // Analyze and resolve conflicts
        if (proposedSlots.Any(s => s.Conflicts.Count > 0))
        {
            request.Status = SchedulingStatus.ResolvingConflicts;
            request = await cosmosDbService.UpdateRequestAsync(request, ct);

            proposedSlots = await conflictService.ResolveConflictsAsync(request, proposedSlots, ct);
        }

        // Present options to user
        request.ProposedSlots = proposedSlots.Take(3).ToList();
        request.Status = SchedulingStatus.PendingUserSelection;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        logger.LogInformation(
            "Request {RequestId} ready for user selection with {Count} proposed slots",
            request.RequestId, request.ProposedSlots.Count);

        return request;
    }

    private async Task<(List<ResolvedParticipant> Resolved, List<DisambiguationItem> Ambiguous)>
        ResolveAllParticipantsAsync(List<ParticipantReference> participants, CancellationToken ct)
    {
        var resolved = new List<ResolvedParticipant>();
        var ambiguous = new List<DisambiguationItem>();
        var unresolvedNames = new List<string>();

        foreach (var participant in participants)
        {
            switch (participant.Type)
            {
                case ParticipantType.User:
                    var result = await graphService.ResolveUserAsync(participant.Name, ct);
                    if (result.IsAmbiguous)
                    {
                        ambiguous.Add(new DisambiguationItem
                        {
                            RequestedName = result.RequestedName,
                            Candidates = result.Candidates,
                            IsRequired = participant.IsRequired
                        });
                    }
                    else if (result.Resolved is not null)
                    {
                        resolved.Add(result.Resolved with { IsRequired = participant.IsRequired });
                    }
                    else
                    {
                        unresolvedNames.Add(participant.Name);
                    }
                    break;

                case ParticipantType.Group:
                case ParticipantType.DistributionList:
                    var members = await graphService.ResolveGroupMembersAsync(participant.Name, ct);
                    if (members.Count > 0)
                    {
                        resolved.AddRange(members.Select(m => m with { IsRequired = participant.IsRequired }));
                    }
                    else
                    {
                        unresolvedNames.Add(participant.Name);
                    }
                    break;
            }
        }

        if (unresolvedNames.Count > 0)
        {
            logger.LogWarning("Could not resolve participants: {Names}", string.Join(", ", unresolvedNames));
        }

        return (resolved.GroupBy(p => p.UserId).Select(g => g.First()).ToList(), ambiguous);
    }

    private static List<ProposedTimeSlot> FindAvailableSlots(
        List<ScheduleItem> scheduleItems,
        TimeWindow timeWindow,
        int durationMinutes)
    {
        var slots = new List<ProposedTimeSlot>();
        var duration = TimeSpan.FromMinutes(durationMinutes);

        // Scan work hours (09:00-17:00) in 30-minute increments
        var current = timeWindow.StartDate.Date.AddHours(9);
        var endDate = timeWindow.EndDate;

        while (current.Add(duration) <= endDate && slots.Count < 50)
        {
            // Skip weekends
            if (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                current = current.AddDays(1).Date.AddHours(9);
                continue;
            }

            // Skip outside work hours
            if (current.Hour < 9 || current.Hour >= 17)
            {
                current = current.AddDays(1).Date.AddHours(9);
                continue;
            }

            var slotEnd = current.Add(duration);
            var conflicts = scheduleItems
                .Where(si => si.Start < slotEnd && si.End > current)
                .Select(si => new SlotConflict
                {
                    UserId = si.UserId,
                    DisplayName = si.DisplayName,
                    ConflictingEventSubject = si.Subject ?? "Unknown",
                    ConflictingEventStart = si.Start,
                    ConflictingEventEnd = si.End,
                    IsRecurring = si.IsRecurring,
                    Sensitivity = si.Sensitivity,
                    Importance = si.Importance
                })
                .ToList();

            var confidence = conflicts.Count == 0 ? SlotConfidence.Full :
                             conflicts.Count <= 1 ? SlotConfidence.Conditional :
                             SlotConfidence.Low;

            slots.Add(new ProposedTimeSlot
            {
                Start = current,
                End = slotEnd,
                Confidence = confidence,
                AvailabilityScore = 1.0 - (conflicts.Count * 0.3),
                Conflicts = conflicts
            });

            current = current.AddMinutes(30);
        }

        slots = ApplyDayOfWeekBoost(slots, timeWindow.PreferredDaysOfWeek);
        return slots.OrderByDescending(s => s.AvailabilityScore).Take(3).ToList();
    }

    private static List<ProposedTimeSlot> ApplyDayOfWeekBoost(
        List<ProposedTimeSlot> slots, IReadOnlyList<DayOfWeek>? preferredDays)
    {
        if (preferredDays is null || preferredDays.Count == 0) return slots;
        return slots.Select(s => preferredDays.Contains(s.Start.DayOfWeek)
            ? s with { AvailabilityScore = s.AvailabilityScore + 0.4 }
            : s).ToList();
    }
}
