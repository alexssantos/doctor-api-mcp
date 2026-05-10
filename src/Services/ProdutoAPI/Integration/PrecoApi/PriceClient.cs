using McpApis.ProdutoAPI.Integration.PrecoApi.Contracts;
using System.Net.Http.Json;

namespace McpApis.ProdutoAPI.Integration.PrecoApi;

public class PriceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PriceClient> _logger;

    public PriceClient(HttpClient http, ILogger<PriceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<PriceResponse?> GetPriceAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/api/prices/{productId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PrecoAPI returned {StatusCode} for productId={ProductId}",
                    (int)response.StatusCode, productId);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PriceResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch price for productId={ProductId}", productId);
            return null;
        }
    }
}
