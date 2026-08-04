using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Tools;

/// <summary>
/// Central gate applied by tools that reach Jaeger/Kubernetes directly (bypassing
/// the registry): blocks data access for applications the operator disabled in
/// the dashboard. Unknown names pass through (fail-open) so excluded
/// infrastructure such as jaeger/prometheus stays queryable.
/// </summary>
public static class ToolGuard
{
    public static bool EnsureEnabled(IApplicationCatalog catalog, string nameOrAlias, out string errorJson)
    {
        if (catalog.TryGet(nameOrAlias, out var app) && !app.Enabled)
        {
            errorJson = JsonSerializer.Serialize(new
            {
                error = $"Application '{app.Name}' is disabled for MCP indexing.",
                hint = "Enable it in the dashboard (/dashboard) or via PUT /api/dashboard/applications/{name}/indexing.",
                enabledApplications = catalog.GetAll()
                    .Where(a => a.Enabled)
                    .Select(a => a.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            }, new JsonSerializerOptions { WriteIndented = true });
            return false;
        }

        errorJson = "";
        return true;
    }
}
