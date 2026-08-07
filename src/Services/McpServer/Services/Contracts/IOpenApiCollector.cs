namespace McpApis.McpServer.Services.Contracts;

public interface IOpenApiCollector
{
    Task<string> GetOpenApiSpecAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<string> GetOpenApiSpecAsync(
        string serviceName,
        string namespaceName,
        CancellationToken cancellationToken = default);
    Task<List<RouteInfo>> GetRoutesAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<List<RouteInfo>> GetRoutesAsync(
        string serviceName,
        string namespaceName,
        CancellationToken cancellationToken = default);
    IReadOnlyCollection<string> GetKnownServices();
}
