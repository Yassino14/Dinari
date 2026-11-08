using Dinari.Models;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.AmountTnd).HasPrecision(18, 3);
            entity.HasIndex(t => t.Date);
            entity.HasIndex(t => t.Category);
            entity.HasIndex(t => t.ExternalHash).IsUnique();
        });
    }
}

