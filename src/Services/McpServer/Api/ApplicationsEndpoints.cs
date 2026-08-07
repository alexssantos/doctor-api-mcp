using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Tools;
using McpApis.McpServer.Infrastructure.Security;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Engines.SystemHealth;
using McpApis.McpServer.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace McpApis.McpServer.Api;

/// <summary>
/// Dashboard endpoints for the auto-discovered application inventory and the
/// per-application MCP indexing toggle. Unlike the MCP tools, these endpoints
/// never hide disabled applications — the dashboard is the administration UI.
/// </summary>
public static class ApplicationsEndpoints
{
    public record IndexingRequest(bool Enabled, string? Namespace = null);

    public static IEndpointRouteBuilder MapApplicationsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");
        group.RequireRateLimiting(ObservabilityPolicies.RateLimit);

        group.MapGet("/applications", async (
            IApplicationCatalog catalog,
            IDiscoveryOrchestrator orchestrator,
            ISystemHealthEngine systemHealth,
            IOptions<ObservabilityLimitsOptions> limits,
            IConfiguration config,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var rescanSeconds = int.TryParse(config["Discovery:RescanSeconds"], out var v) ? v : 60;
            var missingAfter = TimeSpan.FromSeconds(Math.Max(rescanSeconds, 30) * 2);
            var now = DateTimeOffset.UtcNow;
            var window = TimeWindow.EndingAt(
                now, TimeSpan.FromMinutes(limits.Value.DefaultWindowMinutes));
            var system = await systemHealth.SummarizeAsync(window, cancellationToken);
            var healthByKey = system.Data.Services.ToDictionary(
                summary => summary.Service.Key,
                StringComparer.OrdinalIgnoreCase);

            var applications = new List<object>();
            foreach (var a in catalog.GetAll())
            {
                healthByKey.TryGetValue($"{a.Namespace}/{a.Name}", out var health);

                applications.Add(new
                {
                    name = a.Name,
                    @namespace = a.Namespace,
                    sources = ListDiscoveredApplicationsTool.DescribeSources(a.Sources),
                    deploymentName = a.DeploymentName,
                    kubernetesServiceName = a.KubernetesServiceName,
                    otelServiceName = a.OtelServiceName,
                    baseUrl = a.BaseUrl,
                    selector = a.Selector,
                    image = a.Image,
                    imageDigest = a.ImageDigest,
                    version = a.Version,
                    revision = a.Revision,
                    desiredReplicas = a.DesiredReplicas,
                    readyReplicas = a.ReadyReplicas,
                    owner = a.Owner,
                    team = a.Team,
                    description = a.Description,
                    coverage = a.Coverage,
                    declaredDependencies = a.DeclaredDependencies,
                    hasReadyEndpoints = a.HasReadyEndpoints,
                    openApi = new
                    {
                        validated = a.OpenApi.Validated,
                        path = a.OpenApi.Path,
                        failures = a.OpenApi.Failures
                    },
                    enabled = a.Enabled,
                    lockedDisabled = a.LockedDisabled,
                    firstSeen = a.FirstSeen,
                    lastSeen = a.LastSeen,
                    missing = now - a.LastSeen > missingAfter,
                    health
                });
            }

            return Results.Ok(new
            {
                generatedAt = now,
                lastScanAt = orchestrator.LastScanCompletedAt,
                healthWindow = window,
                sources = system.Sources,
                warnings = system.Warnings,
                canManage = httpContext.User.IsInRole(ObservabilityPolicies.Admin),
                applications
            });
        }).RequireAuthorization(ObservabilityPolicies.Reader);

        group.MapPut("/applications/{name}/indexing", async (
            string name,
            IndexingRequest request,
            IApplicationCatalog catalog,
            IIndexingStateStore stateStore,
            IDiscoveryOrchestrator orchestrator) =>
        {
            var resolution = catalog.Resolve(name, request.Namespace);
            if (resolution.Status == CatalogResolutionStatus.Unknown)
                return Results.NotFound(new { error = $"Unknown application: {name}" });
            if (resolution.Status == CatalogResolutionStatus.Ambiguous)
                return Results.Conflict(new
                {
                    code = "ambiguous_service",
                    error = $"Application '{name}' exists in multiple namespaces.",
                    candidates = resolution.Candidates.Select(a => $"{a.Namespace}/{a.Name}")
                });
            var app = resolution.Application!;

            if (app.LockedDisabled)
                return Results.Conflict(new
                {
                    error = $"Application '{app.Name}' is locked by the label mcp-apis/indexed=false on its Service.",
                    hint = "Remove the label (or set it to true) and wait for the next discovery scan to unlock the toggle."
                });

            var stateKey = $"{app.Namespace ?? "~"}/{app.Name}";
            var persisted = await stateStore.SaveAsync(stateKey, request.Enabled);
            catalog.SetEnabled(app.Name, request.Enabled, app.Namespace);

            // A freshly enabled app that never passed validation gets probed right away.
            if (request.Enabled && !app.OpenApi.Validated && app.BaseUrl is not null)
                orchestrator.RequestRescan();

            return Results.Ok(new
            {
                name = app.Name,
                @namespace = app.Namespace,
                enabled = request.Enabled,
                persisted
            });
        }).RequireAuthorization(ObservabilityPolicies.Admin);

        group.MapPost("/discovery/rescan", (IDiscoveryOrchestrator orchestrator) =>
        {
            orchestrator.RequestRescan();
            return Results.Accepted(value: new { status = "scan-requested" });
        }).RequireAuthorization(ObservabilityPolicies.Admin);

        return app;
    }
}
