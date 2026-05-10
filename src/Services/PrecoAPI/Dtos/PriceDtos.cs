using System.ComponentModel.DataAnnotations;

namespace McpApis.PrecoAPI.Dtos;

public record CreatePriceRequest(
    [Required] Guid ProductId,
    [Required][Range(0, double.MaxValue)] decimal Value,
    [Required][StringLength(10)] string Currency);

public record UpdatePriceRequest(
    [Required][Range(0, double.MaxValue)] decimal Value,
    [Required][StringLength(10)] string Currency);
