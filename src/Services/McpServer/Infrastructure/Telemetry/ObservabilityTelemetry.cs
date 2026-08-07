using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace McpApis.McpServer.Infrastructure.Telemetry;

public static class ObservabilityTelemetry
{
    public const string InstrumentationName = "McpApis.ObservabilityIntelligence";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    public static readonly Meter Meter = new(InstrumentationName);
    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("mcp.observability.tool.calls");
    public static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        "mcp.observability.tool.duration", "ms");
    public static readonly Counter<long> ProviderCalls = Meter.CreateCounter<long>("mcp.observability.provider.calls");
    public static readonly Histogram<double> ProviderDuration = Meter.CreateHistogram<double>(
        "mcp.observability.provider.duration", "ms");
    public static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>("mcp.observability.cache.requests");
    public static readonly Histogram<long> ResponseBytes = Meter.CreateHistogram<long>(
        "mcp.observability.response.size", "By");
    public static readonly Counter<long> Findings = Meter.CreateCounter<long>("mcp.observability.findings");
    public static readonly Counter<long> ProcessedItems = Meter.CreateCounter<long>(
        "mcp.observability.items.processed");
}

public sealed class AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        await next(context);

        if (!context.Request.Path.StartsWithSegments("/api") && context.Request.Method != "POST")
            return;

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.LogInformation(
            "Observability audit caller={Caller} method={Method} path={Path} status={StatusCode} durationMs={DurationMs}",
            context.User.Identity?.Name ?? "anonymous",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            elapsed.TotalMilliseconds);
    }
}
