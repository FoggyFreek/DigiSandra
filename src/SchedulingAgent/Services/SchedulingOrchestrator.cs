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

        var resolvedParticipants = await ResolveAllParticipantsAsync(intent.Participants, ct);

        // Add the requester as first participant if not already included
        if (resolvedParticipants.All(p => p.UserId != requesterId))
        {
            resolvedParticipants.Insert(0, new ResolvedParticipant
            {
                UserId = requesterId,
                DisplayName = requesterName,
                Email = requesterName,
                IsRequired = true
            });
        }

        request.ResolvedParticipants = resolvedParticipants;

        if (resolvedParticipants.Count < 2)
        {
            logger.LogWarning("Could not resolve enough participants for request {RequestId}", requestId);
            request.Status = SchedulingStatus.Failed;
            return await cosmosDbService.UpdateRequestAsync(request, ct);
        }

        // Step 3: Check availability via Graph
        request.Status = SchedulingStatus.CheckingAvailability;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        var proposedSlots = await graphService.FindMeetingTimesAsync(
            resolvedParticipants, intent.TimeWindow, intent.DurationMinutes, ct);

        // Fallback to getSchedule if findMeetingTimes returns no results
        if (proposedSlots.Count == 0)
        {
            logger.LogInformation("Using getSchedule fallback for request {RequestId}", requestId);
            var scheduleItems = await graphService.GetScheduleAsync(
                resolvedParticipants.Select(p => p.Email).ToList(),
                intent.TimeWindow.StartDate,
                intent.TimeWindow.EndDate,
                ct);

            proposedSlots = FindAvailableSlots(scheduleItems, intent.TimeWindow, intent.DurationMinutes);
        }

        // Step 4: Analyze and resolve conflicts
        if (proposedSlots.Any(s => s.Conflicts.Count > 0))
        {
            request.Status = SchedulingStatus.ResolvingConflicts;
            request = await cosmosDbService.UpdateRequestAsync(request, ct);

            proposedSlots = await conflictService.ResolveConflictsAsync(request, proposedSlots, ct);
        }

        // Step 5: Present options to user
        request.ProposedSlots = proposedSlots.Take(3).ToList();
        request.Status = SchedulingStatus.PendingUserSelection;
        request = await cosmosDbService.UpdateRequestAsync(request, ct);

        logger.LogInformation(
            "Request {RequestId} ready for user selection with {Count} proposed slots",
            requestId, request.ProposedSlots.Count);

        return request;
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
        return request;
    }

    private async Task<List<ResolvedParticipant>> ResolveAllParticipantsAsync(
        List<ParticipantReference> participants, CancellationToken ct)
    {
        var resolved = new List<ResolvedParticipant>();
        var unresolvedNames = new List<string>();

        foreach (var participant in participants)
        {
            switch (participant.Type)
            {
                case ParticipantType.User:
                    var user = await graphService.ResolveUserAsync(participant.Name, ct);
                    if (user is not null)
                    {
                        resolved.Add(user with { IsRequired = participant.IsRequired });
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

        // Deduplicate by UserId
        return resolved
            .GroupBy(p => p.UserId)
            .Select(g => g.First())
            .ToList();
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

        while (current.Add(duration) <= endDate && slots.Count < 3)
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

        return slots.OrderByDescending(s => s.AvailabilityScore).Take(3).ToList();
    }
}
