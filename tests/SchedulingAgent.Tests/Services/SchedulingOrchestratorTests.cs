using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Services;

public class SchedulingOrchestratorTests
{
    private readonly Mock<IOpenAIService> _openAIService = new();
    private readonly Mock<IGraphService> _graphService = new();
    private readonly Mock<ICosmosDbService> _cosmosDbService = new();
    private readonly Mock<IConflictResolutionService> _conflictService = new();
    private readonly Mock<ILogger<SchedulingOrchestrator>> _logger = new();
    private readonly SchedulingOrchestrator _sut;

    public SchedulingOrchestratorTests()
    {
        _sut = new SchedulingOrchestrator(
            _openAIService.Object,
            _graphService.Object,
            _cosmosDbService.Object,
            _conflictService.Object,
            _logger.Object);
    }

    [Fact]
    public async Task ProcessSchedulingRequestAsync_ValidRequest_ReturnsProposedSlots()
    {
        // Arrange
        var intent = new MeetingIntent
        {
            Subject = "Project X bespreking",
            DurationMinutes = 60,
            TimeWindow = new TimeWindow
            {
                StartDate = DateTimeOffset.UtcNow.AddDays(1),
                EndDate = DateTimeOffset.UtcNow.AddDays(5)
            },
            Participants =
            [
                new ParticipantReference { Name = "Jan", Type = ParticipantType.User, IsRequired = true }
            ]
        };

        _openAIService.Setup(s => s.ExtractMeetingIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intent);

        _graphService.Setup(s => s.ResolveUserAsync("Jan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedParticipant
            {
                UserId = "user-jan",
                DisplayName = "Jan de Vries",
                Email = "jan@contoso.com"
            });

        var proposedSlots = new List<ProposedTimeSlot>
        {
            new()
            {
                Start = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(10),
                End = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(11),
                Confidence = SlotConfidence.Full,
                AvailabilityScore = 1.0
            }
        };

        _graphService.Setup(s => s.FindMeetingTimesAsync(
                It.IsAny<List<ResolvedParticipant>>(),
                It.IsAny<TimeWindow>(),
                60,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposedSlots);

        _cosmosDbService.Setup(s => s.CreateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.UpdateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProcessSchedulingRequestAsync(
            "user-requester", "Requester", "conv-123",
            "Plan een overleg van 1 uur met Jan voor volgende week over Project X");

        // Assert
        result.Status.Should().Be(SchedulingStatus.PendingUserSelection);
        result.ProposedSlots.Should().HaveCount(1);
        result.ResolvedParticipants.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ProcessSchedulingRequestAsync_NoParticipantsResolved_ReturnsFailed()
    {
        // Arrange
        var intent = new MeetingIntent
        {
            Subject = "Test Meeting",
            DurationMinutes = 30,
            TimeWindow = new TimeWindow
            {
                StartDate = DateTimeOffset.UtcNow.AddDays(1),
                EndDate = DateTimeOffset.UtcNow.AddDays(5)
            },
            Participants =
            [
                new ParticipantReference { Name = "NietBestaand", Type = ParticipantType.User }
            ]
        };

        _openAIService.Setup(s => s.ExtractMeetingIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intent);

        _graphService.Setup(s => s.ResolveUserAsync("NietBestaand", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedParticipant?)null);

        _cosmosDbService.Setup(s => s.CreateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.UpdateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProcessSchedulingRequestAsync(
            "user-requester", "Requester", "conv-123",
            "Plan een meeting met NietBestaand");

        // Assert
        result.Status.Should().Be(SchedulingStatus.Failed);
    }

    [Fact]
    public async Task HandleSlotSelectionAsync_ValidSelection_BooksMeeting()
    {
        // Arrange
        var requestId = "req-123";
        var request = new SchedulingRequestDocument
        {
            Id = requestId,
            RequestId = requestId,
            RequesterId = "user-1",
            RequesterName = "Test User",
            ConversationId = "conv-1",
            Intent = new MeetingIntent
            {
                Subject = "Test Meeting",
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
            ProposedSlots =
            [
                new ProposedTimeSlot
                {
                    Start = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
                    End = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
                    Confidence = SlotConfidence.Full
                }
            ],
            Status = SchedulingStatus.PendingUserSelection
        };

        _cosmosDbService.Setup(s => s.GetRequestAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        _cosmosDbService.Setup(s => s.UpdateRequestAsync(It.IsAny<SchedulingRequestDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SchedulingRequestDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _graphService.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<List<ResolvedParticipant>>(), true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-456");

        // Act
        var result = await _sut.HandleSlotSelectionAsync(requestId, 0);

        // Assert
        result.Status.Should().Be(SchedulingStatus.Completed);
        result.CreatedEventId.Should().Be("event-456");
        result.SelectedSlotIndex.Should().Be(0);
    }

    [Fact]
    public async Task HandleSlotSelectionAsync_InvalidSlotIndex_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var requestId = "req-123";
        var request = new SchedulingRequestDocument
        {
            Id = requestId,
            RequestId = requestId,
            RequesterId = "user-1",
            RequesterName = "Test User",
            ConversationId = "conv-1",
            Intent = new MeetingIntent
            {
                Subject = "Test",
                DurationMinutes = 30,
                TimeWindow = new TimeWindow
                {
                    StartDate = DateTimeOffset.UtcNow,
                    EndDate = DateTimeOffset.UtcNow.AddDays(1)
                },
                Participants = []
            },
            ProposedSlots =
            [
                new ProposedTimeSlot
                {
                    Start = DateTimeOffset.UtcNow,
                    End = DateTimeOffset.UtcNow.AddHours(1),
                    Confidence = SlotConfidence.Full
                }
            ],
            Status = SchedulingStatus.PendingUserSelection
        };

        _cosmosDbService.Setup(s => s.GetRequestAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        // Act & Assert
        await _sut.Invoking(s => s.HandleSlotSelectionAsync(requestId, 5))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
