using McpApis.BuildingBlocks.Http;
using McpApis.BuildingBlocks.Observability;
using McpApis.ProdutoAPI.Data;
using McpApis.ProdutoAPI.Integration.PrecoApi;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<ProductDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// PrecoAPI typed HttpClient with CorrelationHandler
var precoApiUrl = builder.Configuration["PrecoApi:BaseUrl"] ?? "http://localhost:5002";
builder.Services.AddHttpClientWithCorrelation<PriceClient, PriceClient>(precoApiUrl);

// Observability
builder.Services.AddObservability("ProdutoAPI", builder.Configuration);

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

app.MapOpenApi();
app.MapScalarApiReference(opts =>
{
    opts.Title = "ProdutoAPI";
    opts.Theme = ScalarTheme.DeepSpace;
});

app.UseAuthorization();
app.MapControllers();

app.Run();
