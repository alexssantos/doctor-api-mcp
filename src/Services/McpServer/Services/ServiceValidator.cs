using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Validates that a candidate service meets the minimum requirements before indexing:
///   1. Service responds (non-5xx) on its base URL or /health endpoint.
///   2. OpenAPI spec is accessible at /openapi/v1.json and returns HTTP 200.
///   3. OpenAPI spec is valid JSON with at least one path defined.
/// </summary>
public class ServiceValidator : IServiceValidator
{
    private readonly HttpClient _http;
    private readonly ILogger<ServiceValidator> _logger;

    public ServiceValidator(ILogger<ServiceValidator> logger)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _logger = logger;
    }

    public async Task<ServiceValidationResult> ValidateAsync(string serviceName, string baseUrl)
    {
        var failures = new List<string>();
        var trimmed = baseUrl.TrimEnd('/');

        if (!await IsReachableAsync(trimmed, failures))
            return Fail(serviceName, baseUrl, failures);

        var spec = await FetchOpenApiSpecAsync(trimmed, failures);
        if (spec is null)
            return Fail(serviceName, baseUrl, failures);

        ValidateSpecContent(spec, failures);

        return new ServiceValidationResult(serviceName, baseUrl, failures.Count == 0, failures);
    }

    private async Task<bool> IsReachableAsync(string baseUrl, List<string> failures)
    {
        // Try /health first, fall back to the root URL
        foreach (var path in new[] { "/health", "/" })
        {
            try
            {
                var response = await _http.GetAsync(baseUrl + path);
                if ((int)response.StatusCode < 500)
                    return true;

                failures.Add($"Service returned HTTP {(int)response.StatusCode} on {path}");
                return false;
            }
            catch (Exception ex) when (path == "/health")
            {
                _logger.LogDebug(ex, "Health probe failed for {BaseUrl}, trying root", baseUrl);
            }
            catch (Exception ex)
            {
                failures.Add($"Service unreachable: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    private async Task<string?> FetchOpenApiSpecAsync(string baseUrl, List<string> failures)
    {
        try
        {
            var response = await _http.GetAsync($"{baseUrl}/openapi/v1.json");
            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"OpenAPI spec not accessible: HTTP {(int)response.StatusCode}");
                return null;
            }
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            failures.Add($"Failed to fetch OpenAPI spec: {ex.Message}");
            return null;
        }
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
        new(name, url, false, failures);
}
