using FluentAssertions;
using SchedulingAgent.Cards;
using SchedulingAgent.Models;
using Xunit;

namespace SchedulingAgent.Tests.Cards;

public class MeetingOptionsCardTests
{
    [Fact]
    public void Build_WithProposedSlots_ReturnsCardWithOptions()
    {
        // Arrange
        var request = new SchedulingRequestDocument
        {
            Id = "req-1",
            RequestId = "req-1",
            RequesterId = "user-1",
            RequesterName = "Test User",
            ConversationId = "conv-1",
            Intent = new MeetingIntent
            {
                Subject = "Project X bespreking",
                DurationMinutes = 60,
                TimeWindow = new TimeWindow
                {
                    StartDate = DateTimeOffset.UtcNow,
                    EndDate = DateTimeOffset.UtcNow.AddDays(5)
                },
                Participants =
                [
                    new ParticipantReference { Name = "Jan", Type = ParticipantType.User }
                ],
                Priority = MeetingPriority.High
            },
            ResolvedParticipants =
            [
                new ResolvedParticipant
                {
                    UserId = "user-1",
                    DisplayName = "Test User",
                    Email = "test@contoso.com"
                },
                new ResolvedParticipant
                {
                    UserId = "user-jan",
                    DisplayName = "Jan de Vries",
                    Email = "jan@contoso.com"
                }
            ],
            ProposedSlots =
            [
                new ProposedTimeSlot
                {
                    Start = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
                    End = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
                    Confidence = SlotConfidence.Full
                },
                new ProposedTimeSlot
                {
                    Start = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(14),
                    End = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(15),
                    Confidence = SlotConfidence.Conditional,
                    Conflicts =
                    [
                        new SlotConflict
                        {
                            UserId = "user-jan",
                            DisplayName = "Jan de Vries",
                            ConflictingEventSubject = "Standup",
                            ConflictingEventStart = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(14),
                            ConflictingEventEnd = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(14).AddMinutes(30)
                        }
                    ]
                }
            ]
        };

        // Act
        var card = MeetingOptionsCard.Build(request);

        // Assert
        card.Should().NotBeNull();
        card.Body.Should().NotBeEmpty();
        card.Actions.Should().HaveCount(1); // Cancel button
        card.Version.Should().NotBeNull();
    }

    [Fact]
    public void BuildConfirmation_ReturnsConfirmationCard()
    {
        // Arrange
        var request = new SchedulingRequestDocument
        {
            Id = "req-1",
            RequestId = "req-1",
            RequesterId = "user-1",
            RequesterName = "Test User",
            ConversationId = "conv-1",
            Intent = new MeetingIntent
            {
                Subject = "Project X",
                DurationMinutes = 60,
                TimeWindow = new TimeWindow
                {
                    StartDate = DateTimeOffset.UtcNow,
                    EndDate = DateTimeOffset.UtcNow.AddDays(5)
                },
                Participants = [],
                IsOnline = true
            },
            ResolvedParticipants =
            [
                new ResolvedParticipant
                {
                    UserId = "user-1",
                    DisplayName = "Test User",
                    Email = "test@contoso.com"
                }
            ],
            ProposedSlots = []
        };

        var slot = new ProposedTimeSlot
        {
            Start = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
            End = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
            Confidence = SlotConfidence.Full
        };

        // Act
        var card = MeetingOptionsCard.BuildConfirmation(request, slot);

        // Assert
        card.Should().NotBeNull();
        card.Body.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildError_ReturnsErrorCard()
    {
        // Act
        var card = MeetingOptionsCard.BuildError("Er is een fout opgetreden.");

        // Assert
        card.Should().NotBeNull();
        card.Body.Should().HaveCountGreaterThan(0);
    }
}
