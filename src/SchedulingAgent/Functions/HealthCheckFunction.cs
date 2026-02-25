using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace SchedulingAgent.Functions;

public sealed class HealthCheckFunction
{
    [Function("HealthCheck")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        return new OkObjectResult(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version = typeof(HealthCheckFunction).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        });
    }
}
