using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace McpApis.BuildingBlocks.Observability;

public class RequestBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private const int MaxBodyLength = 1000;

    public RequestBodyTelemetryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        context.Request.EnableBuffering();

        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(body))
        {
            activity.SetTag("http.request.body", Truncate(body));
        }

        await _next(context);
    }

    private static string Truncate(string input) =>
        input.Length > MaxBodyLength ? input[..MaxBodyLength] + "...[truncated]" : input;
}
