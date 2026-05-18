using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Tools;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var jaegerBaseUrl = builder.Configuration["DataSources:Jaeger:BaseUrl"] ?? "http://jaeger:16686";
var k8sNamespace = builder.Configuration["DataSources:Kubernetes:Namespace"] ?? "mcp-apis";

// Core collectors
builder.Services.AddSingleton<IJaegerCollector>(new JaegerService(jaegerBaseUrl));
builder.Services.AddSingleton<IKubernetesCollector>(new KubernetesService(k8sNamespace));

// Service registry (populated at startup after discovery + validation)
var registry = new ServiceRegistry();
builder.Services.AddSingleton<IServiceRegistry>(registry);
builder.Services.AddSingleton(registry);

// OpenAPI collector depends on registry
builder.Services.AddSingleton<IOpenApiCollector, OpenApiService>();

// Discovery and validation
builder.Services.AddSingleton<IServiceDiscovery, ServiceDiscoveryService>();
builder.Services.AddSingleton<IServiceValidator, ServiceValidator>();

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
    .WithTools<GetOpenApiTool>()
    .WithTools<TraceRouteTool>()
    .WithTools<ExplainApiTool>()
    .WithTools<GetHealthTool>()
    .WithTools<FindDependenciesTool>()
    .WithTools<FindDataOriginTool>();

var app = builder.Build();

// Run service discovery and validation at startup
await RunServiceDiscoveryAsync(app);

app.MapMcp();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mcp-apis-server" }));

app.Run();

static async Task RunServiceDiscoveryAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var discovery = app.Services.GetRequiredService<IServiceDiscovery>();
    var validator = app.Services.GetRequiredService<IServiceValidator>();
    var registry = app.Services.GetRequiredService<ServiceRegistry>();

    logger.LogInformation("Starting service discovery...");

    Dictionary<string, string> candidates;
    try
    {
        candidates = await discovery.DiscoverServicesAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Service discovery failed. No services will be indexed.");
        return;
    }

    foreach (var (name, url) in candidates)
    {
        var result = await validator.ValidateAsync(name, url);
        if (result.IsValid)
        {
            registry.Register(name, url, result.OpenApiPath);
            logger.LogInformation(
                "✓ Registered service '{Name}' at {Url} (spec: {Path})",
                name, url, result.OpenApiPath);
        }
        else
        {
            logger.LogWarning(
                "✗ Skipped service '{Name}' at {Url}: {Failures}",
                name, url, string.Join("; ", result.Failures));
        }
    }

    logger.LogInformation(
        "Service discovery complete. {Count} service(s) registered: {Names}",
        registry.GetAll().Count, string.Join(", ", registry.GetAll().Keys));
}
