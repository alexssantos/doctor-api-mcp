using McpApis.PrecoAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace McpApis.PrecoAPI.Data;

public class PriceDbContext : DbContext
{
    public PriceDbContext(DbContextOptions<PriceDbContext> options) : base(options) { }

    public DbSet<Price> Prices => Set<Price>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Price>(entity =>
        {
            entity.ToTable("prices");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.ProductId).HasColumnName("product_id");
            entity.Property(p => p.Value).HasColumnName("value").HasColumnType("numeric(10,2)");
            entity.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(10);
            entity.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(p => p.ProductId).IsUnique();
        });
    }
}
