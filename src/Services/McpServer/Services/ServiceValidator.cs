using System.Text.Json;
using McpApis.McpServer.Services.Contracts;
using McpApis.McpServer.Infrastructure.Security;

namespace McpApis.McpServer.Services;

/// <summary>
/// Validates that a candidate service meets the minimum requirements before indexing:
///   1. Service responds (non-5xx) on /health or /.
///   2. OpenAPI spec is accessible on one of the configured DataSources:OpenApiSpecPaths.
///   3. OpenAPI spec is valid JSON with at least one path defined.
///
/// The first OpenAPI path that responds with HTTP 200 is recorded in the validation result
/// so the registry can use it for subsequent spec fetches.
/// </summary>
public class ServiceValidator : IServiceValidator
{
    private static readonly string[] DefaultOpenApiPaths = ["/openapi/v1.json"];

    private readonly HttpClient _http;
    private readonly ILogger<ServiceValidator> _logger;
    private readonly string[] _openApiPaths;
    private readonly IServiceUrlPolicy _urlPolicy;

    public ServiceValidator(
        HttpClient http,
        IConfiguration config,
        ILogger<ServiceValidator> logger,
        IServiceUrlPolicy urlPolicy)
    {
        _http = http;
        _logger = logger;
        _urlPolicy = urlPolicy;
        _openApiPaths = config.GetSection("DataSources:OpenApiSpecPaths").Get<string[]>()
            ?? DefaultOpenApiPaths;
    }

    public async Task<ServiceValidationResult> ValidateAsync(
        string serviceName,
        string baseUrl,
        string? namespaceName = null,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        if (!_urlPolicy.TryValidate(baseUrl, namespaceName, out var safeUri, out var policyError))
        {
            failures.Add($"Service URL rejected: {policyError}");
            return Fail(serviceName, baseUrl, failures);
        }
        var trimmed = safeUri.ToString().TrimEnd('/');

        if (!await IsReachableAsync(trimmed, failures, cancellationToken))
            return Fail(serviceName, baseUrl, failures);

        var (spec, resolvedPath) = await ProbeOpenApiAsync(trimmed, failures, cancellationToken);
        if (spec is null)
            return Fail(serviceName, baseUrl, failures);

        ValidateSpecContent(spec, failures);

        return new ServiceValidationResult(
            serviceName, baseUrl, resolvedPath!, failures.Count == 0, failures);
    }

    private async Task<bool> IsReachableAsync(
        string baseUrl,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        foreach (var path in new[] { "/health", "/" })
        {
            try
            {
                using var response = await _http.GetAsync(baseUrl + path, cancellationToken);
                if ((int)response.StatusCode < 500)
                    return true;

                failures.Add($"Service returned HTTP {(int)response.StatusCode} on {path}");
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && path == "/health")
            {
                _logger.LogDebug(ex, "Health probe failed for {BaseUrl}, trying root", baseUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"Service unreachable: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    private async Task<(string? spec, string? resolvedPath)> ProbeOpenApiAsync(
        string baseUrl,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var tried = new List<string>();

        foreach (var path in _openApiPaths)
        {
            try
            {
                using var response = await _http.GetAsync(baseUrl + path, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "OpenAPI spec found at {BaseUrl}{Path}", baseUrl, path);
                    return (await response.Content.ReadAsStringAsync(cancellationToken), path);
                }

                tried.Add($"{path} → HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tried.Add($"{path} → {ex.Message}");
            }
        }

        failures.Add(
            $"OpenAPI spec not found. Probed paths: {string.Join(", ", tried)}");
        return (null, null);
    }

    private static void ValidateSpecContent(string spec, List<string> failures)
    {
        try
        {
            var doc = JsonDocument.Parse(spec);
            if (!doc.RootElement.TryGetProperty("paths", out var paths)
                || !paths.EnumerateObject().Any())
            {
                failures.Add("OpenAPI spec has no paths defined");
            }
        }
        catch
        {
            failures.Add("OpenAPI spec is not valid JSON");
        }
    }

    private static ServiceValidationResult Fail(
        string name, string url, List<string> failures) =>
        new(name, url, "", false, failures);
}
