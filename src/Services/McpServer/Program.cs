using McpApis.McpServer.Services;
using McpApis.McpServer.Tools;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var jaegerBaseUrl = builder.Configuration["Jaeger:BaseUrl"] ?? "http://jaeger:16686";
var k8sNamespace = builder.Configuration["Kubernetes:Namespace"] ?? "mcp-apis";

// Register services
builder.Services.AddSingleton(new JaegerService(jaegerBaseUrl));
builder.Services.AddSingleton(new OpenApiService());
builder.Services.AddSingleton(new KubernetesService(k8sNamespace));

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

app.MapMcp();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mcp-apis-server" }));

app.Run();
