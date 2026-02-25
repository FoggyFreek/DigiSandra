using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public interface IConflictResolutionService
{
    Task<List<ProposedTimeSlot>> ResolveConflictsAsync(
        SchedulingRequestDocument request,
        List<ProposedTimeSlot> slots,
        CancellationToken ct = default);

    Task InitiateConflictNegotiationAsync(
        SchedulingRequestDocument request,
        SlotConflict conflict,
        ConflictAnalysis analysis,
        CancellationToken ct = default);

    Task HandleConflictResponseAsync(
        string requestId,
        string conflictUserId,
        ConflictResponse response,
        CancellationToken ct = default);

    Task ProcessExpiredConflictsAsync(CancellationToken ct = default);
}
