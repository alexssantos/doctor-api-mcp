namespace McpApis.PrecoAPI.Models;

public class Price
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public decimal Value { get; set; }
    public string Currency { get; set; } = "BRL";
    public DateTime UpdatedAt { get; set; }
}
