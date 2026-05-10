using McpApis.PrecoAPI.Contracts;
using McpApis.PrecoAPI.Data;
using McpApis.PrecoAPI.Dtos;
using McpApis.PrecoAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace McpApis.PrecoAPI.Controllers;

[ApiController]
[Route("api/prices")]
[Produces("application/json")]
public class PricesController : ControllerBase
{
    private readonly PriceDbContext _db;

    public PricesController(PriceDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the price for a given product.</summary>
    /// <param name="productId">Product unique identifier.</param>
    [HttpGet("{productId:guid}")]
    [ProducesResponseType(typeof(PriceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByProductId(Guid productId)
    {
        var price = await _db.Prices
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId);

        if (price is null)
            return NotFound();

        return Ok(new PriceResponse(price.ProductId, price.Value, price.Currency));
    }

    /// <summary>Creates a price entry for a product.</summary>
    /// <remarks>
    /// Example:
    ///
    ///     POST /api/prices
    ///     {
    ///         "productId": "b3f1c1e2-1234-4b3f-a111-111111111111",
    ///         "value": 5500.50,
    ///         "currency": "BRL"
    ///     }
    ///
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(PriceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePriceRequest request)
    {
        var existing = await _db.Prices.AnyAsync(p => p.ProductId == request.ProductId);
        if (existing)
            return Conflict(new { message = "Price for this product already exists." });

        var price = new Price
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            Value = request.Value,
            Currency = request.Currency,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Prices.Add(price);
        await _db.SaveChangesAsync();

        var response = new PriceResponse(price.ProductId, price.Value, price.Currency);
        return CreatedAtAction(nameof(GetByProductId), new { productId = price.ProductId }, response);
    }

    /// <summary>Updates the price for a product.</summary>
    [HttpPut("{productId:guid}")]
    [ProducesResponseType(typeof(PriceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid productId, [FromBody] UpdatePriceRequest request)
    {
        var price = await _db.Prices.FirstOrDefaultAsync(p => p.ProductId == productId);

        if (price is null)
            return NotFound();

        price.Value = request.Value;
        price.Currency = request.Currency;
        price.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new PriceResponse(price.ProductId, price.Value, price.Currency));
    }

    /// <summary>Removes the price for a product.</summary>
    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid productId)
    {
        var price = await _db.Prices.FirstOrDefaultAsync(p => p.ProductId == productId);

        if (price is null)
            return NotFound();

        _db.Prices.Remove(price);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
