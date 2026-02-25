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
}
