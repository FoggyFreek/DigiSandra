using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public interface ICosmosDbService
{
    Task<SchedulingRequestDocument> CreateRequestAsync(SchedulingRequestDocument request, CancellationToken ct = default);
    Task<SchedulingRequestDocument?> GetRequestAsync(string requestId, CancellationToken ct = default);
    Task<SchedulingRequestDocument> UpdateRequestAsync(SchedulingRequestDocument request, CancellationToken ct = default);
    Task<ConflictResolutionStateDocument> CreateConflictStateAsync(ConflictResolutionStateDocument state, CancellationToken ct = default);
    Task<ConflictResolutionStateDocument?> GetConflictStateAsync(string requestId, string conflictUserId, CancellationToken ct = default);
    Task<ConflictResolutionStateDocument> UpdateConflictStateAsync(ConflictResolutionStateDocument state, CancellationToken ct = default);
    Task<List<ConflictResolutionStateDocument>> GetExpiredConflictsAsync(CancellationToken ct = default);
    Task CreateAuditLogAsync(AuditLogDocument auditLog, CancellationToken ct = default);
}
