using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Cosmos DB document capturing the full lifecycle summary of a completed scheduling request.
/// Written once when the request reaches a terminal state (Completed/Failed/Cancelled).
/// Partition key: /requestId — co-located with the request document.
/// TTL: 90 days to support trend analysis while remaining GDPR-compliant.
/// </summary>
public sealed class RequestSummaryDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType => "RequestSummary";

    [JsonPropertyName("requesterId")]
    public required string RequesterId { get; set; }

    [JsonPropertyName("requesterName")]
    public required string RequesterName { get; set; }

    [JsonPropertyName("outcome")]
    public SchedulingStatus Outcome { get; set; }

    /// <summary>Total elapsed seconds from request creation to terminal state.</summary>
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    /// <summary>Number of slots proposed to the user.</summary>
    [JsonPropertyName("slotCount")]
    public int SlotCount { get; set; }

    /// <summary>Total conflict occurrences across all proposed slots.</summary>
    [JsonPropertyName("conflictCount")]
    public int ConflictCount { get; set; }

    /// <summary>Whether the getSchedule fallback was triggered because findMeetingTimes returned nothing.</summary>
    [JsonPropertyName("usedScheduleFallback")]
    public bool UsedScheduleFallback { get; set; }

    /// <summary>Whether the user was asked to disambiguate participant names.</summary>
    [JsonPropertyName("disambiguationRequired")]
    public bool DisambiguationRequired { get; set; }

    /// <summary>Number of participants in the meeting (resolved).</summary>
    [JsonPropertyName("participantCount")]
    public int ParticipantCount { get; set; }

    /// <summary>Whether the meeting was booked as a recurring series.</summary>
    [JsonPropertyName("isRecurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 7776000; // 90 days
}

/// <summary>
/// Cosmos DB document storing requester feedback after a meeting is booked.
/// Partition key: /requestId — co-located with the request document.
/// TTL: 90 days. Improvement suggestions are user-initiated and stored verbatim.
/// </summary>
public sealed class FeedbackDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType => "Feedback";

    [JsonPropertyName("requesterId")]
    public required string RequesterId { get; set; }

    /// <summary>User satisfaction score: 1 (poor) to 5 (excellent).</summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>Optional free-text improvement suggestion provided by the requester.</summary>
    [JsonPropertyName("improvementSuggestion")]
    public string? ImprovementSuggestion { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 7776000; // 90 days
}
