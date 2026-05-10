using McpApis.BuildingBlocks.Observability;
using McpApis.PrecoAPI.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<PriceDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Observability (OTel + optional body capture)
builder.Services.AddObservability("PrecoAPI", builder.Configuration);

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

app.MapOpenApi();
app.MapScalarApiReference(opts =>
{
    opts.Title = "PrecoAPI";
    opts.Theme = ScalarTheme.DeepSpace;
});

app.UseAuthorization();
app.MapControllers();

app.Run();

