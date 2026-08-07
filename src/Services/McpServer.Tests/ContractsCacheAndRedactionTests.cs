using System.Text.Json;
using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Infrastructure.Caching;

namespace McpApis.McpServer.Tests;

public sealed class ContractsCacheAndRedactionTests
{
    [Fact]
    public void Envelope_serializes_state_axes_as_strings()
    {
        var envelope = ObservationEnvelope<object>.Success(
            new { healthStatus = HealthState.Degraded },
            null,
            null,
            [new SourceStatus("metrics", SourceAvailability.Stale, DateTimeOffset.UtcNow, 30, 4, [])]);

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"executionStatus\":\"partial\"", json);
        Assert.Contains("\"availability\":\"stale\"", json);
        Assert.Contains("\"healthStatus\":\"degraded\"", json);
    }

    [Fact]
    public async Task Cache_coalesces_concurrent_misses_and_reuses_value()
    {
        var cache = new ObservabilityCache();
        var calls = 0;

        async Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(25);
            return 42;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrCreateAsync("answer", TimeSpan.FromMinutes(1), Factory)));
        var cached = await cache.GetOrCreateAsync("answer", TimeSpan.FromMinutes(1), Factory);

        Assert.All(results, value => Assert.Equal(42, value));
        Assert.Equal(42, cached);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Redactor_removes_nested_secrets_tokens_and_emails()
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "password", "token" };
        var json = """{"user":{"email":"ana@example.com","password":"p@ss"},"token":"abc"}""";

        var redacted = SensitiveDataRedactor.Redact(json, fields);
        var text = SensitiveDataRedactor.Redact(
            "Authorization: Bearer abc.def.ghi for ana@example.com", fields);

        Assert.DoesNotContain("p@ss", redacted);
        Assert.DoesNotContain("abc\"", redacted);
        Assert.DoesNotContain("ana@example.com", redacted);
        Assert.Contains("[REDACTED]", redacted);
        Assert.DoesNotContain("abc.def.ghi", text);
        Assert.DoesNotContain("ana@example.com", text);
    }
}
