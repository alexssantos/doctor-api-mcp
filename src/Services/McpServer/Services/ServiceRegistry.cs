using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Thread-safe in-memory registry of services discovered and validated at startup.
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<string, string> _services =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(string serviceName, string baseUrl) =>
        _services[serviceName] = baseUrl.TrimEnd('/');

    public IReadOnlyDictionary<string, string> GetAll() => _services;

    public bool TryGetBaseUrl(string serviceName, out string baseUrl) =>
        _services.TryGetValue(serviceName, out baseUrl!);
}
