using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Extensions.Logging;

namespace SchedulingAgent.Functions;

public sealed class BotEndpointFunction(
    IBotFrameworkHttpAdapter adapter,
    IBot bot,
    ILogger<BotEndpointFunction> logger)
{
    [Function("BotEndpoint")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "messages")] HttpRequest req,
        CancellationToken ct)
    {
        logger.LogInformation("Bot endpoint received message");

        try
        {
            await adapter.ProcessAsync(req, req.HttpContext.Response, bot, ct);
            return new OkResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing bot message");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
