namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Resolved entry for a registered service: its base URL and the exact OpenAPI spec path that responded.
/// </summary>
public record ServiceEndpoint(string BaseUrl, string OpenApiPath);

/// <summary>
/// Runtime registry of services that have been discovered and validated.
/// Populated at startup by IServiceDiscovery + IServiceValidator.
/// </summary>
public interface IServiceRegistry
{
    IReadOnlyDictionary<string, ServiceEndpoint> GetAll();
    bool TryGet(string serviceName, out ServiceEndpoint endpoint);
    void Register(string serviceName, string baseUrl, string openApiPath);
}
