using System.ComponentModel;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class GetOpenApiTool
{
    [McpServerTool(Name = "get_openapi"), Description("Retrieves the OpenAPI specification for a given service.")]
    public static async Task<string> Execute(
        IOpenApiCollector openApi,
        [Description("Service name (e.g. catalog-api, orders-api)")] string serviceName)
    {
        return await openApi.GetOpenApiSpecAsync(serviceName);
    }
}
