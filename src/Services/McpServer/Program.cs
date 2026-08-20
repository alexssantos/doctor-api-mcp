using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Api;
using McpApis.McpServer.Infrastructure.Caching;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Engines.Recommendations;
using McpApis.McpServer.Engines.RootCause;
using McpApis.McpServer.Engines.SystemHealth;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Security;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Providers.Jaeger;
using McpApis.McpServer.Providers.Kubernetes;
using McpApis.McpServer.Providers.Loki;
using McpApis.McpServer.Providers.OpenApi;
using McpApis.McpServer.Providers.Prometheus;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Tools;
using McpApis.McpServer.Tools.VNext;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Observability (OTel traces + Prometheus metrics for the MCP Server itself)
builder.Services.AddObservability("McpServer", builder.Configuration);
builder.Services.AddObservabilityIntelligenceOptions(
    builder.Configuration, builder.Environment);
builder.Services.AddObservabilitySecurity(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// Configuration
var jaegerBaseUrl = builder.Configuration["DataSources:Jaeger:BaseUrl"] ?? "http://jaeger:16686";
var prometheusBaseUrl = builder.Configuration["DataSources:Prometheus:BaseUrl"] ?? "http://prometheus:9090";
var lokiBaseUrl = builder.Configuration["DataSources:Loki:BaseUrl"] ?? "http://loki:3100";
var k8sNamespace = builder.Configuration["DataSources:Kubernetes:Namespace"] ?? "mcp-apis";
var clusterAccess = builder.Configuration
    .GetSection(ClusterAccessOptions.SectionName)
    .Get<ClusterAccessOptions>() ?? new ClusterAccessOptions();

// Core collectors
// Typed HttpClients via IHttpClientFactory: avoids socket exhaustion / stale-DNS
// issues that come from manually `new`-ing up long-lived HttpClient instances,
// and gives us a single place to configure timeouts.
builder.Services.AddHttpClient<IJaegerCollector, JaegerService>(client =>
{
    client.BaseAddress = new Uri(jaegerBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(CreateBackendHandler);
builder.Services.AddHttpClient<IPrometheusCollector, PrometheusService>(client =>
{
    client.BaseAddress = new Uri(prometheusBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(CreateBackendHandler);
builder.Services.AddHttpClient<ILogsProvider, LokiLogsProvider>(client =>
{
    client.BaseAddress = new Uri(lokiBaseUrl.TrimEnd('/') + "/");
    client.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(CreateBackendHandler);
if (clusterAccess.Scope == ClusterAccessScope.None)
    builder.Services.AddSingleton<IKubernetesCollector, DisabledKubernetesCollector>();
else
    builder.Services.AddSingleton<IKubernetesCollector>(new KubernetesService(k8sNamespace));

// Application catalog: live inventory of everything discovered in the cluster.
// The legacy registry is a read-only view of it filtered by enabled + validated,
// so spec-based tools honor the dashboard indexing toggle automatically.
builder.Services.AddSingleton<IApplicationCatalog, ApplicationCatalog>();
builder.Services.AddSingleton<IServiceIdentityResolver, ServiceIdentityResolver>();
builder.Services.AddSingleton<IServiceRegistry, ServiceRegistry>();
if (clusterAccess.StateStorage == ClusterStateStorage.Memory)
{
    builder.Services.AddSingleton<IIndexingStateStore, InMemoryIndexingStateStore>();
    builder.Services.AddSingleton<IDeploymentHistoryStore, InMemoryDeploymentHistoryStore>();
}
else
{
    builder.Services.AddSingleton<IIndexingStateStore, KubernetesIndexingStateStore>();
    builder.Services.AddSingleton<IDeploymentHistoryStore, KubernetesDeploymentHistoryStore>();
}
builder.Services.AddSingleton<IObservabilityCache, ObservabilityCache>();
builder.Services.AddSingleton<IClusterRequirementsValidator, ClusterRequirementsValidator>();

// OpenAPI collector depends on registry + catalog
builder.Services.AddHttpClient<IOpenApiCollector, OpenApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(CreateBackendHandler);

// Discovery: validation (typed client) + orchestrator (singleton, resolves the
// validator/Jaeger clients through scopes so HttpClient handlers keep rotating)
builder.Services.AddHttpClient<IServiceValidator, ServiceValidator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(CreateBackendHandler);
builder.Services.AddSingleton<DiscoveryOrchestrator>();
builder.Services.AddSingleton<IDiscoveryOrchestrator>(sp => sp.GetRequiredService<DiscoveryOrchestrator>());
builder.Services.AddHostedService<DiscoveryBackgroundService>();

// Register MCP Server with all tools
builder.Services.AddScoped<IMetricsProvider, PrometheusMetricsProvider>();
builder.Services.AddScoped<ITraceProvider, JaegerTraceProvider>();
builder.Services.AddScoped<IKubernetesProvider, KubernetesProvider>();
builder.Services.AddScoped<IDeploymentEventProvider, DeploymentEventProvider>();
builder.Services.AddScoped<IApplicationSpecProvider, ApplicationSpecProvider>();
builder.Services.AddScoped<IHealthEngine, HealthEngine>();
builder.Services.AddScoped<IHealthAnalysisService, HealthAnalysisService>();
builder.Services.AddScoped<IDependencyEngine, DependencyEngine>();
builder.Services.AddScoped<IAnomalyEngine, AnomalyEngine>();
builder.Services.AddScoped<ICorrelationEngine, CorrelationEngine>();
builder.Services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
builder.Services.AddScoped<IRootCauseEngine, RootCauseEngine>();
builder.Services.AddScoped<ISystemHealthEngine, SystemHealthEngine>();

var mcpBuilder = builder.Services
    .AddMcpServer(opts =>
    {
        opts.ServerInfo = new()
        {
            Name = "mcp-apis-server",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithTools<ListDiscoveredApplicationsTool>()
    .WithTools<GetOpenApiTool>()
    .WithTools<TraceRouteTool>()
    .WithTools<ExplainApiTool>()
    .WithTools<FindDependenciesTool>()
    .WithTools<ServiceGetSpecTool>()
    .WithTools<ServiceGetHealthTool>()
    .WithTools<ServiceGetScoreTool>()
    .WithTools<ServiceGetDependenciesTool>()
    .WithTools<ServiceDetectAnomaliesTool>()
    .WithTools<ServiceGetIncidentTimelineTool>()
    .WithTools<ServiceFindRootCauseTool>()
    .WithTools<SystemGetHealthSummaryTool>();

if (clusterAccess.Scope != ClusterAccessScope.None)
{
    mcpBuilder
        .WithTools<GetHealthTool>()
        .WithTools<FindDataOriginTool>();
    if (clusterAccess.ServiceDiscovery)
        mcpBuilder.WithTools<ListServicesTool>();
}

if (builder.Configuration.GetValue<bool>("Observability:Features:EnableRawQueries"))
    mcpBuilder.WithTools<QueryMetricsTool>();

var app = builder.Build();

var requirementsValidator = app.Services.GetRequiredService<IClusterRequirementsValidator>();
var clusterAccessOptions = app.Services.GetRequiredService<IOptions<ClusterAccessOptions>>().Value;
if (clusterAccessOptions.ValidateOnStart)
    await requirementsValidator.ValidateAsync(forceRefresh: true);

// Populate the catalog before serving traffic so MCP clients that connect early
// already see the discovered applications. The background service re-scans after.
try
{
    await app.Services.GetRequiredService<IDiscoveryOrchestrator>().ScanAsync();
}
catch (Exception ex)
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogError(ex, "Initial discovery scan failed. The catalog starts empty; the background service will retry.");
}

// Serve the React dashboard (built into wwwroot/dashboard) at /dashboard
app.UseDefaultFiles();
app.UseStaticFiles();

// Explicit UseRouting: without it, endpoint matching runs implicitly as the very
// first middleware, so the greedy "/dashboard/{**slug}" fallback below matches
// every request under /dashboard (including real asset files) before the static
// file middleware above ever gets a chance to serve them - StaticFileMiddleware
// no-ops once an endpoint is already matched. Calling UseRouting() here defers
// matching until after UseStaticFiles has had first crack at real files.
app.UseRouting();

app.UseBodyCaptureTelemetry();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapMcp()
    .RequireAuthorization(ObservabilityPolicies.Reader)
    .RequireRateLimiting(ObservabilityPolicies.RateLimit);

app.MapDashboardApi();
app.MapApplicationsApi();

app.MapFallbackToFile("/dashboard", "dashboard/index.html");
app.MapFallbackToFile("/dashboard/{**slug}", "dashboard/index.html");

// Health + metrics endpoints
app.MapGet("/live", () => Results.Ok(new
{
    status = "alive",
    service = "mcp-apis-server",
    observedAt = DateTimeOffset.UtcNow
})).AllowAnonymous();
app.MapGet("/ready", async (
    IDiscoveryOrchestrator discovery,
    IClusterRequirementsValidator requirements,
    CancellationToken cancellationToken) =>
{
    var report = await requirements.ValidateAsync(cancellationToken: cancellationToken);
    if (!report.MeetsMinimumRequirements)
    {
        return Results.Json(new
        {
            status = "requirements-not-met",
            service = "mcp-apis-server",
            mode = report.Mode,
            missingRequirements = report.MissingRequirements
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    if (discovery.LastScanCompletedAt is null)
        return Results.Json(new { status = "starting", service = "mcp-apis-server" }, statusCode: 503);
    return Results.Ok(new
    {
        status = "ready",
        service = "mcp-apis-server",
        mode = report.Mode,
        lastScanAt = discovery.LastScanCompletedAt
    });
}).AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mcp-apis-server" }))
    .AllowAnonymous();
app.MapGet("/api/status", async (
        IDiscoveryOrchestrator discovery,
        IApplicationCatalog catalog,
        IClusterRequirementsValidator requirements,
        CancellationToken cancellationToken) =>
    {
        var report = await requirements.ValidateAsync(cancellationToken: cancellationToken);
        return Results.Ok(new
        {
            status = discovery.LastScanCompletedAt is null
                ? "starting"
                : report.MeetsMinimumRequirements ? "ready" : "requirements-not-met",
            lastScanAt = discovery.LastScanCompletedAt,
            catalogServices = catalog.GetAll().Count,
            enabledServices = catalog.GetAll().Count(a => a.Enabled),
            clusterAccess = new
            {
                report.Mode,
                report.Scope,
                report.ServiceDiscovery,
                report.StateStorage,
                report.VolumesAllowed,
                report.MeetsMinimumRequirements,
                report.MissingRequirements
            }
        });
    })
    .RequireAuthorization(ObservabilityPolicies.Reader)
    .RequireRateLimiting(ObservabilityPolicies.RateLimit);
app.MapGet("/api/requirements", async (
        IClusterRequirementsValidator requirements,
        bool? refresh,
        CancellationToken cancellationToken) =>
        Results.Ok(await requirements.ValidateAsync(refresh == true, cancellationToken)))
    .RequireAuthorization(ObservabilityPolicies.Reader)
    .RequireRateLimiting(ObservabilityPolicies.RateLimit);
app.MapPrometheusScrapingEndpoint()
    .RequireAuthorization(ObservabilityPolicies.Reader);

app.Run();

static HttpMessageHandler CreateBackendHandler() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectTimeout = TimeSpan.FromSeconds(5)
};

public partial class Program { }
