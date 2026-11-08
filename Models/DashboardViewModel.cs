namespace Dinari.Models;

public class CategoryExpenseSummary
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal TotalTnd { get; set; }
    public double PercentageOfTotal { get; set; }
    public int TransactionCount { get; set; }
    public decimal ForecastNextMonthTnd { get; set; }
}

public class MonthlyTrendPoint
{
    public string MonthLabel { get; set; } = string.Empty; // ex: "2026-08"
    public decimal TotalExpensesTnd { get; set; }
    public decimal TotalIncomeTnd { get; set; }
    public bool IsForecast { get; set; }
}

public class UnusualExpenseAlert
{
    public int TransactionId { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal AmountTnd { get; set; }
    public decimal CategoryAverageTnd { get; set; }
    public double ZScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class BadHabitInsight
{
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning"; // "danger", "warning", "info"
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedMonthlyImpactTnd { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public class CsvImportResult
{
    public int TotalRowsRead { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicatesCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class DashboardViewModel
{
    public string CurrentMonthLabel { get; set; } = string.Empty;
    public decimal CurrentMonthExpensesTnd { get; set; }
    public decimal CurrentMonthIncomeTnd { get; set; }
    public decimal NetBalanceTnd => CurrentMonthIncomeTnd - CurrentMonthExpensesTnd;
    public decimal SavingsRatePercent => CurrentMonthIncomeTnd > 0
        ? Math.Round(((CurrentMonthIncomeTnd - CurrentMonthExpensesTnd) / CurrentMonthIncomeTnd) * 100m, 1)
        : 0m;

    public decimal ForecastNextMonthTotalTnd { get; set; }
    public double ForecastGrowthPercent { get; set; }
    public string ForecastMethodSummary { get; set; } = string.Empty;

    public List<CategoryExpenseSummary> CategoryBreakdown { get; set; } = new();
    public List<MonthlyTrendPoint> MonthlyTrends { get; set; } = new();
    public List<UnusualExpenseAlert> UnusualExpenses { get; set; } = new();
    public List<BadHabitInsight> BadHabits { get; set; } = new();
    public List<Transaction> RecentTransactions { get; set; } = new();
}

