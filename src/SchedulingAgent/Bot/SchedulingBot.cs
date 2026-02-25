using System.Text.Json;
using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using SchedulingAgent.Cards;
using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Bot;

public sealed class SchedulingBot(
    ISchedulingOrchestrator orchestrator,
    IConflictResolutionService conflictService,
    ILogger<SchedulingBot> logger) : ActivityHandler
{
    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
    {
        var userId = turnContext.Activity.From.AadObjectId ?? turnContext.Activity.From.Id;
        var userName = turnContext.Activity.From.Name ?? "Onbekend";
        var conversationId = turnContext.Activity.Conversation.Id;
        var userMessage = turnContext.Activity.Text;

        // Check if this is an Adaptive Card action
        if (turnContext.Activity.Value is not null)
        {
            await HandleCardActionAsync(turnContext, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            await turnContext.SendActivityAsync(
                MessageFactory.Text("Ik heb een bericht nodig om een vergadering te plannen. " +
                    "Beschrijf de vergadering die je wilt plannen."), ct);
            return;
        }

        logger.LogInformation("Received scheduling request from {User}: {Message}", userName, userMessage);

        try
        {
            // Send typing indicator
            await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, ct);

            var request = await orchestrator.ProcessSchedulingRequestAsync(
                userId, userName, conversationId, userMessage, ct);

            if (request.Status == SchedulingStatus.Failed)
            {
                var errorCard = MeetingOptionsCard.BuildError(
                    "Kon niet genoeg deelnemers vinden. Controleer de namen en probeer het opnieuw.");
                await SendAdaptiveCardAsync(turnContext, errorCard, ct);
                return;
            }

            if (request.ProposedSlots.Count == 0)
            {
                var noSlotsCard = MeetingOptionsCard.BuildError(
                    "Er zijn geen beschikbare tijdsloten gevonden in het opgegeven tijdsvenster. " +
                    "Probeer een groter tijdsvenster of minder deelnemers.");
                await SendAdaptiveCardAsync(turnContext, noSlotsCard, ct);
                return;
            }

            var optionsCard = MeetingOptionsCard.Build(request);
            await SendAdaptiveCardAsync(turnContext, optionsCard, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing scheduling request for {User}", userName);
            var errorCard = MeetingOptionsCard.BuildError(
                "Er is een onverwachte fout opgetreden. Probeer het later opnieuw.");
            await SendAdaptiveCardAsync(turnContext, errorCard, ct);
        }
    }

    private async Task HandleCardActionAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
    {
        var valueJson = JsonSerializer.Serialize(turnContext.Activity.Value);
        var action = JsonSerializer.Deserialize<CardAction>(valueJson);

        if (action is null)
        {
            logger.LogWarning("Received unknown card action: {Value}", valueJson);
            return;
        }

        logger.LogInformation("Handling card action: {Action} for request {RequestId}",
            action.Action, action.RequestId);

        switch (action.Action)
        {
            case "selectSlot":
                await HandleSlotSelectionAsync(turnContext, action, ct);
                break;

            case "cancel":
                await turnContext.SendActivityAsync(
                    MessageFactory.Text("Vergaderverzoek geannuleerd."), ct);
                break;

            case "conflictResponse":
                await HandleConflictResponseActionAsync(turnContext, action, ct);
                break;

            default:
                logger.LogWarning("Unknown card action: {Action}", action.Action);
                break;
        }
    }

    private async Task HandleSlotSelectionAsync(
        ITurnContext<IMessageActivity> turnContext, CardAction action, CancellationToken ct)
    {
        try
        {
            await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, ct);

            var request = await orchestrator.HandleSlotSelectionAsync(
                action.RequestId, action.SlotIndex, ct);

            var selectedSlot = request.ProposedSlots[action.SlotIndex];
            var confirmationCard = MeetingOptionsCard.BuildConfirmation(request, selectedSlot);
            await SendAdaptiveCardAsync(turnContext, confirmationCard, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling slot selection for request {RequestId}", action.RequestId);
            var errorCard = MeetingOptionsCard.BuildError(
                "Kon de vergadering niet boeken. Probeer het opnieuw.");
            await SendAdaptiveCardAsync(turnContext, errorCard, ct);
        }
    }

    private async Task HandleConflictResponseActionAsync(
        ITurnContext<IMessageActivity> turnContext, CardAction action, CancellationToken ct)
    {
        try
        {
            var response = Enum.Parse<ConflictResponse>(action.Response);
            await conflictService.HandleConflictResponseAsync(
                action.RequestId, action.ConflictUserId, response, ct);

            var message = response switch
            {
                ConflictResponse.Accepted => "Bedankt! Je afspraak wordt verplaatst.",
                ConflictResponse.Declined => "Begrepen. We zoeken een alternatief tijdslot.",
                _ => "Je reactie is verwerkt."
            };

            await turnContext.SendActivityAsync(MessageFactory.Text(message), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling conflict response for request {RequestId}", action.RequestId);
            await turnContext.SendActivityAsync(
                MessageFactory.Text("Er ging iets mis bij het verwerken van je reactie."), ct);
        }
    }

    private static async Task SendAdaptiveCardAsync(
        ITurnContext turnContext, AdaptiveCard card, CancellationToken ct)
    {
        var attachment = new Attachment
        {
            ContentType = AdaptiveCard.ContentType,
            Content = card
        };

        var activity = MessageFactory.Attachment(attachment);
        await turnContext.SendActivityAsync(activity, ct);
    }

    protected override async Task OnMembersAddedAsync(
        IList<ChannelAccount> membersAdded,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken ct)
    {
        foreach (var member in membersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(
                    MessageFactory.Text(
                        "Hallo! Ik ben de DigiSandra planningsassistent. " +
                        "Beschrijf de vergadering die je wilt plannen en ik regel het voor je.\n\n" +
                        "Voorbeeld: \"Plan een overleg van 1 uur met Jan en het Marketing-team " +
                        "voor volgende week over Project X.\""),
                    ct);
            }
        }
    }
}

internal sealed record CardAction
{
    public string Action { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public int SlotIndex { get; init; }
    public string Response { get; init; } = string.Empty;
    public string ConflictUserId { get; init; } = string.Empty;
}
