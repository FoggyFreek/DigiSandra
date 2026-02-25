using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public interface IOpenAIService
{
    Task<MeetingIntent> ExtractMeetingIntentAsync(string userMessage, CancellationToken ct = default);
    Task<ConflictAnalysis> AnalyzeConflictAsync(
        SlotConflict conflict,
        MeetingPriority requestPriority,
        CancellationToken ct = default);
}
