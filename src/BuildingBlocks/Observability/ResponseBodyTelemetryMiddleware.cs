using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace McpApis.BuildingBlocks.Observability;

public class ResponseBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private const int MaxBodyLength = 1000;

    public ResponseBodyTelemetryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalBody = context.Response.Body;

        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Position = 0;
        var body = await new StreamReader(buffer).ReadToEndAsync();

        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(body))
        {
            activity.SetTag("http.response.body", Truncate(body));
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }

    private static string Truncate(string input) =>
        input.Length > MaxBodyLength ? input[..MaxBodyLength] + "...[truncated]" : input;
}
