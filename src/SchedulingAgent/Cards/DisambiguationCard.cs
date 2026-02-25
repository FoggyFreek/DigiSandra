using AdaptiveCards;
using SchedulingAgent.Models;

namespace SchedulingAgent.Cards;

public static class DisambiguationCard
{
    public static AdaptiveCard Build(SchedulingRequestDocument request)
    {
        var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Deelnemer verduidelijking nodig",
                    Size = AdaptiveTextSize.Large,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Accent
                },
                new AdaptiveTextBlock
                {
                    Text = "Meerdere personen gevonden voor de volgende namen. Kies de juiste persoon.",
                    Wrap = true,
                    IsSubtle = true
                }
            ]
        };

        foreach (var item in request.PendingDisambiguations ?? [])
        {
            card.Body.Add(new AdaptiveTextBlock
            {
                Text = $"Wie bedoel je met \"{item.RequestedName}\"?",
                Weight = AdaptiveTextWeight.Bolder,
                Spacing = AdaptiveSpacing.Medium
            });

            card.Body.Add(new AdaptiveChoiceSetInput
            {
                Id = item.RequestedName,
                Style = AdaptiveChoiceInputStyle.Expanded,
                IsRequired = item.IsRequired,
                Choices = item.Candidates
                    .Select(c => new AdaptiveChoice { Title = c.DisplayName, Value = c.UserId })
                    .ToList()
            });
        }

        card.Actions.Add(new AdaptiveSubmitAction
        {
            Title = "Bevestigen",
            Data = new { action = "disambiguate", requestId = request.RequestId }
        });

        card.Actions.Add(new AdaptiveSubmitAction
        {
            Title = "Annuleren",
            Data = new { action = "cancel", requestId = request.RequestId },
            Style = "destructive"
        });

        return card;
    }
}
