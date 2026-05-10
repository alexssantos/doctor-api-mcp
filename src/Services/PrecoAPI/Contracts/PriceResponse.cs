namespace McpApis.PrecoAPI.Contracts;

/// <summary>Public contract exposed by PrecoAPI — consumed by ProdutoAPI.</summary>
public record PriceResponse(
    Guid ProductId,
    decimal Value,
    string Currency);
