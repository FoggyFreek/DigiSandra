using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public interface ISchedulingOrchestrator
{
    Task<SchedulingRequestDocument> ProcessSchedulingRequestAsync(
        string requesterId,
        string requesterName,
        string conversationId,
        string userMessage,
        CancellationToken ct = default);

    Task<SchedulingRequestDocument> HandleSlotSelectionAsync(
        string requestId,
        int slotIndex,
        CancellationToken ct = default);

    Task<SchedulingRequestDocument> HandleDisambiguationResponseAsync(
        string requestId,
        Dictionary<string, string> selections,
        CancellationToken ct = default);

    Task HandleFeedbackAsync(
        string requestId,
        string requesterId,
        int score,
        string? improvementSuggestion,
        CancellationToken ct = default);
}
