using System.Text.Json.Serialization;

namespace SchedulingAgent.Models;

/// <summary>
/// Cosmos DB document for audit trail logging.
/// Logs metadata only — never meeting content.
/// </summary>
public sealed class AuditLogDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; set; }

    [JsonPropertyName("documentType")]
    public string DocumentType => "AuditLog";

    [JsonPropertyName("action")]
    public required string Action { get; set; }

    [JsonPropertyName("actorId")]
    public required string ActorId { get; set; }

    [JsonPropertyName("actorType")]
    public required ActorType ActorType { get; set; }

    [JsonPropertyName("details")]
    public Dictionary<string, string> Details { get; set; } = [];

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; } = 604800;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActorType
{
    User,
    System,
    Bot
}
