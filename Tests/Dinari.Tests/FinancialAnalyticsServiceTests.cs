using Dinari.Data;
using Dinari.Models;
using Dinari.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dinari.Tests;

public class FinancialAnalyticsServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task BuildDashboardAsync_DetectsZScoreOutlier()
    {
        using var context = CreateInMemoryDbContext();
        var catService = new CategorizationService();
        var analyticsService = new FinancialAnalyticsService(context, catService);

        // 4 normal transport expenses around 20 TND + 1 huge anomaly of 300 TND
        context.Transactions.AddRange(
            new Transaction { Date = new DateTime(2026, 11, 1), Description = "Taxi 1", AmountTnd = 20.000m, Type = TransactionType.Expense, Category = TransactionCategory.Transport, ExternalHash = "h1" },
            new Transaction { Date = new DateTime(2026, 11, 2), Description = "Taxi 2", AmountTnd = 22.000m, Type = TransactionType.Expense, Category = TransactionCategory.Transport, ExternalHash = "h2" },
            new Transaction { Date = new DateTime(2026, 11, 3), Description = "Taxi 3", AmountTnd = 19.500m, Type = TransactionType.Expense, Category = TransactionCategory.Transport, ExternalHash = "h3" },
            new Transaction { Date = new DateTime(2026, 11, 4), Description = "Taxi 4", AmountTnd = 21.000m, Type = TransactionType.Expense, Category = TransactionCategory.Transport, ExternalHash = "h4" },
            new Transaction { Date = new DateTime(2026, 11, 5), Description = "Grosse Réparation Garage", AmountTnd = 320.000m, Type = TransactionType.Expense, Category = TransactionCategory.Transport, ExternalHash = "h5" }
        );
        await context.SaveChangesAsync();

        var dashboard = await analyticsService.BuildDashboardAsync();

        Assert.Single(dashboard.UnusualExpenses);
        Assert.Equal("Grosse Réparation Garage", dashboard.UnusualExpenses[0].Description);
        Assert.True(dashboard.UnusualExpenses[0].ZScore >= 1.85);
    }

    [Fact]
    public async Task BuildDashboardAsync_ForecastsNextMonthUsingHistoricalMonths()
    {
        using var context = CreateInMemoryDbContext();
        var catService = new CategorizationService();
        var analyticsService = new FinancialAnalyticsService(context, catService);

        context.Transactions.AddRange(
            new Transaction { Date = new DateTime(2026, 8, 15), Description = "Loyer", AmountTnd = 500.000m, Type = TransactionType.Expense, Category = TransactionCategory.UtilitiesAndRent, ExternalHash = "m8" },
            new Transaction { Date = new DateTime(2026, 9, 15), Description = "Loyer", AmountTnd = 520.000m, Type = TransactionType.Expense, Category = TransactionCategory.UtilitiesAndRent, ExternalHash = "m9" },
            new Transaction { Date = new DateTime(2026, 10, 15), Description = "Loyer", AmountTnd = 540.000m, Type = TransactionType.Expense, Category = TransactionCategory.UtilitiesAndRent, ExternalHash = "m10" }
        );
        await context.SaveChangesAsync();

        var dashboard = await analyticsService.BuildDashboardAsync();

        Assert.True(dashboard.ForecastNextMonthTotalTnd > 500.000m);
        Assert.Equal(4, dashboard.MonthlyTrends.Count); // 3 historical + 1 forecast point
        Assert.True(dashboard.MonthlyTrends.Last().IsForecast);
    }
}
