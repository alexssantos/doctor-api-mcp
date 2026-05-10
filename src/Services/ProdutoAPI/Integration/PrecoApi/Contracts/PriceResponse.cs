namespace McpApis.ProdutoAPI.Integration.PrecoApi.Contracts;

/// <summary>Consumer-side copy of PrecoAPI's PriceResponse contract.</summary>
public record PriceResponse(
    Guid ProductId,
    decimal Value,
    string Currency);
