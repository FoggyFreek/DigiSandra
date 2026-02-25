using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Cosmos DB document tracking the state of a conflict resolution interaction.
/// Used when the agent reaches out to a participant to resolve a scheduling conflict.
/// </summary>
public sealed class ConflictResolutionStateDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType => "ConflictResolutionState";

    [JsonPropertyName("conflictUserId")]
    public required string ConflictUserId { get; set; }

    [JsonPropertyName("conflictUserName")]
    public required string ConflictUserName { get; set; }

    [JsonPropertyName("chatId")]
    public string? ChatId { get; set; }

    [JsonPropertyName("originalEventSubject")]
    public required string OriginalEventSubject { get; set; }

    [JsonPropertyName("originalEventStart")]
    public required DateTimeOffset OriginalEventStart { get; set; }

    [JsonPropertyName("originalEventEnd")]
    public required DateTimeOffset OriginalEventEnd { get; set; }

    [JsonPropertyName("proposedNewStart")]
    public required DateTimeOffset ProposedNewStart { get; set; }

    [JsonPropertyName("proposedNewEnd")]
    public required DateTimeOffset ProposedNewEnd { get; set; }

    [JsonPropertyName("pendingResponse")]
    public bool PendingResponse { get; set; } = true;

    [JsonPropertyName("responseReceived")]
    public ConflictResponse? ResponseReceived { get; set; }

    [JsonPropertyName("expirationTime")]
    public required DateTimeOffset ExpirationTime { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 604800;

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConflictResponse
{
    Accepted,
    Declined,
    CounterProposed,
    TimedOut
}
