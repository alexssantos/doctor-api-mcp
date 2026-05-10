using System.ComponentModel.DataAnnotations;

namespace McpApis.ProdutoAPI.Dtos;

public record CreateProductRequest(
    [Required][StringLength(200)] string Name,
    [StringLength(1000)] string? Description);

public record UpdateProductRequest(
    [Required][StringLength(200)] string Name,
    [StringLength(1000)] string? Description);

public record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    PriceDto? Price);

public record PriceDto(decimal Value, string Currency);
