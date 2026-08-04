using McpApis.BuildingBlocks.Observability;
using McpApis.PrecoAPI.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<PriceDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Observability (OTel + optional body capture)
builder.Services.AddObservability("PrecoAPI", builder.Configuration);

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
        doc.Info.Title = "PrecoAPI";
        doc.Info.Description = "Serviço responsável por gerenciar preços de produtos.";
        doc.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Ensure DB tables exist
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PriceDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseBodyCaptureTelemetry();

// Health endpoint for k8s probes (kept separate from the interactive API
// docs UI, which is restricted to Development below).
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "precoapi" }));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opts =>
    {
        opts.Title = "PrecoAPI";
        opts.Theme = ScalarTheme.DeepSpace;
    });
}

app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();

