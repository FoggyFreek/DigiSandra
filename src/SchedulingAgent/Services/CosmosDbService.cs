using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchedulingAgent.Models;

namespace SchedulingAgent.Services;

public sealed class CosmosDbService : ICosmosDbService
{
    private readonly Container _container;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(
        CosmosClient cosmosClient,
        IOptions<CosmosDbOptions> options,
        ILogger<CosmosDbService> logger)
    {
        _logger = logger;
        _container = cosmosClient.GetContainer(options.Value.DatabaseName, options.Value.ContainerName);
    }

    public async Task<SchedulingRequestDocument> CreateRequestAsync(
        SchedulingRequestDocument request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating scheduling request {RequestId}", request.RequestId);

        var response = await _container.CreateItemAsync(
            request,
            new PartitionKey(request.RequestId),
            cancellationToken: ct);

        _logger.LogInformation("Scheduling request created, RU charge: {RuCharge}", response.RequestCharge);
        return response.Resource;
    }

    public async Task<SchedulingRequestDocument?> GetRequestAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting scheduling request {RequestId}", requestId);

        try
        {
            var response = await _container.ReadItemAsync<SchedulingRequestDocument>(
                requestId,
                new PartitionKey(requestId),
                cancellationToken: ct);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Scheduling request {RequestId} not found", requestId);
            return null;
        }
    }

    public async Task<SchedulingRequestDocument> UpdateRequestAsync(
        SchedulingRequestDocument request, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating scheduling request {RequestId}, status: {Status}",
            request.RequestId, request.Status);

        request.UpdatedAt = DateTimeOffset.UtcNow;

        var options = new ItemRequestOptions();
        if (request.ETag is not null)
        {
            options.IfMatchEtag = request.ETag;
        }

        var response = await _container.ReplaceItemAsync(
            request,
            request.Id,
            new PartitionKey(request.RequestId),
            options,
            ct);

        _logger.LogInformation("Scheduling request updated, RU charge: {RuCharge}", response.RequestCharge);
        return response.Resource;
    }

    public async Task<ConflictResolutionStateDocument> CreateConflictStateAsync(
        ConflictResolutionStateDocument state, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating conflict state for request {RequestId}, user {UserId}",
            state.RequestId, state.ConflictUserId);

        var response = await _container.CreateItemAsync(
            state,
            new PartitionKey(state.RequestId),
            cancellationToken: ct);

        return response.Resource;
    }

    public async Task<ConflictResolutionStateDocument?> GetConflictStateAsync(
        string requestId, string conflictUserId, CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.requestId = @requestId AND c.conflictUserId = @userId AND c.documentType = 'ConflictResolutionState'")
            .WithParameter("@requestId", requestId)
            .WithParameter("@userId", conflictUserId);

        using var iterator = _container.GetItemQueryIterator<ConflictResolutionStateDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(requestId) });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<ConflictResolutionStateDocument> UpdateConflictStateAsync(
        ConflictResolutionStateDocument state, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Updating conflict state for request {RequestId}, user {UserId}",
            state.RequestId, state.ConflictUserId);

        state.UpdatedAt = DateTimeOffset.UtcNow;

        var options = new ItemRequestOptions();
        if (state.ETag is not null)
        {
            options.IfMatchEtag = state.ETag;
        }

        var response = await _container.ReplaceItemAsync(
            state,
            state.Id,
            new PartitionKey(state.RequestId),
            options,
            ct);

        return response.Resource;
    }

    public async Task<List<ConflictResolutionStateDocument>> GetExpiredConflictsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Querying for expired conflict resolution states");

        var now = DateTimeOffset.UtcNow.ToString("o");
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.documentType = 'ConflictResolutionState' AND c.pendingResponse = true AND c.expirationTime < @now")
            .WithParameter("@now", now);

        var results = new List<ConflictResolutionStateDocument>();
        using var iterator = _container.GetItemQueryIterator<ConflictResolutionStateDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            results.AddRange(response);
        }

        _logger.LogInformation("Found {Count} expired conflict states", results.Count);
        return results;
    }

    public async Task CreateAuditLogAsync(AuditLogDocument auditLog, CancellationToken ct = default)
    {
        _logger.LogDebug("Creating audit log for request {RequestId}, action: {Action}",
            auditLog.RequestId, auditLog.Action);

        await _container.CreateItemAsync(
            auditLog,
            new PartitionKey(auditLog.RequestId),
            cancellationToken: ct);
    }
}
