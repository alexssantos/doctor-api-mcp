using McpApis.ProdutoAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace McpApis.ProdutoAPI.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(p => p.CreatedAt).HasColumnName("created_at");
        });
    }
}
