using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Providers.Loki;

/// <summary>
/// Read-only Loki adapter. LogQL is assembled exclusively from server-owned
/// templates and a catalog-resolved identity; callers can never submit raw LogQL.
/// </summary>
public sealed partial class LokiLogsProvider(
    HttpClient httpClient,
    IOptions<ObservabilityLimitsOptions> limits,
    IOptions<ObservabilityFeatureOptions> features,
    ILogger<LokiLogsProvider> logger) : ILogsProvider
{
    private static readonly IReadOnlySet<string> SensitiveFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "pwd", "secret", "token", "authorization", "cookie",
            "apiKey", "connectionString", "clientSecret", "accessToken", "refreshToken"
        };

    public Task<ProviderResult<IReadOnlyList<LogPattern>>> GetErrorPatternsAsync(
        ServiceIdentity service,
        TimeWindow window,
        int limit,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            service,
            window,
            Math.Clamp(limit, 1, limits.Value.MaxLogs),
            " |~ \"(?i)(error|exception|fail|fatal|timeout|warn)\"",
            cancellationToken);

    public Task<ProviderResult<IReadOnlyList<LogPattern>>> FindByTraceIdAsync(
        ServiceIdentity service,
        string traceId,
        TimeWindow window,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(traceId) || !TraceIdRegex().IsMatch(traceId))
        {
            return Task.FromResult(ProviderResult<IReadOnlyList<LogPattern>>.Unavailable(
                "logs", 0, "Trace ID must contain 16 to 64 hexadecimal characters."));
        }

        return QueryAsync(
            service,
            window,
            Math.Clamp(limit, 1, limits.Value.MaxLogs),
            $" |= \"{traceId.ToLowerInvariant()}\"",
            cancellationToken);
    }

    private async Task<ProviderResult<IReadOnlyList<LogPattern>>> QueryAsync(
        ServiceIdentity service,
        TimeWindow window,
        int limit,
        string lineFilter,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.logs.query");
        activity?.SetTag("provider", "loki");
        if (!features.Value.EnableLogs)
            return ProviderResult<IReadOnlyList<LogPattern>>.Unavailable(
                "logs", 0, "Loki integration is disabled by feature policy.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var query = BuildSelector(service) + lineFilter;
            var requestUri = BuildRequestUri(query, window, limit);
            using var response = await httpClient.GetAsync(
                requestUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (payload.Length > limits.Value.MaxResponseBytes)
                throw new InvalidDataException(
                    $"Loki response exceeded the {limits.Value.MaxResponseBytes}-byte provider limit.");

            var patterns = Parse(payload, limit);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "available", patterns.Count);
            var observedAt = patterns.Count == 0
                ? DateTimeOffset.UtcNow
                : patterns.Max(pattern => pattern.LastSeen);
            return ProviderResult<IReadOnlyList<LogPattern>>.Available(
                "logs", patterns, observedAt, (long)elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout", 0);
            return ProviderResult<IReadOnlyList<LogPattern>>.Unavailable(
                "logs", (long)elapsed.TotalMilliseconds, "Loki provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Loki log collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable", 0);
            var warning = ex switch
            {
                InvalidDataException => ex.Message,
                JsonException => "Loki returned malformed JSON.",
                HttpRequestException => "Loki request failed.",
                _ => "Loki log collection failed."
            };
            return ProviderResult<IReadOnlyList<LogPattern>>.Unavailable(
                "logs", (long)elapsed.TotalMilliseconds, warning);
        }
    }

    internal static string BuildSelector(ServiceIdentity service)
    {
        var names = new[]
            {
                service.DeploymentName,
                service.KubernetesServiceName,
                service.ServiceName
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Regex.Escape(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var podPattern = string.Join("|", names);
        return $"{{namespace=\"{EscapeLabelValue(service.Namespace)}\",pod=~\"^(?:{podPattern})(?:-.+)?$\"}}";
    }

    private static string BuildRequestUri(string query, TimeWindow window, int limit)
    {
        static long Nanoseconds(DateTimeOffset value) =>
            checked((value.ToUnixTimeMilliseconds() * 1_000_000) +
                    ((value.Ticks % TimeSpan.TicksPerMillisecond) * 100));

        return "loki/api/v1/query_range" +
               $"?query={Uri.EscapeDataString(query)}" +
               $"&start={Nanoseconds(window.From)}" +
               $"&end={Nanoseconds(window.To)}" +
               $"&limit={limit.ToString(CultureInfo.InvariantCulture)}" +
               "&direction=backward";
    }

    private static IReadOnlyList<LogPattern> Parse(byte[] payload, int limit)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("status", out var status) ||
            !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase) ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
            throw new JsonException("Unexpected Loki response shape.");

        var groups = new Dictionary<string, LogAccumulator>(StringComparer.Ordinal);
        foreach (var streamResult in result.EnumerateArray())
        {
            var pod = streamResult.TryGetProperty("stream", out var stream) &&
                      stream.TryGetProperty("pod", out var podNode)
                ? podNode.GetString()
                : null;
            if (!streamResult.TryGetProperty("values", out var values) ||
                values.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                    continue;
                var timestamp = ParseTimestamp(item[0].GetString());
                var raw = item[1].GetString() ?? string.Empty;
                var parsed = ParseLine(raw);
                var fingerprint = Fingerprint(parsed.Message);
                if (!groups.TryGetValue(fingerprint, out var group))
                {
                    group = new LogAccumulator(
                        fingerprint, parsed.Level, parsed.Message, timestamp,
                        parsed.TraceId, pod, parsed.Redacted);
                    groups[fingerprint] = group;
                }
                else
                {
                    group.Add(timestamp, parsed.TraceId, pod, parsed.Redacted);
                }
            }
        }

        return groups.Values
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.LastSeen)
            .Take(limit)
            .Select(group => group.ToPattern())
            .ToArray();
    }

    private static ParsedLog ParseLine(string raw)
    {
        var message = raw;
        string? level = null;
        string? traceId = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            message = GetString(root, "message", "Message", "msg", "@m") ?? raw;
            level = GetString(root, "level", "Level", "log.level", "severityText");
            traceId = GetString(root, "traceId", "TraceId", "trace_id", "trace.id");
        }
        catch (JsonException)
        {
            traceId = TraceIdInTextRegex().Match(raw) is { Success: true } match
                ? match.Groups[1].Value
                : null;
        }

        var redacted = SensitiveDataRedactor.Redact(message, SensitiveFields);
        level ??= InferLevel(message);
        return new ParsedLog(
            string.IsNullOrWhiteSpace(level) ? "error" : level.ToLowerInvariant(),
            redacted,
            traceId,
            !string.Equals(message, redacted, StringComparison.Ordinal));
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in root.EnumerateObject())
        {
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var nanoseconds))
            return DateTimeOffset.UtcNow;
        var seconds = Math.DivRem(nanoseconds, 1_000_000_000, out var remainder);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(remainder / 100);
    }

    private static string Fingerprint(string message)
    {
        var normalized = VolatileValueRegex().Replace(message.ToLowerInvariant(), "#");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16]
            .ToLowerInvariant();
    }

    private static string InferLevel(string message)
    {
        if (FatalRegex().IsMatch(message)) return "fatal";
        if (WarningRegex().IsMatch(message)) return "warning";
        return "error";
    }

    private static string EscapeLabelValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void Record(TimeSpan elapsed, string availability, int processed)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", "loki"),
            new KeyValuePair<string, object?>("availability", availability)
        };
        ObservabilityTelemetry.ProviderCalls.Add(1, tags);
        ObservabilityTelemetry.ProviderDuration.Record(elapsed.TotalMilliseconds, tags);
        ObservabilityTelemetry.ProcessedItems.Add(processed,
            new KeyValuePair<string, object?>("item.type", "logs"));
    }

    private sealed record ParsedLog(string Level, string Message, string? TraceId, bool Redacted);

    private sealed class LogAccumulator(
        string fingerprint,
        string level,
        string message,
        DateTimeOffset timestamp,
        string? traceId,
        string? pod,
        bool redacted)
    {
        public int Count { get; private set; } = 1;
        public DateTimeOffset FirstSeen { get; private set; } = timestamp;
        public DateTimeOffset LastSeen { get; private set; } = timestamp;
        public string? TraceId { get; private set; } = traceId;
        public string? Pod { get; private set; } = pod;
        public bool Redacted { get; private set; } = redacted;

        public void Add(DateTimeOffset seenAt, string? candidateTraceId, string? candidatePod, bool wasRedacted)
        {
            Count++;
            if (seenAt < FirstSeen) FirstSeen = seenAt;
            if (seenAt > LastSeen) LastSeen = seenAt;
            TraceId ??= candidateTraceId;
            Pod ??= candidatePod;
            Redacted |= wasRedacted;
        }

        public LogPattern ToPattern() => new(
            fingerprint, level, message, Count, FirstSeen, LastSeen, TraceId, Pod, Redacted);
    }

    [GeneratedRegex("^[a-fA-F0-9]{16,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdRegex();

    [GeneratedRegex("(?i)(?:trace[_ .-]?id)[=: \\\"']+([a-f0-9]{16,64})")]
    private static partial Regex TraceIdInTextRegex();

    [GeneratedRegex("(?i)\\b(?:[0-9a-f]{8}-[0-9a-f-]{27,}|[0-9a-f]{16,64}|\\d+(?:\\.\\d+)?)\\b")]
    private static partial Regex VolatileValueRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("(?i)\\b(fatal|critical)\\b")]
    private static partial Regex FatalRegex();

    [GeneratedRegex("(?i)\\b(warn|warning)\\b")]
    private static partial Regex WarningRegex();
}
