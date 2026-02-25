using AdaptiveCards;
using SchedulingAgent.Models;

namespace SchedulingAgent.Cards;

public static class MeetingOptionsCard
{
    public static AdaptiveCard Build(SchedulingRequestDocument request)
    {
        var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Vergadervoorstel",
                    Size = AdaptiveTextSize.Large,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Accent
                },
                new AdaptiveFactSet
                {
                    Facts =
                    [
                        new AdaptiveFact("Onderwerp", request.Intent.Subject),
                        new AdaptiveFact("Duur", $"{request.Intent.DurationMinutes} minuten"),
                        new AdaptiveFact("Deelnemers", string.Join(", ",
                            request.ResolvedParticipants.Select(p => p.DisplayName))),
                        new AdaptiveFact("Prioriteit", request.Intent.Priority.ToString())
                    ]
                },
                new AdaptiveTextBlock
                {
                    Text = "Kies een tijdslot:",
                    Size = AdaptiveTextSize.Medium,
                    Weight = AdaptiveTextWeight.Bolder,
                    Spacing = AdaptiveSpacing.Large
                }
            ]
        };

        for (var i = 0; i < request.ProposedSlots.Count; i++)
        {
            var slot = request.ProposedSlots[i];
            card.Body.Add(BuildSlotContainer(slot, i));
        }

        card.Actions.Add(new AdaptiveSubmitAction
        {
            Title = "Annuleren",
            Data = new { action = "cancel", requestId = request.RequestId },
            Style = "destructive"
        });

        return card;
    }

    private static AdaptiveContainer BuildSlotContainer(ProposedTimeSlot slot, int index)
    {
        var confidenceEmoji = slot.Confidence switch
        {
            SlotConfidence.Full => "[Beschikbaar]",
            SlotConfidence.Conditional => "[Voorwaardelijk]",
            _ => "[Beperkt]"
        };

        var confidenceColor = slot.Confidence switch
        {
            SlotConfidence.Full => AdaptiveTextColor.Good,
            SlotConfidence.Conditional => AdaptiveTextColor.Warning,
            _ => AdaptiveTextColor.Attention
        };

        var container = new AdaptiveContainer
        {
            Style = AdaptiveContainerStyle.Emphasis,
            Spacing = AdaptiveSpacing.Medium,
            Items =
            [
                new AdaptiveColumnSet
                {
                    Columns =
                    [
                        new AdaptiveColumn
                        {
                            Width = "stretch",
                            Items =
                            [
                                new AdaptiveTextBlock
                                {
                                    Text = $"Optie {index + 1}: {slot.Start:dddd d MMMM yyyy}",
                                    Weight = AdaptiveTextWeight.Bolder,
                                    Size = AdaptiveTextSize.Medium
                                },
                                new AdaptiveTextBlock
                                {
                                    Text = $"{slot.Start:HH:mm} - {slot.End:HH:mm}",
                                    Spacing = AdaptiveSpacing.None
                                }
                            ]
                        },
                        new AdaptiveColumn
                        {
                            Width = "auto",
                            Items =
                            [
                                new AdaptiveTextBlock
                                {
                                    Text = confidenceEmoji,
                                    Color = confidenceColor,
                                    Weight = AdaptiveTextWeight.Bolder,
                                    HorizontalAlignment = AdaptiveHorizontalAlignment.Right
                                }
                            ]
                        }
                    ]
                }
            ],
            SelectAction = new AdaptiveSubmitAction
            {
                Data = new { action = "selectSlot", requestId = "", slotIndex = index }
            }
        };

        if (slot.Conflicts.Count > 0)
        {
            container.Items.Add(new AdaptiveTextBlock
            {
                Text = $"Conflicten: {string.Join(", ", slot.Conflicts.Select(c => c.DisplayName))}",
                Size = AdaptiveTextSize.Small,
                Color = AdaptiveTextColor.Warning,
                IsSubtle = true
            });
        }

        return container;
    }

    public static AdaptiveCard BuildConfirmation(SchedulingRequestDocument request, ProposedTimeSlot selectedSlot)
    {
        return new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Vergadering gepland!",
                    Size = AdaptiveTextSize.Large,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Good
                },
                new AdaptiveFactSet
                {
                    Facts =
                    [
                        new AdaptiveFact("Onderwerp", request.Intent.Subject),
                        new AdaptiveFact("Datum", selectedSlot.Start.ToString("dddd d MMMM yyyy")),
                        new AdaptiveFact("Tijd", $"{selectedSlot.Start:HH:mm} - {selectedSlot.End:HH:mm}"),
                        new AdaptiveFact("Deelnemers", string.Join(", ",
                            request.ResolvedParticipants.Select(p => p.DisplayName))),
                        new AdaptiveFact("Online meeting",
                            request.Intent.IsOnline ? "Ja (Teams)" : "Nee")
                    ]
                },
                new AdaptiveTextBlock
                {
                    Text = "De uitnodigingen zijn verzonden naar alle deelnemers.",
                    Spacing = AdaptiveSpacing.Medium,
                    IsSubtle = true
                }
            ]
        };
    }

    public static AdaptiveCard BuildError(string message)
    {
        return new AdaptiveCard(new AdaptiveSchemaVersion(1, 5))
        {
            Body =
            [
                new AdaptiveTextBlock
                {
                    Text = "Er is een fout opgetreden",
                    Size = AdaptiveTextSize.Large,
                    Weight = AdaptiveTextWeight.Bolder,
                    Color = AdaptiveTextColor.Attention
                },
                new AdaptiveTextBlock
                {
                    Text = message,
                    Wrap = true
                }
            ]
        };
    }
}
