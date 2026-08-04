using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Api;

/// <summary>
/// Minimal API endpoints backing the React dashboard (served from wwwroot/dashboard).
/// Aggregates data from the service registry, Kubernetes, Jaeger and Prometheus so the
/// browser never needs direct network access to cluster-internal services.
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/links", (IConfiguration config) =>
            Results.Ok(GetLinks(config)));

        group.MapGet("/overview", async (
            IServiceRegistry registry,
            IKubernetesCollector k8s,
            IConfiguration config) =>
        {
            var services = await BuildServiceOverviewAsync(registry, k8s);

            var deployments = new List<DeploymentInfo>();
            var pods = new List<PodInfo>();
            try
            {
                deployments = await k8s.ListDeploymentsAsync();
                pods = await k8s.ListPodsAsync();
            }
            catch
            {
                // Kubernetes API may be unreachable outside the cluster; overview still returns registry data.
            }

            return Results.Ok(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                services,
                cluster = new
                {
                    totalPods = pods.Count,
                    readyPods = pods.Count(p => p.Ready),
                    totalDeployments = deployments.Count,
                    readyDeployments = deployments.Count(d => d.Replicas > 0 && d.ReadyReplicas >= d.Replicas)
                },
                links = GetLinks(config)
            });
        });

        group.MapGet("/services", async (IServiceRegistry registry, IKubernetesCollector k8s) =>
            Results.Ok(await BuildServiceOverviewAsync(registry, k8s)));

        group.MapGet("/traces", async (IJaegerCollector jaeger, string service, int limit = 15) =>
        {
            try
            {
                var spans = await jaeger.GetTraceSpansAsync(service, limit: limit <= 0 ? 15 : limit);
                var traces = spans
                    .GroupBy(s => s.TraceId)
                    .Select(g => new
                    {
                        traceId = g.Key,
                        rootOperation = g.OrderByDescending(s => s.Duration).First().OperationName,
                        spanCount = g.Count(),
                        durationMs = g.Max(s => s.Duration) / 1000.0
                    })
                    .OrderByDescending(t => t.durationMs)
                    .ToList();

                return Results.Ok(traces);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/dependencies", async (IJaegerCollector jaeger) =>
        {
            try
            {
                return Results.Ok(await jaeger.GetDependenciesAsync());
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/metrics", async (IPrometheusCollector prometheus, string query) =>
        {
            try
            {
                return Results.Ok(await prometheus.QueryAsync(query));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/metrics/range", async (
            IPrometheusCollector prometheus,
            string query,
            int minutes = 30,
            string? step = "15s") =>
        {
            try
            {
                var end = DateTimeOffset.UtcNow;
                var start = end.AddMinutes(minutes <= 0 ? -30 : -minutes);
                return Results.Ok(await prometheus.QueryRangeAsync(query, start, end, step ?? "15s"));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return app;
    }

    private static async Task<List<object>> BuildServiceOverviewAsync(IServiceRegistry registry, IKubernetesCollector k8s)
    {
        var result = new List<object>();
        foreach (var (name, endpoint) in registry.GetAll())
        {
            HealthStatus? health = null;
            try
            {
                health = await k8s.GetHealthAsync(name);
            }
            catch
            {
                // Health check is best-effort; the dashboard shows "unknown" when it fails.
            }

            result.Add(new
            {
                name,
                endpoint.BaseUrl,
                endpoint.OpenApiPath,
                health
            });
        }
        return result;
    }

    private static Dictionary<string, string> GetLinks(IConfiguration config) =>
        config.GetSection("Dashboard:Links").Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>();
}
