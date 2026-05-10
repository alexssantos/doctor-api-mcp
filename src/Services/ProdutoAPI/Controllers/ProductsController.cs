using McpApis.ProdutoAPI.Data;
using McpApis.ProdutoAPI.Dtos;
using McpApis.ProdutoAPI.Integration.PrecoApi;
using McpApis.ProdutoAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace McpApis.ProdutoAPI.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly ProductDbContext _db;
    private readonly PriceClient _priceClient;

    public ProductsController(ProductDbContext db, PriceClient priceClient)
    {
        _db = db;
        _priceClient = priceClient;
    }

    /// <summary>Returns all products enriched with their current prices.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var products = await _db.Products.AsNoTracking().ToListAsync(ct);

        var tasks = products.Select(async p =>
        {
            var price = await _priceClient.GetPriceAsync(p.Id, ct);
            return ToResponse(p, price);
        });

        var responses = await Task.WhenAll(tasks);
        return Ok(responses);
    }

    /// <summary>Returns a single product with its current price.</summary>
    /// <param name="id">Product unique identifier.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
            return NotFound();

        var price = await _priceClient.GetPriceAsync(product.Id, ct);
        return Ok(ToResponse(product, price));
    }

    /// <summary>Creates a new product.</summary>
    /// <remarks>
    /// Example:
    ///
    ///     POST /api/products
    ///     {
    ///         "name": "Notebook Dell",
    ///         "description": "Notebook i7 16GB RAM"
    ///     }
    ///
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToResponse(product, null));
    }

    /// <summary>Updates a product's details.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
            return NotFound();

        product.Name = request.Name;
        product.Description = request.Description;

        await _db.SaveChangesAsync();

        return Ok(ToResponse(product, null));
    }

    /// <summary>Removes a product.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
            return NotFound();

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static ProductResponse ToResponse(
        Product product,
        McpApis.ProdutoAPI.Integration.PrecoApi.Contracts.PriceResponse? price) =>
        new(
            product.Id,
            product.Name,
            product.Description,
            price is not null ? new PriceDto(price.Value, price.Currency) : null);
}
