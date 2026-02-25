using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Cosmos DB document representing a scheduling request and its lifecycle.
/// Partition key: /requestId
/// </summary>
public sealed class SchedulingRequestDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType => "SchedulingRequest";

    [JsonPropertyName("requesterId")]
    public required string RequesterId { get; set; }

    [JsonPropertyName("requesterName")]
    public required string RequesterName { get; set; }

    [JsonPropertyName("conversationId")]
    public required string ConversationId { get; set; }

    [JsonPropertyName("intent")]
    public required MeetingIntent Intent { get; set; }

    [JsonPropertyName("resolvedParticipants")]
    public List<ResolvedParticipant> ResolvedParticipants { get; set; } = [];

    [JsonPropertyName("proposedSlots")]
    public List<ProposedTimeSlot> ProposedSlots { get; set; } = [];

    [JsonPropertyName("selectedSlotIndex")]
    public int? SelectedSlotIndex { get; set; }

    [JsonPropertyName("status")]
    public SchedulingStatus Status { get; set; } = SchedulingStatus.Received;

    [JsonPropertyName("createdEventId")]
    public string? CreatedEventId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 604800; // 7 days

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public sealed record ResolvedParticipant
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; init; } = true;

    [JsonPropertyName("resolvedFromGroup")]
    public string? ResolvedFromGroup { get; init; }
}

public sealed record ProposedTimeSlot
{
    [JsonPropertyName("start")]
    public required DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public required DateTimeOffset End { get; init; }

    [JsonPropertyName("confidence")]
    public required SlotConfidence Confidence { get; init; }

    [JsonPropertyName("availabilityScore")]
    public double AvailabilityScore { get; init; } = 1.0;

    [JsonPropertyName("conflicts")]
    public List<SlotConflict> Conflicts { get; init; } = [];
}

public sealed record SlotConflict
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("conflictingEventSubject")]
    public required string ConflictingEventSubject { get; init; }

    [JsonPropertyName("conflictingEventStart")]
    public required DateTimeOffset ConflictingEventStart { get; init; }

    [JsonPropertyName("conflictingEventEnd")]
    public required DateTimeOffset ConflictingEventEnd { get; init; }

    [JsonPropertyName("isRecurring")]
    public bool IsRecurring { get; init; }

    [JsonPropertyName("sensitivity")]
    public string Sensitivity { get; init; } = "normal";

    [JsonPropertyName("importance")]
    public string Importance { get; init; } = "normal";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchedulingStatus
{
    Received,
    ParsingIntent,
    ResolvingParticipants,
    CheckingAvailability,
    ResolvingConflicts,
    PendingUserSelection,
    Booking,
    Completed,
    Cancelled,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SlotConfidence
{
    Full,
    Conditional,
    Low
}
