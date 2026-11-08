namespace Dinari.Models;

public class CategoryExpenseSummary
{
    public TransactionCategory Category { get; set; }
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
    public decimal NetSavingsTnd => TotalIncomeTnd - TotalExpensesTnd;
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
    public string SourceLabel { get; set; } = string.Empty;
}

public class BadHabitInsight
{
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning"; // "danger", "warning", "info", "success"
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedMonthlyImpactTnd { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public class CsvImportResult
{
    public string FileName { get; set; } = string.Empty;
    public int? BatchId { get; set; }
    public int TotalRowsRead { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicatesCount { get; set; }
    public string InterpretationSummary { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public List<CsvImportBatch> ImportHistory { get; set; } = new();
}

public class JournalEntryItem
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public TransactionCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public decimal DebitExpenseTnd => Type == TransactionType.Expense ? AmountTnd : 0m;
    public decimal CreditIncomeTnd => Type == TransactionType.Income ? AmountTnd : 0m;
    public decimal AmountTnd { get; set; }
    public decimal RunningBalanceTnd { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public bool IsAutoCategorized { get; set; }
}

public class JournalViewModel
{
    public string? SelectedMonth { get; set; }
    public TransactionType? SelectedType { get; set; }
    public TransactionCategory? SelectedCategory { get; set; }
    public int? SelectedBatchId { get; set; }
    public string? SearchQuery { get; set; }

    public List<string> AvailableMonths { get; set; } = new();
    public List<CsvImportBatch> ImportBatches { get; set; } = new();

    public decimal OpeningBalanceTnd { get; set; }
    public decimal TotalCreditIncomeTnd { get; set; }
    public decimal TotalDebitExpenseTnd { get; set; }
    public decimal NetPeriodFlowTnd => TotalCreditIncomeTnd - TotalDebitExpenseTnd;
    public decimal ClosingBalanceTnd { get; set; }
    public int TotalEntriesCount { get; set; }

    public List<JournalEntryItem> Entries { get; set; } = new();
}

public class BudgetRuleBucket
{
    public string Name { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public double TargetPercent { get; set; }
    public double ActualPercent { get; set; }
    public decimal TargetAmountTnd { get; set; }
    public decimal ActualAmountTnd { get; set; }
    public decimal VarianceTnd => ActualAmountTnd - TargetAmountTnd;
    public string StatusBadge { get; set; } = "success";
    public string Advice { get; set; } = string.Empty;
}

public class SavingsAdviceTip
{
    public string Icon { get; set; } = "bi-piggy-bank";
    public string CategoryTag { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public decimal PotentialMonthlySavingsTnd { get; set; }
    public decimal PotentialAnnualSavingsTnd => PotentialMonthlySavingsTnd * 12m;
    public string Priority { get; set; } = "Haute";
}

public class SavingsGoalProjection
{
    public string GoalTitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-bullseye";
    public decimal TargetAmountTnd { get; set; }
    public int MonthsWithCurrentSavings { get; set; }
    public int MonthsWithOptimizedSavings { get; set; }
    public int MonthsSaved => Math.Max(0, MonthsWithCurrentSavings - MonthsWithOptimizedSavings);
}

public class BilanEpargneViewModel
{
    public int TotalMonthsAnalyzed { get; set; }
    public int TotalTransactionsCount { get; set; }
    public decimal CumulativeIncomeTnd { get; set; }
    public decimal CumulativeExpensesTnd { get; set; }
    public decimal CumulativeNetSavingsTnd => CumulativeIncomeTnd - CumulativeExpensesTnd;
    public decimal GlobalSavingsRatePercent => CumulativeIncomeTnd > 0
        ? Math.Round(((CumulativeIncomeTnd - CumulativeExpensesTnd) / CumulativeIncomeTnd) * 100m, 1)
        : 0m;

    public decimal AverageMonthlyIncomeTnd { get; set; }
    public decimal AverageMonthlyExpensesTnd { get; set; }
    public decimal AverageMonthlySavingsTnd => AverageMonthlyIncomeTnd - AverageMonthlyExpensesTnd;

    public string BestSavingsMonthLabel { get; set; } = string.Empty;
    public decimal BestSavingsMonthAmountTnd { get; set; }
    public string HighestExpenseMonthLabel { get; set; } = string.Empty;
    public decimal HighestExpenseMonthAmountTnd { get; set; }

    public int FinancialHealthScore { get; set; }
    public string FinancialHealthLabel { get; set; } = string.Empty;
    public string FinancialHealthSummary { get; set; } = string.Empty;

    public decimal EssentialMonthlyAvgTnd { get; set; }
    public decimal EmergencyFund3MonthsTnd => Math.Round(EssentialMonthlyAvgTnd * 3m, 3);
    public decimal EmergencyFund6MonthsTnd => Math.Round(EssentialMonthlyAvgTnd * 6m, 3);
    public double EmergencyFundProgressPercent => EmergencyFund3MonthsTnd > 0
        ? Math.Min(100.0, Math.Max(0.0, Math.Round((double)(CumulativeNetSavingsTnd / EmergencyFund3MonthsTnd) * 100.0, 1)))
        : 0.0;

    public decimal PotentialExtraMonthlySavingsTnd { get; set; }
    public decimal OptimizedMonthlySavingsTnd => Math.Max(0m, AverageMonthlySavingsTnd) + PotentialExtraMonthlySavingsTnd;

    public List<BudgetRuleBucket> Rule503020 { get; set; } = new();
    public List<SavingsAdviceTip> SavingsAdvices { get; set; } = new();
    public List<SavingsGoalProjection> GoalProjections { get; set; } = new();
    public List<MonthlyTrendPoint> MonthlyBilanHistory { get; set; } = new();
}

public class DashboardViewModel
{
    public string CurrentMonthLabel { get; set; } = string.Empty;
    public List<string> AvailableMonths { get; set; } = new();
    public int? SelectedBatchId { get; set; }
    public string? SelectedBatchName { get; set; }
    public List<CsvImportBatch> ImportBatches { get; set; } = new();

    public int TotalProtectedTransactionsCount { get; set; }
    public decimal CumulativeNetWorthTnd { get; set; }
    public int FinancialHealthScore { get; set; }
    public string FinancialHealthLabel { get; set; } = string.Empty;

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
