namespace SchedulingAgent.Models;

public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    public required string Endpoint { get; set; }
    public required string DatabaseName { get; set; }
    public required string ContainerName { get; set; }
    public int DefaultTtlSeconds { get; set; } = 604800;
}

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    public required string Endpoint { get; set; }
    public required string DeploymentName { get; set; }
}

public sealed class GraphOptions
{
    public const string SectionName = "MicrosoftGraph";

    public required string TenantId { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
}

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public required string MicrosoftAppId { get; set; }
    public required string MicrosoftAppPassword { get; set; }
}

public sealed class ConflictResolutionOptions
{
    public const string SectionName = "ConflictResolution";

    public int TimeoutHours { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;
}
