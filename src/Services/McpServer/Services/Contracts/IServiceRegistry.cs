namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Runtime registry of services that have been discovered and validated.
/// Populated at startup by IServiceDiscovery + IServiceValidator.
/// </summary>
public interface IServiceRegistry
{
    IReadOnlyDictionary<string, string> GetAll();
    bool TryGetBaseUrl(string serviceName, out string baseUrl);
    void Register(string serviceName, string baseUrl);
}
