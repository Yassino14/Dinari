using System.Text;
using Dinari.Data;
using Dinari.Models;
using Dinari.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dinari.Tests;

public class CsvImportServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ImportFromStreamAsync_ShouldImportValidCsvAndDeduplicateOnSecondImport()
    {
        using var context = CreateInMemoryDbContext();
        var catService = new CategorizationService();
        var importService = new CsvImportService(context, catService);

        string csvContent = "Date;Description;Montant;Type\n" +
                            "10/11/2026;Aziza Supermarché Courses;-64,850;Depense\n" +
                            "11/11/2026;Station OLA Energy Carburant;-75,000;Depense\n" +
                            "15/11/2026;Prime Projet Freelance;450,000;Revenu\n";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var result1 = await importService.ImportFromStreamAsync(stream1);

        Assert.Equal(3, result1.TotalRowsRead);
        Assert.Equal(3, result1.ImportedCount);
        Assert.Equal(0, result1.SkippedDuplicatesCount);
        Assert.Equal(3, await context.Transactions.CountAsync());

        // Second import of the identical stream to verify SHA-256 deduplication
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var result2 = await importService.ImportFromStreamAsync(stream2);

        Assert.Equal(3, result2.TotalRowsRead);
        Assert.Equal(0, result2.ImportedCount);
        Assert.Equal(3, result2.SkippedDuplicatesCount);
        Assert.Equal(3, await context.Transactions.CountAsync());
    }

    [Fact]
    public void ComputeTransactionHash_GeneratesConsistentSha256()
    {
        using var context = CreateInMemoryDbContext();
        var catService = new CategorizationService();
        var importService = new CsvImportService(context, catService);

        var date = new DateTime(2026, 11, 10);
        string hash1 = importService.ComputeTransactionHash(date, "Aziza Supermarché", 64.850m, TransactionType.Expense);
        string hash2 = importService.ComputeTransactionHash(date, "  aziza supermarché  ", 64.850m, TransactionType.Expense);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }
}
