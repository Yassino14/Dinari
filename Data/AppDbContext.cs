using Dinari.Models;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CsvImportBatch> CsvImportBatches => Set<CsvImportBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.AmountTnd).HasPrecision(18, 3);
            entity.HasIndex(t => t.Date);
            entity.HasIndex(t => t.Category);
            entity.HasIndex(t => t.ImportBatchId);
            entity.HasIndex(t => t.ExternalHash).IsUnique();
        });

        modelBuilder.Entity<CsvImportBatch>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.TotalIncomeTnd).HasPrecision(18, 3);
            entity.Property(b => b.TotalExpensesTnd).HasPrecision(18, 3);
            entity.HasIndex(b => b.ImportedAtUtc);
        });
    }
}
