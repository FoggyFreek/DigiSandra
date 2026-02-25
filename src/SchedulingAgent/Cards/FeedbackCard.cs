using AdaptiveCards;
using SchedulingAgent.Models;

namespace SchedulingAgent.Cards;

public static class FeedbackCard
{
    public static AdaptiveCard Build(SchedulingRequestDocument request)
    {
        return new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Hoe ging het?",
                    Size = AdaptiveTextSize.Medium,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Accent
                },
                new AdaptiveTextBlock
                {
                    Text = "Help ons de planningsassistent te verbeteren door een beoordeling te geven.",
                    Wrap = true,
                    IsSubtle = true,
                    Spacing = AdaptiveSpacing.None
                },
                new AdaptiveTextBlock
                {
                    Text = "Beoordeling",
                    Weight = AdaptiveTextWeight.Bolder,
                    Spacing = AdaptiveSpacing.Medium
                },
                new AdaptiveChoiceSetInput
                {
                    Id = "score",
                    Style = AdaptiveChoiceInputStyle.Expanded,
                    IsRequired = true,
                    Choices =
                    [
                        new AdaptiveChoice { Title = "★★★★★  Uitstekend", Value = "5" },
                        new AdaptiveChoice { Title = "★★★★☆  Goed", Value = "4" },
                        new AdaptiveChoice { Title = "★★★☆☆  Redelijk", Value = "3" },
                        new AdaptiveChoice { Title = "★★☆☆☆  Matig", Value = "2" },
                        new AdaptiveChoice { Title = "★☆☆☆☆  Slecht", Value = "1" }
                    ]
                },
                new AdaptiveTextBlock
                {
                    Text = "Wat kan beter? (optioneel)",
                    Weight = AdaptiveTextWeight.Bolder,
                    Spacing = AdaptiveSpacing.Medium
                },
                new AdaptiveTextInput
                {
                    Id = "improvementSuggestion",
                    Placeholder = "Bijv. 'De tijdsloten sloten niet goed aan bij onze voorkeuren.'",
                    IsMultiline = true,
                    MaxLength = 500
                }
            ],
            Actions =
            [
                new AdaptiveSubmitAction
                {
                    Title = "Verzenden",
                    Style = "positive",
                    Data = new { action = "submitFeedback", requestId = request.RequestId }
                },
                new AdaptiveSubmitAction
                {
                    Title = "Overslaan",
                    Data = new { action = "skipFeedback", requestId = request.RequestId }
                }
            ]
        };
    }
}
