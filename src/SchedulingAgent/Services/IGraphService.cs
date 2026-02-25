using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public interface IGraphService
{
    Task<ResolvedParticipant?> ResolveUserAsync(string displayName, CancellationToken ct = default);
    Task<List<ResolvedParticipant>> ResolveGroupMembersAsync(string groupName, CancellationToken ct = default);
    Task<List<ProposedTimeSlot>> FindMeetingTimesAsync(
        List<ResolvedParticipant> participants,
        TimeWindow timeWindow,
        int durationMinutes,
        CancellationToken ct = default);
    Task<List<ScheduleItem>> GetScheduleAsync(
        List<string> userIds,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default);
    Task<string> CreateEventAsync(
        string organizerId,
        string subject,
        DateTimeOffset start,
        DateTimeOffset end,
        List<ResolvedParticipant> attendees,
        bool isOnline,
        CancellationToken ct = default);
    Task<string> CreateChatAsync(string userId, CancellationToken ct = default);
    Task SendChatMessageAsync(string chatId, string message, CancellationToken ct = default);
}

public sealed record ScheduleItem
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public required string Status { get; init; }
    public string? Subject { get; init; }
    public bool IsRecurring { get; init; }
    public string Sensitivity { get; init; } = "normal";
    public string Importance { get; init; } = "normal";
}
