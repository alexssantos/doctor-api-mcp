using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public sealed class InMemoryIndexingStateStore : IIndexingStateStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, bool> _values = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return new Dictionary<string, bool>(_values, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SaveAsync(string appName, bool enabled, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _values[appName] = enabled;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}
