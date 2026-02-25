using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using SchedulingAgent.Models;
using SchedulingAgent.Prompts;

namespace SchedulingAgent.Services;

public sealed class OpenAIService(
    AzureOpenAIClient openAIClient,
    IOptions<AzureOpenAIOptions> options,
    ILogger<OpenAIService> logger) : IOpenAIService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MeetingIntent> ExtractMeetingIntentAsync(string userMessage, CancellationToken ct = default)
    {
        logger.LogInformation("Extracting meeting intent from user message");

        var chatClient = openAIClient.GetChatClient(options.Value.DeploymentName);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(IntentExtractionPrompt.SystemPrompt),
            new UserChatMessage(userMessage)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "meeting_intent",
                BinaryData.FromString(IntentExtractionPrompt.JsonSchema))
        };

        var response = await chatClient.CompleteChatAsync(messages, chatOptions, ct);
        var content = response.Value.Content[0].Text;

        logger.LogDebug("OpenAI intent extraction response received");

        var intent = JsonSerializer.Deserialize<MeetingIntent>(content, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize meeting intent from OpenAI response");

        return intent;
    }

    public async Task<ConflictAnalysis> AnalyzeConflictAsync(
        SlotConflict conflict,
        MeetingPriority requestPriority,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Analyzing conflict for user {UserId}, event '{Subject}'",
            conflict.UserId, conflict.ConflictingEventSubject);

        var chatClient = openAIClient.GetChatClient(options.Value.DeploymentName);

        var contextJson = JsonSerializer.Serialize(new
        {
            userPriority = requestPriority.ToString(),
            blockedBy = conflict.ConflictingEventSubject,
            isRecurring = conflict.IsRecurring,
            sensitivity = conflict.Sensitivity,
            importance = conflict.Importance,
            conflictStart = conflict.ConflictingEventStart,
            conflictEnd = conflict.ConflictingEventEnd
        }, JsonOptions);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(ConflictResolutionPrompt.SystemPrompt),
            new UserChatMessage(contextJson)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.2f,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "conflict_analysis",
                BinaryData.FromString(ConflictResolutionPrompt.JsonSchema))
        };

        var response = await chatClient.CompleteChatAsync(messages, chatOptions, ct);
        var content = response.Value.Content[0].Text;

        logger.LogDebug("OpenAI conflict analysis response received");

        var analysis = JsonSerializer.Deserialize<ConflictAnalysis>(content, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize conflict analysis from OpenAI response");

        return analysis;
    }
}
