using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Api;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Tools;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Observability (OTel traces + Prometheus metrics for the MCP Server itself)
builder.Services.AddObservability("McpServer", builder.Configuration);

// Configuration
var jaegerBaseUrl = builder.Configuration["DataSources:Jaeger:BaseUrl"] ?? "http://jaeger:16686";
var prometheusBaseUrl = builder.Configuration["DataSources:Prometheus:BaseUrl"] ?? "http://prometheus:9090";
var k8sNamespace = builder.Configuration["DataSources:Kubernetes:Namespace"] ?? "mcp-apis";

// Core collectors
// Typed HttpClients via IHttpClientFactory: avoids socket exhaustion / stale-DNS
// issues that come from manually `new`-ing up long-lived HttpClient instances,
// and gives us a single place to configure timeouts.
builder.Services.AddHttpClient<IJaegerCollector, JaegerService>(client =>
{
    client.BaseAddress = new Uri(jaegerBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<IPrometheusCollector, PrometheusService>(client =>
{
    client.BaseAddress = new Uri(prometheusBaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IKubernetesCollector>(new KubernetesService(k8sNamespace));

// Application catalog: live inventory of everything discovered in the cluster.
// The legacy registry is a read-only view of it filtered by enabled + validated,
// so spec-based tools honor the dashboard indexing toggle automatically.
builder.Services.AddSingleton<IApplicationCatalog, ApplicationCatalog>();
builder.Services.AddSingleton<IServiceRegistry, ServiceRegistry>();
builder.Services.AddSingleton<IIndexingStateStore, KubernetesIndexingStateStore>();

// OpenAPI collector depends on registry + catalog
builder.Services.AddHttpClient<IOpenApiCollector, OpenApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Discovery: validation (typed client) + orchestrator (singleton, resolves the
// validator/Jaeger clients through scopes so HttpClient handlers keep rotating)
builder.Services.AddHttpClient<IServiceValidator, ServiceValidator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<DiscoveryOrchestrator>();
builder.Services.AddSingleton<IDiscoveryOrchestrator>(sp => sp.GetRequiredService<DiscoveryOrchestrator>());
builder.Services.AddHostedService<DiscoveryBackgroundService>();

// Register MCP Server with all tools
builder.Services
    .AddMcpServer(opts =>
    {
        opts.ServerInfo = new()
        {
            Name = "mcp-apis-server",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithTools<ListServicesTool>()
    .WithTools<ListDiscoveredApplicationsTool>()
    .WithTools<GetOpenApiTool>()
    .WithTools<TraceRouteTool>()
    .WithTools<ExplainApiTool>()
    .WithTools<GetHealthTool>()
    .WithTools<FindDependenciesTool>()
    .WithTools<FindDataOriginTool>()
    .WithTools<QueryMetricsTool>();

var app = builder.Build();

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

app.UseBodyCaptureTelemetry();

app.MapMcp();

app.MapDashboardApi();
app.MapApplicationsApi();

app.MapFallbackToFile("/dashboard", "dashboard/index.html");
app.MapFallbackToFile("/dashboard/{**slug}", "dashboard/index.html");

// Health + metrics endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mcp-apis-server" }));
app.MapPrometheusScrapingEndpoint();

app.Run();
