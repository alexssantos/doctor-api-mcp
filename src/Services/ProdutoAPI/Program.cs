using McpApis.BuildingBlocks.Http;
using McpApis.BuildingBlocks.Observability;
using McpApis.ProdutoAPI.Data;
using McpApis.ProdutoAPI.Integration.PrecoApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<ProductDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// PrecoAPI typed HttpClient with CorrelationHandler
var precoApiUrl = builder.Configuration["PrecoApi:BaseUrl"] ?? "http://localhost:5002";
builder.Services.AddHttpClientWithCorrelation<PriceClient, PriceClient>(precoApiUrl);

// Observability
builder.Services.AddObservability("ProdutoAPI", builder.Configuration);

// Rate limiting: protects against abuse/DoS since the API has no authentication.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Controllers + OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi(opts =>
{
    opts.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "ProdutoAPI";
        doc.Info.Description = "Serviço responsável por gerenciar produtos. Consulta PrecoAPI para enriquecer dados com preços.";
        doc.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Ensure DB tables exist
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseBodyCaptureTelemetry();

// Health endpoint for k8s liveness/readiness probes.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "produtoapi" }));

// ProdutoAPI is an example service for testing the MCP server, so the OpenAPI spec
// and Scalar UI stay enabled in every environment, including Production (the spec
// is also required by the MCP Server's ServiceValidator - see README).
app.MapOpenApi();
app.MapScalarApiReference(opts =>
{
    opts.Title = "ProdutoAPI";
    opts.Theme = ScalarTheme.DeepSpace;
});

app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();
