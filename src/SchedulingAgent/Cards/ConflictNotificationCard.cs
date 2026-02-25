using AdaptiveCards;
using SchedulingAgent.Models;

namespace SchedulingAgent.Cards;

public static class ConflictNotificationCard
{
    public static AdaptiveCard Build(ConflictResolutionStateDocument state, string meetingSubject)
    {
        return new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Agendaconflict gevonden",
                    Size = AdaptiveTextSize.Large,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Warning
                },
                new AdaptiveTextBlock
                {
                    Text = $"Er is een conflict gevonden voor de vergadering '{meetingSubject}'.",
                    Wrap = true
                },
                new AdaptiveFactSet
                {
                    Facts =
                    [
                        new AdaptiveFact("Jouw afspraak", state.OriginalEventSubject),
                        new AdaptiveFact("Huidige tijd",
                            $"{state.OriginalEventStart:dddd d MMMM HH:mm} - {state.OriginalEventEnd:HH:mm}"),
                        new AdaptiveFact("Voorgestelde nieuwe tijd",
                            $"{state.ProposedNewStart:dddd d MMMM HH:mm} - {state.ProposedNewEnd:HH:mm}")
                    ]
                },
                new AdaptiveTextBlock
                {
                    Text = "Wil je jouw afspraak verplaatsen?",
                    Weight = AdaptiveTextWeight.Bolder,
                    Spacing = AdaptiveSpacing.Large
                }
            ],
            Actions =
            [
                new AdaptiveSubmitAction
                {
                    Title = "Ja, verplaatsen",
                    Style = "positive",
                    Data = new
                    {
                        action = "conflictResponse",
                        requestId = state.RequestId,
                        conflictUserId = state.ConflictUserId,
                        response = "Accepted"
                    }
                },
                new AdaptiveSubmitAction
                {
                    Title = "Nee, niet verplaatsen",
                    Style = "destructive",
                    Data = new
                    {
                        action = "conflictResponse",
                        requestId = state.RequestId,
                        conflictUserId = state.ConflictUserId,
                        response = "Declined"
                    }
                }
            ]
        };
    }
}
