using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceDetectAnomaliesTool
{
    [McpServerTool(Name = "service_detect_anomalies"),
     Description("Detects deterministic anomalies by comparing the current window with the previous, 24-hour and 7-day baselines using relative change and robust Z-score. Low sample counts return inconclusive.")]
    public static Task<ObservationEnvelope<AnomalyReport>> Execute(
        IServiceIdentityResolver resolver,
        IAnomalyEngine engine,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when ambiguous.")] string? namespaceName = null,
        [Description("Current analysis window in minutes.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_detect_anomalies",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<AnomalyReport>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<AnomalyReport>.Failure(
                        "invalid_window", error!, resolution.Identity);
                var result = await engine.DetectAsync(resolution.Identity!, window, ct);
                return ObservationEnvelope<AnomalyReport>.Success(
                    result.Data, resolution.Identity, window,
                    result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
