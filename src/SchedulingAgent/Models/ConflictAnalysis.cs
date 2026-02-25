using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Result of the AI-driven conflict analysis from GPT-4o.
/// </summary>
public sealed record ConflictAnalysis
{
    [JsonPropertyName("canAutoResolve")]
    public required bool CanAutoResolve { get; init; }

    [JsonPropertyName("strategy")]
    public required ConflictStrategy Strategy { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }

    [JsonPropertyName("suggestedAlternativeSlot")]
    public AlternativeSlot? SuggestedAlternativeSlot { get; init; }

    [JsonPropertyName("blockedByUserId")]
    public string? BlockedByUserId { get; init; }

    [JsonPropertyName("blockedByEventType")]
    public string? BlockedByEventType { get; init; }
}

public sealed record AlternativeSlot
{
    [JsonPropertyName("start")]
    public required DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public required DateTimeOffset End { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConflictStrategy
{
    ProposeReschedule,
    AskParticipant,
    SuggestAlternativeSlot,
    ReduceAttendees,
    Escalate
}
