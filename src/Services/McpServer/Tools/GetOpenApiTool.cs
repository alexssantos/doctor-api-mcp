using System.ComponentModel;
using McpApis.McpServer.Services;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class GetOpenApiTool
{
    [McpServerTool(Name = "get_openapi"), Description("Retrieves the OpenAPI specification for a given service (precoapi or produtoapi).")]
    public static async Task<string> Execute(
        OpenApiService openApi,
        [Description("Service name: precoapi or produtoapi")] string serviceName)
    {
        return await openApi.GetOpenApiSpecAsync(serviceName);
    }
}
