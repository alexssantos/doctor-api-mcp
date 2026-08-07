using System.Diagnostics;
using System.Text.Json;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Providers.OpenApi;

public sealed class ApplicationSpecProvider(
    IApplicationCatalog catalog,
    IOpenApiCollector openApi,
    ILogger<ApplicationSpecProvider> logger) : IApplicationSpecProvider
{
    public async Task<ProviderResult<ServiceSpecReport>> GetSpecAsync(
        ServiceIdentity service,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.openapi.spec");
        activity?.SetTag("provider", "openapi");

        if (!catalog.TryGet(service.ServiceName, service.Namespace, out var app))
            return ProviderResult<ServiceSpecReport>.Unavailable(
                "application_spec", 0, "Service disappeared from the catalog.");

        var warnings = new List<string>();
        var endpoints = new List<ApiEndpointSummary>();
        var description = app.Description;
        var version = app.Version;
        var openApiAvailability = app.OpenApi.Validated
            ? SourceAvailability.Available
            : SourceAvailability.Unavailable;

        if (app.OpenApi.Validated)
        {
            try
            {
                var document = await openApi.GetOpenApiSpecAsync(
                    service.ServiceName, service.Namespace, cancellationToken);
                using var json = JsonDocument.Parse(document);
                if (json.RootElement.TryGetProperty("info", out var info))
                {
                    description ??= info.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                    version ??= info.TryGetProperty("version", out var ver) ? ver.GetString() : null;
                }
                endpoints.AddRange(ParseEndpoints(json.RootElement));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "OpenAPI parsing failed for {Namespace}/{Service}.",
                    service.Namespace, service.ServiceName);
                warnings.Add("OpenAPI could not be read; catalog metadata is still available.");
                openApiAvailability = SourceAvailability.Unavailable;
            }
        }
        else
        {
            warnings.Add(app.OpenApi.Failures.Count > 0
                ? $"OpenAPI unavailable: {string.Join("; ", app.OpenApi.Failures)}"
                : "OpenAPI unavailable.");
        }

        var coverage = app.Coverage with { OpenApi = openApiAvailability };
        var report = new ServiceSpecReport(
            description,
            app.Owner,
            app.Team,
            version,
            app.Image,
            app.ImageDigest,
            app.Revision,
            app.DesiredReplicas,
            app.ReadyReplicas,
            app.Labels,
            app.Annotations,
            app.Selector,
            coverage,
            endpoints,
            app.DeclaredDependencies);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var availability = openApiAvailability == SourceAvailability.Available
            ? SourceAvailability.Available
            : SourceAvailability.Stale;
        return new ProviderResult<ServiceSpecReport>(
            "application_spec",
            availability,
            report,
            app.LastSeen,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - app.LastSeen).TotalSeconds),
            warnings,
            (long)elapsed.TotalMilliseconds);
    }

    private static IEnumerable<ApiEndpointSummary> ParseEndpoints(JsonElement root)
    {
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("get" or "post" or "put" or "patch" or "delete" or "head" or "options"))
                    continue;
                var value = operation.Value;
                yield return new ApiEndpointSummary(
                    operation.Name.ToUpperInvariant(),
                    path.Name,
                    value.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                    value.TryGetProperty("operationId", out var id) ? id.GetString() : null,
                    value.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Object
                        ? responses.EnumerateObject().Select(r => r.Name).Order().ToArray()
                        : []);
            }
        }
    }
}
