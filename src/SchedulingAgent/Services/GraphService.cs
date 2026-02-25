using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.FindMeetingTimes;
using Microsoft.Graph.Users.Item.Calendar.GetSchedule;
using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public sealed class GraphService(
    GraphServiceClient graphClient,
    ILogger<GraphService> logger) : IGraphService
{
    public async Task<ResolvedParticipant?> ResolveUserAsync(string displayName, CancellationToken ct = default)
    {
        logger.LogInformation("Resolving user by display name: {DisplayName}", displayName);

        try
        {
            var users = await graphClient.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"displayName eq '{EscapeODataValue(displayName)}'";
                config.QueryParameters.Select = ["id", "displayName", "mail", "userPrincipalName"];
                config.QueryParameters.Top = 1;
            }, ct);

            var user = users?.Value?.FirstOrDefault();
            if (user is null)
            {
                logger.LogWarning("User not found: {DisplayName}", displayName);
                return null;
            }

            return new ResolvedParticipant
            {
                UserId = user.Id!,
                DisplayName = user.DisplayName!,
                Email = user.Mail ?? user.UserPrincipalName!,
                IsRequired = true
            };
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error resolving user {DisplayName}", displayName);
            throw;
        }
    }

    public async Task<List<ResolvedParticipant>> ResolveGroupMembersAsync(string groupName, CancellationToken ct = default)
    {
        logger.LogInformation("Resolving group members for: {GroupName}", groupName);

        try
        {
            var groups = await graphClient.Groups.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"displayName eq '{EscapeODataValue(groupName)}'";
                config.QueryParameters.Select = ["id", "displayName"];
                config.QueryParameters.Top = 1;
            }, ct);

            var group = groups?.Value?.FirstOrDefault();
            if (group is null)
            {
                logger.LogWarning("Group not found: {GroupName}", groupName);
                return [];
            }

            var members = await graphClient.Groups[group.Id].Members.GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "displayName", "mail", "userPrincipalName"];
            }, ct);

            return members?.Value?
                .OfType<User>()
                .Select(m => new ResolvedParticipant
                {
                    UserId = m.Id!,
                    DisplayName = m.DisplayName!,
                    Email = m.Mail ?? m.UserPrincipalName!,
                    IsRequired = true,
                    ResolvedFromGroup = groupName
                })
                .ToList() ?? [];
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error resolving group {GroupName}", groupName);
            throw;
        }
    }

    public async Task<List<ProposedTimeSlot>> FindMeetingTimesAsync(
        List<ResolvedParticipant> participants,
        TimeWindow timeWindow,
        int durationMinutes,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Finding meeting times for {Count} participants, duration {Duration}min",
            participants.Count, durationMinutes);

        var organizerId = participants.First().UserId;

        try
        {
            var requestBody = new FindMeetingTimesPostRequestBody
            {
                Attendees = participants.Select(p => new AttendeeBase
                {
                    EmailAddress = new EmailAddress { Address = p.Email, Name = p.DisplayName },
                    Type = p.IsRequired ? AttendeeType.Required : AttendeeType.Optional
                }).ToList(),
                TimeConstraint = new TimeConstraint
                {
                    ActivityDomain = ActivityDomain.Work,
                    TimeSlots =
                    [
                        new TimeSlot
                        {
                            Start = new DateTimeTimeZone
                            {
                                DateTime = timeWindow.StartDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                                TimeZone = "UTC"
                            },
                            End = new DateTimeTimeZone
                            {
                                DateTime = timeWindow.EndDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                                TimeZone = "UTC"
                            }
                        }
                    ]
                },
                MeetingDuration = TimeSpan.FromMinutes(durationMinutes),
                MaxCandidates = 3,
                MinimumAttendeePercentage = 100
            };

            var result = await graphClient.Users[organizerId].FindMeetingTimes
                .PostAsync(requestBody, cancellationToken: ct);

            if (result?.MeetingTimeSuggestions is null || result.MeetingTimeSuggestions.Count == 0)
            {
                logger.LogWarning("No meeting time suggestions found, falling back to getSchedule");
                return [];
            }

            return result.MeetingTimeSuggestions.Select(s => new ProposedTimeSlot
            {
                Start = DateTimeOffset.Parse(s.MeetingTimeSlot!.Start!.DateTime!),
                End = DateTimeOffset.Parse(s.MeetingTimeSlot!.End!.DateTime!),
                Confidence = MapConfidence(s.Confidence),
                AvailabilityScore = s.Confidence.HasValue ? s.Confidence.Value / 100.0 : 0.5
            }).ToList();
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error finding meeting times");
            throw;
        }
    }

    public async Task<List<ScheduleItem>> GetScheduleAsync(
        List<string> userIds,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        logger.LogInformation("Getting schedule for {Count} users", userIds.Count);

        var organizerId = userIds.First();

        try
        {
            var requestBody = new GetSchedulePostRequestBody
            {
                Schedules = userIds,
                StartTime = new DateTimeTimeZone
                {
                    DateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                EndTime = new DateTimeTimeZone
                {
                    DateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                AvailabilityViewInterval = 30
            };

            var result = await graphClient.Users[organizerId].Calendar.GetSchedule
                .PostAsGetSchedulePostResponseAsync(requestBody, cancellationToken: ct);

            var items = new List<ScheduleItem>();
            if (result?.Value is null) return items;

            foreach (var schedule in result.Value)
            {
                if (schedule.ScheduleItems is null) continue;

                foreach (var item in schedule.ScheduleItems)
                {
                    items.Add(new ScheduleItem
                    {
                        UserId = schedule.ScheduleId!,
                        DisplayName = schedule.ScheduleId!,
                        Start = DateTimeOffset.Parse(item.Start!.DateTime!),
                        End = DateTimeOffset.Parse(item.End!.DateTime!),
                        Status = item.Status?.ToString() ?? "unknown",
                        Subject = item.Subject,
                        IsRecurring = false // getSchedule does not expose recurrence; full Event data would be needed
                    });
                }
            }

            return items;
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error getting schedule");
            throw;
        }
    }

    public async Task<string> CreateEventAsync(
        string organizerId,
        string subject,
        DateTimeOffset start,
        DateTimeOffset end,
        List<ResolvedParticipant> attendees,
        bool isOnline,
        CancellationToken ct = default)
    {
        logger.LogInformation("Creating event '{Subject}' for {Count} attendees", subject, attendees.Count);

        try
        {
            var newEvent = new Event
            {
                Subject = subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                },
                Attendees = attendees.Select(a => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = a.Email, Name = a.DisplayName },
                    Type = a.IsRequired ? AttendeeType.Required : AttendeeType.Optional
                }).ToList(),
                IsOnlineMeeting = isOnline,
                OnlineMeetingProvider = isOnline ? OnlineMeetingProviderType.TeamsForBusiness : null,
                AllowNewTimeProposals = false
            };

            var created = await graphClient.Users[organizerId].Events.PostAsync(newEvent, cancellationToken: ct);
            logger.LogInformation("Event created with ID: {EventId}", created?.Id);
            return created?.Id ?? throw new InvalidOperationException("Event creation returned null ID");
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error creating event");
            throw;
        }
    }

    public async Task<string> CreateChatAsync(string userId, CancellationToken ct = default)
    {
        logger.LogInformation("Creating 1:1 chat with user {UserId}", userId);

        try
        {
            var chat = new Chat
            {
                ChatType = ChatType.OneOnOne,
                Members =
                [
                    new AadUserConversationMember
                    {
                        Roles = ["owner"],
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{userId}')"
                        }
                    }
                ]
            };

            var created = await graphClient.Chats.PostAsync(chat, cancellationToken: ct);
            return created?.Id ?? throw new InvalidOperationException("Chat creation returned null ID");
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error creating chat with {UserId}", userId);
            throw;
        }
    }

    public async Task SendChatMessageAsync(string chatId, string message, CancellationToken ct = default)
    {
        logger.LogInformation("Sending chat message to chat {ChatId}", chatId);

        try
        {
            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = message
                }
            };

            await graphClient.Chats[chatId].Messages.PostAsync(chatMessage, cancellationToken: ct);
        }
        catch (ServiceException ex)
        {
            logger.LogError(ex, "Graph API error sending message to chat {ChatId}", chatId);
            throw;
        }
    }

    private static SlotConfidence MapConfidence(double? confidence) => confidence switch
    {
        >= 80 => SlotConfidence.Full,
        >= 50 => SlotConfidence.Conditional,
        _ => SlotConfidence.Low
    };

    private static string EscapeODataValue(string value) =>
        value.Replace("'", "''");
}
