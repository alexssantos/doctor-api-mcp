namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Persists the user's explicit enable/disable choices for MCP indexing so they
/// survive pod restarts. Only explicit overrides are stored — defaults are not.
/// </summary>
public interface IIndexingStateStore
{
    /// <summary>Loads the explicit overrides: canonical app name → enabled.</summary>
    Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct = default);

    /// <summary>Records an explicit choice. Returns false when persistence is unavailable (state kept in memory only).</summary>
    Task<bool> SaveAsync(string appName, bool enabled, CancellationToken ct = default);
}
