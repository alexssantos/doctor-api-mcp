namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Discovers candidate services and their base URLs from one or more sources
/// (static config, Kubernetes labels, etc.).
/// Returns a name → baseUrl map for further validation before registration.
/// </summary>
public interface IServiceDiscovery
{
    Task<Dictionary<string, string>> DiscoverServicesAsync();
}
