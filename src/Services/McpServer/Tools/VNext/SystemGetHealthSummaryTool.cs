using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.SystemHealth;
using McpApis.McpServer.Infrastructure.Options;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class SystemGetHealthSummaryTool
{
    [McpServerTool(Name = "system_get_health_summary"),
     Description("Summarizes every enabled and authorized service using the same cached Health Engine reports as service_get_health. Critical and unknown services are surfaced first with explicit aggregate coverage.")]
    public static Task<ObservationEnvelope<SystemHealthSummary>> Execute(
        ISystemHealthEngine engine,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Health analysis window in minutes.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "system_get_health_summary",
            limits.Value,
            async ct =>
            {
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<SystemHealthSummary>.Failure(
                        "invalid_window", error!, recovery:
                        $"Use 1..{limits.Value.MaxWindowMinutes} minutes.");
                var result = await engine.SummarizeAsync(window, ct);
                return ObservationEnvelope<SystemHealthSummary>.Success(
                    result.Data, null, window,
                    result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
