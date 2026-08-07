using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Tools.VNext;

internal static class VNextToolSupport
{
    public static ObservationEnvelope<T>? ResolveOrError<T>(
        IServiceIdentityResolver resolver,
        string serviceName,
        string? namespaceName,
        out ServiceResolution resolution)
    {
        resolution = resolver.Resolve(serviceName, namespaceName);
        if (resolution.IsResolved)
            return null;

        var (code, recovery) = resolution.Status switch
        {
            ServiceResolutionStatus.Ambiguous =>
                ("ambiguous_service", "Supply the namespace parameter using one of the candidates."),
            ServiceResolutionStatus.Disabled =>
                ("service_disabled", "An administrator can enable indexing for this service."),
            ServiceResolutionStatus.NamespaceNotAllowed =>
                ("namespace_not_allowed", "Choose a service from an authorized namespace."),
            ServiceResolutionStatus.NamespaceRequired =>
                ("namespace_required", "Correlate the service with a Kubernetes namespace before querying it."),
            _ => ("unknown_service", "Use list_discovered_applications to inspect available services.")
        };
        return ObservationEnvelope<T>.Failure(
            code,
            resolution.Message ?? "Service could not be resolved.",
            candidates: resolution.Candidates,
            recovery: recovery);
    }

    public static bool TryCreateWindow(
        int? minutes,
        ObservabilityLimitsOptions limits,
        out TimeWindow window,
        out string? error)
    {
        var duration = minutes ?? limits.DefaultWindowMinutes;
        if (duration <= 0 || duration > limits.MaxWindowMinutes)
        {
            window = null!;
            error = $"Window must be between 1 and {limits.MaxWindowMinutes} minutes.";
            return false;
        }

        window = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(duration));
        error = null;
        return true;
    }

    public static async Task<ObservationEnvelope<T>> ExecuteAsync<T>(
        string tool,
        ObservabilityLimitsOptions limits,
        Func<CancellationToken, Task<ObservationEnvelope<T>>> operation,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity($"tool.{tool}");
        activity?.SetTag("tool", tool);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(limits.ToolTimeoutSeconds));

        ObservationEnvelope<T> result;
        try
        {
            result = await operation(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = ObservationEnvelope<T>.Failure(
                "tool_timeout",
                $"Tool execution exceeded {limits.ToolTimeoutSeconds} seconds.",
                recovery: "Reduce the requested window or retry after backend recovery.");
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var status = result.ExecutionStatus.ToString().ToLowerInvariant();
        activity?.SetTag("execution.status", status);
        var tags = new[]
        {
            new KeyValuePair<string, object?>("tool", tool),
            new KeyValuePair<string, object?>("execution.status", status)
        };
        ObservabilityTelemetry.ToolCalls.Add(1, tags);
        ObservabilityTelemetry.ToolDuration.Record(elapsed.TotalMilliseconds, tags);
        var size = JsonSerializer.SerializeToUtf8Bytes(result).LongLength;
        ObservabilityTelemetry.ResponseBytes.Record(size, tags);
        if (size > limits.MaxResponseBytes)
        {
            return ObservationEnvelope<T>.Failure(
                "response_too_large",
                $"Response exceeded the {limits.MaxResponseBytes}-byte limit.",
                result.Service,
                result.Window,
                recovery: "Reduce the window, graph depth, or item limit.");
        }
        return result;
    }
}
