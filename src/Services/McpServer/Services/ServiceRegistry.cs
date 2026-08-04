using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Adapter that projects the application catalog as the legacy service registry.
/// Only applications that are enabled AND have a validated OpenAPI spec are
/// exposed, so every spec-based consumer (OpenApiService and its tools)
/// automatically honors the dashboard indexing toggle.
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly IApplicationCatalog _catalog;

    public ServiceRegistry(IApplicationCatalog catalog) => _catalog = catalog;

    public IReadOnlyDictionary<string, ServiceEndpoint> GetAll() =>
        _catalog.GetAll()
            .Where(IsIndexable)
            .ToDictionary(
                a => a.Name,
                a => new ServiceEndpoint(a.BaseUrl!.TrimEnd('/'), a.OpenApi.Path!),
                StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string serviceName, out ServiceEndpoint endpoint)
    {
        if (_catalog.TryGet(serviceName, out var app) && IsIndexable(app))
        {
            endpoint = new ServiceEndpoint(app.BaseUrl!.TrimEnd('/'), app.OpenApi.Path!);
            return true;
        }

        endpoint = null!;
        return false;
    }

    public void Register(string serviceName, string baseUrl, string openApiPath) =>
        throw new NotSupportedException(
            "Registration is driven by the DiscoveryOrchestrator; the registry is a read-only view of the application catalog.");

    private static bool IsIndexable(DiscoveredApplication app) =>
        app.Enabled && app.OpenApi.Validated && app.BaseUrl is not null;
}
