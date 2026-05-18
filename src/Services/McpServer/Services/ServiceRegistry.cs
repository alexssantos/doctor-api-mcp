using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Thread-safe in-memory registry of services discovered and validated at startup.
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<string, ServiceEndpoint> _services =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string serviceName, string baseUrl, string openApiPath) =>
        _services[serviceName] = new ServiceEndpoint(baseUrl.TrimEnd('/'), openApiPath);

    public IReadOnlyDictionary<string, ServiceEndpoint> GetAll() => _services;

    public bool TryGet(string serviceName, out ServiceEndpoint endpoint) =>
        _services.TryGetValue(serviceName, out endpoint!);
}
