using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Tools;

namespace McpApis.McpServer.Api;

/// <summary>
/// Dashboard endpoints for the auto-discovered application inventory and the
/// per-application MCP indexing toggle. Unlike the MCP tools, these endpoints
/// never hide disabled applications — the dashboard is the administration UI.
/// </summary>
public static class ApplicationsEndpoints
{
    public record IndexingRequest(bool Enabled);

    public static IEndpointRouteBuilder MapApplicationsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/applications", async (
            IApplicationCatalog catalog,
            IDiscoveryOrchestrator orchestrator,
            IKubernetesCollector k8s,
            IConfiguration config) =>
        {
            var rescanSeconds = int.TryParse(config["Discovery:RescanSeconds"], out var v) ? v : 60;
            var missingAfter = TimeSpan.FromSeconds(Math.Max(rescanSeconds, 30) * 2);
            var now = DateTimeOffset.UtcNow;

            var applications = new List<object>();
            foreach (var a in catalog.GetAll())
            {
                HealthStatus? health = null;
                var appLabel = a.DeploymentName ?? a.KubernetesServiceName;
                if (appLabel is not null)
                {
                    try
                    {
                        health = await k8s.GetHealthAsync(appLabel, a.Namespace);
                    }
                    catch
                    {
                        // Health is best-effort; the dashboard shows "unknown" when it fails.
                    }
                }

                applications.Add(new
                {
                    name = a.Name,
                    @namespace = a.Namespace,
                    sources = ListDiscoveredApplicationsTool.DescribeSources(a.Sources),
                    deploymentName = a.DeploymentName,
                    kubernetesServiceName = a.KubernetesServiceName,
                    otelServiceName = a.OtelServiceName,
                    baseUrl = a.BaseUrl,
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
                applications
            });
        });

        group.MapPut("/applications/{name}/indexing", async (
            string name,
            IndexingRequest request,
            IApplicationCatalog catalog,
            IIndexingStateStore stateStore,
            IDiscoveryOrchestrator orchestrator) =>
        {
            if (!catalog.TryGet(name, out var app))
                return Results.NotFound(new { error = $"Unknown application: {name}" });

            if (app.LockedDisabled)
                return Results.Conflict(new
                {
                    error = $"Application '{app.Name}' is locked by the label mcp-apis/indexed=false on its Service.",
                    hint = "Remove the label (or set it to true) and wait for the next discovery scan to unlock the toggle."
                });

            var persisted = await stateStore.SaveAsync(app.Name, request.Enabled);
            catalog.SetEnabled(app.Name, request.Enabled);

            // A freshly enabled app that never passed validation gets probed right away.
            if (request.Enabled && !app.OpenApi.Validated && app.BaseUrl is not null)
                orchestrator.RequestRescan();

            return Results.Ok(new { name = app.Name, enabled = request.Enabled, persisted });
        });

        group.MapPost("/discovery/rescan", (IDiscoveryOrchestrator orchestrator) =>
        {
            orchestrator.RequestRescan();
            return Results.Accepted(value: new { status = "scan-requested" });
        });

        return app;
    }
}
