using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Parsed meeting intent extracted from natural language input via Azure OpenAI.
/// </summary>
public sealed record MeetingIntent
{
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("durationMinutes")]
    public required int DurationMinutes { get; init; }

    [JsonPropertyName("timeWindow")]
    public required TimeWindow TimeWindow { get; init; }

    [JsonPropertyName("participants")]
    public required List<ParticipantReference> Participants { get; init; }

    [JsonPropertyName("priority")]
    public MeetingPriority Priority { get; init; } = MeetingPriority.Normal;

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; init; } = true;

    [JsonPropertyName("room")]
    public string? Room { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("recurrence")]
    public RecurrenceInfo? Recurrence { get; init; }
}

public sealed record RecurrenceInfo
{
    [JsonPropertyName("count")]
    public int Count { get; init; } = 1;

    [JsonPropertyName("frequency")]
    public RecurrenceFrequency Frequency { get; init; } = RecurrenceFrequency.Weekly;

    [JsonPropertyName("intervalWeeks")]
    public int IntervalWeeks { get; init; } = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecurrenceFrequency
{
    Weekly,
    BiWeekly,
    Monthly
}

public sealed record TimeWindow
{
    [JsonPropertyName("startDate")]
    public required DateTimeOffset StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public required DateTimeOffset EndDate { get; init; }

    [JsonPropertyName("preferredTimeOfDay")]
    public TimeOfDayPreference? PreferredTimeOfDay { get; init; }

    [JsonPropertyName("preferredDaysOfWeek")]
    public IReadOnlyList<DayOfWeek>? PreferredDaysOfWeek { get; init; }
}

public sealed record ParticipantReference
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public ParticipantType Type { get; init; } = ParticipantType.User;

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; init; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MeetingPriority
{
    Low,
    Normal,
    High,
    Urgent
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParticipantType
{
    User,
    Group,
    DistributionList
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimeOfDayPreference
{
    Morning,
    Afternoon,
    Evening
}
