using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Engines.RootCause;
using McpApis.McpServer.Engines.SystemHealth;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Security;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Api;

/// <summary>
/// Dashboard REST projection over the same normalized providers and engines as
/// the MCP tools. The browser never sends PromQL/LogQL and never reimplements
/// health, anomaly, correlation or RCA rules.
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");
        group.RequireAuthorization(ObservabilityPolicies.Reader)
            .RequireRateLimiting(ObservabilityPolicies.RateLimit);

        group.MapGet("/links", (IConfiguration config) =>
            Results.Ok(GetLinks(config)));

        group.MapGet("/overview", async (
            ISystemHealthEngine systemHealth,
            IKubernetesCollector kubernetes,
            IOptions<ObservabilityLimitsOptions> limits,
            IConfiguration config,
            CancellationToken cancellationToken) =>
        {
            var window = TimeWindow.EndingAt(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(limits.Value.DefaultWindowMinutes));
            var systemTask = systemHealth.SummarizeAsync(window, cancellationToken);
            var deploymentsTask = SafeAsync(
                () => kubernetes.ListDeploymentsAsync(cancellationToken), []);
            var podsTask = SafeAsync(
                () => kubernetes.ListPodsAsync(cancellationToken), []);
            await Task.WhenAll(systemTask, deploymentsTask, podsTask);
            var analysis = await systemTask;
            var deployments = await deploymentsTask;
            var pods = await podsTask;

            return Results.Ok(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                window,
                system = analysis.Data,
                sources = analysis.Sources,
                warnings = analysis.Warnings,
                cluster = new
                {
                    totalPods = pods.Count,
                    readyPods = pods.Count(pod => pod.Ready),
                    totalDeployments = deployments.Count,
                    readyDeployments = deployments.Count(deployment =>
                        deployment.Replicas > 0 &&
                        deployment.ReadyReplicas >= deployment.Replicas)
                },
                links = GetLinks(config)
            });
        });

        group.MapGet("/intelligence/system", async (
            ISystemHealthEngine engine,
            IOptions<ObservabilityLimitsOptions> limits,
            int? minutes,
            CancellationToken cancellationToken) =>
        {
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<SystemHealthSummary>.Failure(
                    "invalid_window", error!));
            var result = await engine.SummarizeAsync(window, cancellationToken);
            return Results.Ok(ObservationEnvelope<SystemHealthSummary>.Success(
                result.Data, null, window, result.Sources, result.Evidence, result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/spec", async (
            string service,
            string? namespaceName,
            IServiceIdentityResolver resolver,
            IApplicationSpecProvider provider,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<ServiceSpecReport>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            var result = await provider.GetSpecAsync(resolution.Identity!, cancellationToken);
            return Results.Ok(ObservationEnvelope<ServiceSpecReport>.Success(
                result.Value ?? EmptySpec(), resolution.Identity, null,
                [result.ToSourceStatus()], warnings: result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/health", async (
            string service,
            string? namespaceName,
            int? minutes,
            IServiceIdentityResolver resolver,
            IHealthAnalysisService engine,
            IOptions<ObservabilityLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<HealthReport>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<HealthReport>.Failure(
                    "invalid_window", error!, resolution.Identity));
            var result = await engine.EvaluateAsync(
                resolution.Identity!, resolution.Application!.Selector, window, cancellationToken);
            return Results.Ok(ObservationEnvelope<HealthReport>.Success(
                result.Data, resolution.Identity, window,
                result.Sources, result.Evidence, result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/dependencies", async (
            string service,
            string? namespaceName,
            int? minutes,
            int depth,
            IServiceIdentityResolver resolver,
            IDependencyEngine engine,
            IOptions<ObservabilityLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<DependencyGraph>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<DependencyGraph>.Failure(
                    "invalid_window", error!, resolution.Identity));
            if (depth < 1 || depth > limits.Value.MaxGraphDepth)
                return Results.BadRequest(ObservationEnvelope<DependencyGraph>.Failure(
                    "invalid_depth", $"Depth must be between 1 and {limits.Value.MaxGraphDepth}.",
                    resolution.Identity, window));
            var result = await engine.AnalyzeAsync(
                resolution.Identity!, window, depth, cancellationToken);
            return Results.Ok(ObservationEnvelope<DependencyGraph>.Success(
                result.Data, resolution.Identity, window,
                result.Sources, result.Evidence, result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/anomalies", async (
            string service,
            string? namespaceName,
            int? minutes,
            IServiceIdentityResolver resolver,
            IAnomalyEngine engine,
            IOptions<ObservabilityLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<AnomalyReport>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<AnomalyReport>.Failure(
                    "invalid_window", error!, resolution.Identity));
            var result = await engine.DetectAsync(
                resolution.Identity!, window, cancellationToken);
            return Results.Ok(ObservationEnvelope<AnomalyReport>.Success(
                result.Data, resolution.Identity, window,
                result.Sources, result.Evidence, result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/timeline", async (
            string service,
            string? namespaceName,
            int? minutes,
            IServiceIdentityResolver resolver,
            ICorrelationEngine engine,
            IOptions<ObservabilityLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<IncidentTimeline>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<IncidentTimeline>.Failure(
                    "invalid_window", error!, resolution.Identity));
            var result = await engine.BuildTimelineAsync(
                resolution.Identity!, resolution.Application!.Selector, window, cancellationToken);
            return Results.Ok(ObservationEnvelope<IncidentTimeline>.Success(
                result.Data, resolution.Identity, window,
                result.Sources, result.Evidence, result.Warnings));
        });

        group.MapGet("/intelligence/services/{service}/root-cause", async (
            string service,
            string? namespaceName,
            int? minutes,
            int depth,
            IServiceIdentityResolver resolver,
            IRootCauseEngine engine,
            IOptions<ObservabilityLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            var failure = ResolveOrFailure<RootCauseReport>(
                resolver, service, namespaceName, out var resolution);
            if (failure is not null) return failure;
            if (!TryWindow(minutes, limits.Value, out var window, out var error))
                return Results.BadRequest(ObservationEnvelope<RootCauseReport>.Failure(
                    "invalid_window", error!, resolution.Identity));
            if (depth < 1 || depth > limits.Value.MaxGraphDepth)
                return Results.BadRequest(ObservationEnvelope<RootCauseReport>.Failure(
                    "invalid_depth", $"Depth must be between 1 and {limits.Value.MaxGraphDepth}.",
                    resolution.Identity, window));
            var result = await engine.AnalyzeAsync(
                resolution.Identity!, resolution.Application!.Selector,
                window, depth, cancellationToken);
            return Results.Ok(ObservationEnvelope<RootCauseReport>.Success(
                result.Data, resolution.Identity, window,
                result.Sources, result.Evidence, result.Warnings));
        });

        MapRawAdminEndpoints(app, group);
        return app;
    }

    private static void MapRawAdminEndpoints(
        IEndpointRouteBuilder app,
        RouteGroupBuilder group)
    {
        var enabled = app.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetValue<bool>("Observability:Features:EnableRawQueries");
        if (!enabled) return;

        group.MapGet("/admin/metrics", async (
            IPrometheusCollector prometheus,
            string query,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await prometheus.QueryAsync(query, cancellationToken));
            }
            catch
            {
                return Results.Problem(
                    "Prometheus administrative query failed.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization(ObservabilityPolicies.Admin);

        group.MapGet("/admin/metrics/range", async (
            IPrometheusCollector prometheus,
            IOptions<ObservabilityLimitsOptions> limits,
            string query,
            int minutes,
            string? step,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var bounded = Math.Clamp(minutes, 1, limits.Value.MaxWindowMinutes);
                var end = DateTimeOffset.UtcNow;
                return Results.Ok(await prometheus.QueryRangeAsync(
                    query, end.AddMinutes(-bounded), end, step ?? "15s", cancellationToken));
            }
            catch
            {
                return Results.Problem(
                    "Prometheus administrative range query failed.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization(ObservabilityPolicies.Admin);
    }

    private static IResult? ResolveOrFailure<T>(
        IServiceIdentityResolver resolver,
        string service,
        string? namespaceName,
        out ServiceResolution resolution)
    {
        resolution = resolver.Resolve(service, namespaceName);
        if (resolution.IsResolved) return null;
        return Results.BadRequest(ObservationEnvelope<T>.Failure(
            resolution.Status switch
            {
                ServiceResolutionStatus.Ambiguous => "ambiguous_service",
                ServiceResolutionStatus.Disabled => "service_disabled",
                ServiceResolutionStatus.NamespaceNotAllowed => "namespace_not_allowed",
                _ => "unknown_service"
            },
            resolution.Message ?? "Service could not be resolved.",
            candidates: resolution.Candidates));
    }

    private static bool TryWindow(
        int? minutes,
        ObservabilityLimitsOptions limits,
        out TimeWindow window,
        out string? error)
    {
        var duration = minutes ?? limits.DefaultWindowMinutes;
        if (duration < 1 || duration > limits.MaxWindowMinutes)
        {
            window = null!;
            error = $"Window must be between 1 and {limits.MaxWindowMinutes} minutes.";
            return false;
        }
        window = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(duration));
        error = null;
        return true;
    }

    private static ServiceSpecReport EmptySpec() => new(
        null, null, null, null, null, null, null, 0, 0,
        new Dictionary<string, string>(), new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new SignalCoverage(
            SourceAvailability.Unavailable, SourceAvailability.Unavailable,
            SourceAvailability.Unavailable, SourceAvailability.Unavailable,
            SourceAvailability.Unavailable, SourceAvailability.Unavailable),
        [], []);

    private static async Task<T> SafeAsync<T>(Func<Task<T>> operation, T fallback)
    {
        try { return await operation(); }
        catch { return fallback; }
    }

    private static Dictionary<string, string> GetLinks(IConfiguration config) =>
        config.GetSection("Dashboard:Links").Get<Dictionary<string, string>>()
        ?? new Dictionary<string, string>();
}
