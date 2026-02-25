using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SchedulingAgent.Services;

namespace SchedulingAgent.Functions;

public sealed class ConflictTimeoutFunction(
    IConflictResolutionService conflictService,
    ILogger<ConflictTimeoutFunction> logger)
{
    /// <summary>
    /// Timer function that runs every 15 minutes to check for expired conflict resolutions.
    /// When a conflict resolution request times out (default 4 hours), it marks it as timed out
    /// and triggers a fallback scenario.
    /// </summary>
    [Function("ConflictTimeoutChecker")]
    public async Task RunAsync(
        [TimerTrigger("0 */15 * * * *")] TimerInfo timerInfo,
        CancellationToken ct)
    {
        logger.LogInformation("Conflict timeout checker triggered at {Time}", DateTimeOffset.UtcNow);

        try
        {
            await conflictService.ProcessExpiredConflictsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing expired conflicts");
            throw;
        }

        if (timerInfo.ScheduleStatus is not null)
        {
            logger.LogInformation("Next conflict timeout check at {NextRun}",
                timerInfo.ScheduleStatus.Next);
        }
    }
}
