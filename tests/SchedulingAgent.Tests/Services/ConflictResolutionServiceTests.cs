using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SchedulingAgent.Models;
using SchedulingAgent.Services;
using Xunit;

namespace SchedulingAgent.Tests.Services;

public class ConflictResolutionServiceTests
{
    private readonly Mock<IOpenAIService> _openAIService = new();
    private readonly Mock<IGraphService> _graphService = new();
    private readonly Mock<ICosmosDbService> _cosmosDbService = new();
    private readonly Mock<ILogger<ConflictResolutionService>> _logger = new();
    private readonly IOptions<ConflictResolutionOptions> _options;
    private readonly ConflictResolutionService _sut;

    public ConflictResolutionServiceTests()
    {
        _options = Options.Create(new ConflictResolutionOptions
        {
            TimeoutHours = 4,
            MaxRetries = 3
        });

        _sut = new ConflictResolutionService(
            _openAIService.Object,
            _graphService.Object,
            _cosmosDbService.Object,
            _options,
            _logger.Object);
    }

    [Fact]
    public async Task ResolveConflictsAsync_NoConflicts_ReturnsSlotsUnchanged()
    {
        // Arrange
        var request = CreateTestRequest();
        var slots = new List<ProposedTimeSlot>
        {
            new()
            {
                Start = DateTimeOffset.UtcNow.AddDays(1),
                End = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
                Confidence = SlotConfidence.Full,
                Conflicts = []
            }
        };

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ResolveConflictsAsync(request, slots);

        // Assert
        result.Should().HaveCount(1);
        result[0].Confidence.Should().Be(SlotConfidence.Full);
        _openAIService.Verify(
            s => s.AnalyzeConflictAsync(It.IsAny<SlotConflict>(), It.IsAny<MeetingPriority>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveConflictsAsync_WithConflict_AskParticipant_InitiatesNegotiation()
    {
        // Arrange
        var request = CreateTestRequest();
        var conflict = new SlotConflict
        {
            UserId = "user-jan",
            DisplayName = "Jan",
            ConflictingEventSubject = "Wekelijks overleg",
            ConflictingEventStart = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
            ConflictingEventEnd = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
            IsRecurring = true
        };

        var slots = new List<ProposedTimeSlot>
        {
            new()
            {
                Start = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
                End = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
                Confidence = SlotConfidence.Conditional,
                Conflicts = [conflict]
            }
        };

        _openAIService.Setup(s => s.AnalyzeConflictAsync(conflict, MeetingPriority.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictAnalysis
            {
                CanAutoResolve = false,
                Strategy = ConflictStrategy.AskParticipant,
                Reasoning = "Terugkerend overleg, vraag de deelnemer",
                BlockedByUserId = "user-jan"
            });

        _graphService.Setup(s => s.CreateChatAsync("user-jan", It.IsAny<CancellationToken>()))
            .ReturnsAsync("chat-123");

        _graphService.Setup(s => s.SendChatMessageAsync("chat-123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _cosmosDbService.Setup(s => s.CreateConflictStateAsync(It.IsAny<ConflictResolutionStateDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConflictResolutionStateDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ResolveConflictsAsync(request, slots);

        // Assert
        _graphService.Verify(s => s.CreateChatAsync("user-jan", It.IsAny<CancellationToken>()), Times.Once);
        _graphService.Verify(s => s.SendChatMessageAsync("chat-123", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _cosmosDbService.Verify(s => s.CreateConflictStateAsync(
            It.Is<ConflictResolutionStateDocument>(d =>
                d.ConflictUserId == "user-jan" && d.PendingResponse),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveConflictsAsync_WithConflict_SuggestAlternative_AddsNewSlot()
    {
        // Arrange
        var request = CreateTestRequest();
        var alternativeStart = DateTimeOffset.UtcNow.AddDays(2).Date.AddHours(14);
        var alternativeEnd = alternativeStart.AddHours(1);

        var conflict = new SlotConflict
        {
            UserId = "user-jan",
            DisplayName = "Jan",
            ConflictingEventSubject = "Focus Time",
            ConflictingEventStart = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
            ConflictingEventEnd = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11)
        };

        var slots = new List<ProposedTimeSlot>
        {
            new()
            {
                Start = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(10),
                End = DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(11),
                Confidence = SlotConfidence.Conditional,
                Conflicts = [conflict]
            }
        };

        _openAIService.Setup(s => s.AnalyzeConflictAsync(conflict, MeetingPriority.Normal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictAnalysis
            {
                CanAutoResolve = true,
                Strategy = ConflictStrategy.SuggestAlternativeSlot,
                Reasoning = "Focus Time wordt gerespecteerd",
                SuggestedAlternativeSlot = new AlternativeSlot
                {
                    Start = alternativeStart,
                    End = alternativeEnd
                }
            });

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ResolveConflictsAsync(request, slots);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.Start == alternativeStart && s.Confidence == SlotConfidence.Conditional);
    }

    [Fact]
    public async Task ProcessExpiredConflictsAsync_MarksExpiredAsTimedOut()
    {
        // Arrange
        var expiredState = new ConflictResolutionStateDocument
        {
            Id = "state-1",
            RequestId = "req-1",
            ConflictUserId = "user-1",
            ConflictUserName = "Test User",
            OriginalEventSubject = "Meeting",
            OriginalEventStart = DateTimeOffset.UtcNow.AddDays(-1),
            OriginalEventEnd = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1),
            ProposedNewStart = DateTimeOffset.UtcNow.AddDays(-1).AddHours(2),
            ProposedNewEnd = DateTimeOffset.UtcNow.AddDays(-1).AddHours(3),
            PendingResponse = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(-1)
        };

        _cosmosDbService.Setup(s => s.GetExpiredConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([expiredState]);

        _cosmosDbService.Setup(s => s.UpdateConflictStateAsync(It.IsAny<ConflictResolutionStateDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConflictResolutionStateDocument doc, CancellationToken _) => doc);

        _cosmosDbService.Setup(s => s.CreateAuditLogAsync(It.IsAny<AuditLogDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ProcessExpiredConflictsAsync();

        // Assert
        _cosmosDbService.Verify(s => s.UpdateConflictStateAsync(
            It.Is<ConflictResolutionStateDocument>(d =>
                d.PendingResponse == false && d.ResponseReceived == ConflictResponse.TimedOut),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SchedulingRequestDocument CreateTestRequest() => new()
    {
        Id = "req-test",
        RequestId = "req-test",
        RequesterId = "user-requester",
        RequesterName = "Test Requester",
        ConversationId = "conv-test",
        Intent = new MeetingIntent
        {
            Subject = "Test Meeting",
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
            Priority = MeetingPriority.Normal
        }
    };
}
