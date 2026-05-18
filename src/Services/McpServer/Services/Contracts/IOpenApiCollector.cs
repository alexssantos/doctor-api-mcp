namespace McpApis.McpServer.Services.Contracts;

public interface IOpenApiCollector
{
    Task<string> GetOpenApiSpecAsync(string serviceName);
    Task<List<RouteInfo>> GetRoutesAsync(string serviceName);
    IReadOnlyCollection<string> GetKnownServices();
}
