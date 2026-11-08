using Dinari.Data;
using Dinari.Models;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Services;

public class FinancialAnalyticsService : IFinancialAnalyticsService
{
    private readonly AppDbContext _dbContext;
    private readonly ICategorizationService _categorizationService;

    public FinancialAnalyticsService(AppDbContext dbContext, ICategorizationService categorizationService)
    {
        _dbContext = dbContext;
        _categorizationService = categorizationService;
    }

    public async Task<DashboardViewModel> BuildDashboardAsync(CancellationToken cancellationToken = default)
    {
        var allTransactions = await _dbContext.Transactions
            .AsNoTracking()
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        var vm = new DashboardViewModel();
        if (allTransactions.Count == 0)
        {
            vm.CurrentMonthLabel = DateTime.Today.ToString("yyyy-MM");
            return vm;
        }

        // Mois de référence = le mois le plus récent présent dans les données
        DateTime latestDate = allTransactions.Max(t => t.Date);
        int refYear = latestDate.Year;
        int refMonth = latestDate.Month;
        vm.CurrentMonthLabel = $"{refYear:D4}-{refMonth:D2}";

        var currentMonthTx = allTransactions
            .Where(t => t.Date.Year == refYear && t.Date.Month == refMonth)
            .ToList();

        vm.CurrentMonthExpensesTnd = currentMonthTx
            .Where(t => t.Type == TransactionType.Expense)
            .Sum(t => t.AmountTnd);

        vm.CurrentMonthIncomeTnd = currentMonthTx
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.AmountTnd);

        vm.RecentTransactions = allTransactions.Take(12).ToList();

        // 1. Agrégation mensuelle historique
        var monthlyGroups = allTransactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyTrendPoint
            {
                MonthLabel = $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                TotalExpensesTnd = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd),
                TotalIncomeTnd = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd),
                IsForecast = false
            })
            .ToList();

        // 2. Prévision du mois suivant par Régression Linéaire (Moindres Carrés Ordinaires)
        var expenseHistory = monthlyGroups.Select(m => (double)m.TotalExpensesTnd).ToList();
        decimal nextMonthForecast = ComputeLinearRegressionForecast(expenseHistory);
        vm.ForecastNextMonthTotalTnd = nextMonthForecast;

        if (vm.CurrentMonthExpensesTnd > 0)
        {
            vm.ForecastGrowthPercent = Math.Round(
                (double)((nextMonthForecast - vm.CurrentMonthExpensesTnd) / vm.CurrentMonthExpensesTnd) * 100.0, 1);
        }

        vm.ForecastMethodSummary = monthlyGroups.Count >= 3
            ? $"Régression linéaire OLS combinée à une moyenne pondérée sur {monthlyGroups.Count} mois."
            : "Moyenne mobile sur les mois disponibles (ajoutez au moins 3 mois pour affiner la régression).";

        DateTime nextMonthDate = new DateTime(refYear, refMonth, 1).AddMonths(1);
        monthlyGroups.Add(new MonthlyTrendPoint
        {
            MonthLabel = $"{nextMonthDate:yyyy-MM} (Prév.)",
            TotalExpensesTnd = nextMonthForecast,
            TotalIncomeTnd = monthlyGroups.LastOrDefault()?.TotalIncomeTnd ?? 0m,
            IsForecast = true
        });
        vm.MonthlyTrends = monthlyGroups;

        // 3. Répartition par catégorie sur le mois de référence + prévision par catégorie
        var allExpenses = allTransactions.Where(t => t.Type == TransactionType.Expense).ToList();
        int distinctMonthsCount = Math.Max(1, allExpenses.Select(t => $"{t.Date.Year}-{t.Date.Month}").Distinct().Count());

        var currentMonthExpenses = currentMonthTx.Where(t => t.Type == TransactionType.Expense).ToList();
        decimal totalRefExpense = Math.Max(0.001m, vm.CurrentMonthExpensesTnd);

        vm.CategoryBreakdown = allExpenses
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                decimal currentCatTotal = currentMonthExpenses
                    .Where(c => c.Category == g.Key)
                    .Sum(c => c.AmountTnd);

                decimal historicalMonthlyAvg = g.Sum(x => x.AmountTnd) / distinctMonthsCount;
                // Pondération : 60 % mois courant + 40 % moyenne historique
                decimal catForecast = Math.Round((currentCatTotal * 0.6m) + (historicalMonthlyAvg * 0.4m), 3);

                return new CategoryExpenseSummary
                {
                    CategoryName = _categorizationService.GetCategoryDisplayName(g.Key),
                    TotalTnd = currentCatTotal,
                    PercentageOfTotal = vm.CurrentMonthExpensesTnd > 0
                        ? Math.Round((double)(currentCatTotal / totalRefExpense) * 100.0, 1)
                        : 0.0,
                    TransactionCount = currentMonthExpenses.Count(c => c.Category == g.Key),
                    ForecastNextMonthTnd = catForecast
                };
            })
            .OrderByDescending(c => c.TotalTnd)
            .ToList();

        // 4. Détection des dépenses inhabituelles (Z-Score par catégorie)
        vm.UnusualExpenses = DetectUnusualExpenses(allExpenses);

        // 5. Détection des mauvaises habitudes financières
        vm.BadHabits = DetectBadHabits(allExpenses, currentMonthExpenses, distinctMonthsCount);

        return vm;
    }

    private static decimal ComputeLinearRegressionForecast(List<double> monthlyTotals)
    {
        int n = monthlyTotals.Count;
        if (n == 0) return 0m;
        if (n == 1) return Math.Round((decimal)monthlyTotals[0], 3);

        // Indices x = 1, 2, ..., n
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            double x = i + 1;
            double y = monthlyTotals[i];
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denominator = (n * sumX2) - (sumX * sumX);
        double olsForecast = denominator != 0
            ? ((sumY * sumX2 - sumX * sumXY) / denominator) + (((n * sumXY - sumX * sumY) / denominator) * (n + 1))
            : monthlyTotals.Average();

        // Lissage avec la moyenne mobile pondérée pour éviter les extrapolations extrêmes
        double recentWeighted = (monthlyTotals[n - 1] * 0.6) + (monthlyTotals.Average() * 0.4);
        double blended = (olsForecast * 0.5) + (recentWeighted * 0.5);

        return Math.Max(0m, Math.Round((decimal)blended, 3));
    }

    private List<UnusualExpenseAlert> DetectUnusualExpenses(List<Transaction> expenses)
    {
        var alerts = new List<UnusualExpenseAlert>();

        foreach (var group in expenses.GroupBy(e => e.Category))
        {
            var items = group.ToList();
            if (items.Count < 4) continue;

            double mean = items.Average(i => (double)i.AmountTnd);
            double variance = items.Sum(i => Math.Pow((double)i.AmountTnd - mean, 2)) / items.Count;
            double stdDev = Math.Sqrt(variance);

            if (stdDev < 1.0) continue;

            foreach (var tx in items)
            {
                double zScore = ((double)tx.AmountTnd - mean) / stdDev;
                decimal diff = tx.AmountTnd - (decimal)mean;

                // Seuil statistique : Z-Score >= 1.85 et écart d'au moins 25 TND au-dessus de la moyenne de sa catégorie
                if (zScore >= 1.85 && diff >= 25.000m)
                {
                    alerts.Add(new UnusualExpenseAlert
                    {
                        TransactionId = tx.Id,
                        Date = tx.Date,
                        Description = tx.Description,
                        CategoryName = _categorizationService.GetCategoryDisplayName(tx.Category),
                        AmountTnd = tx.AmountTnd,
                        CategoryAverageTnd = Math.Round((decimal)mean, 3),
                        ZScore = Math.Round(zScore, 2),
                        Reason = $"Dépasse de {diff:0.000} TND la moyenne habituelle ({mean:0.000} TND) de cette catégorie (Z = {zScore:0.00})."
                    });
                }
            }
        }

        return alerts.OrderByDescending(a => a.ZScore).Take(8).ToList();
    }

    private static List<BadHabitInsight> DetectBadHabits(
        List<Transaction> allExpenses,
        List<Transaction> currentMonthExpenses,
        int distinctMonthsCount)
    {
        var insights = new List<BadHabitInsight>();
        decimal currentTotal = currentMonthExpenses.Sum(e => e.AmountTnd);
        if (currentTotal <= 0) return insights;

        // Habitude 1 : Fréquence élevée en Cafés, Restaurants & Fast-Food
        var diningTx = currentMonthExpenses.Where(e => e.Category == TransactionCategory.DiningOut).ToList();
        decimal diningTotal = diningTx.Sum(e => e.AmountTnd);
        decimal diningShare = (diningTotal / currentTotal) * 100m;

        if (diningTx.Count >= 8 || diningShare >= 15m)
        {
            insights.Add(new BadHabitInsight
            {
                Title = "Cumul élevé de cafés, restaurants & livraisons",
                Severity = diningShare >= 22m ? "danger" : "warning",
                Description = $"Vous avez effectué {diningTx.Count} dépenses de restauration ce mois-ci pour un total de {diningTotal:0.000} TND ({diningShare:0.0}% de vos dépenses).",
                EstimatedMonthlyImpactTnd = Math.Round(diningTotal * 0.40m, 3),
                Recommendation = "Réduire de 40 % les sorties/livraisons (ex. préparer le café ou déjeuner maison 2 jours de plus par semaine) libérerait immédiatement cette épargne."
            });
        }

        // Habitude 2 : Surchauffe de début de mois (hors loyer et factures fixes)
        var variableExpenses = currentMonthExpenses
            .Where(e => e.Category != TransactionCategory.UtilitiesAndRent)
            .ToList();
        decimal variableTotal = variableExpenses.Sum(e => e.AmountTnd);
        if (variableTotal > 0)
        {
            decimal firstTenDaysTotal = variableExpenses
                .Where(e => e.Date.Day <= 10)
                .Sum(e => e.AmountTnd);
            decimal firstTenDaysShare = (firstTenDaysTotal / variableTotal) * 100m;

            if (firstTenDaysShare >= 55m)
            {
                insights.Add(new BadHabitInsight
                {
                    Title = "Concentration des dépenses en début de mois (Effet Jour de Paie)",
                    Severity = "warning",
                    Description = $"{firstTenDaysShare:0.0}% de vos dépenses variables ({firstTenDaysTotal:0.000} TND) sont consommées dès les 10 premiers jours du mois.",
                    EstimatedMonthlyImpactTnd = Math.Round(firstTenDaysTotal * 0.25m, 3),
                    Recommendation = "Instaurez un plafond hebdomadaire en TND dès la réception du salaire ou virez votre épargne dès le 1er du mois."
                });
            }
        }

        // Habitude 3 : Dérive des achats Shopping & Loisirs par rapport à la moyenne historique
        decimal currentShopping = currentMonthExpenses
            .Where(e => e.Category == TransactionCategory.ShoppingAndEntertainment)
            .Sum(e => e.AmountTnd);

        decimal avgHistoricalShopping = allExpenses
            .Where(e => e.Category == TransactionCategory.ShoppingAndEntertainment)
            .Sum(e => e.AmountTnd) / distinctMonthsCount;

        if (avgHistoricalShopping > 0 && currentShopping > avgHistoricalShopping * 1.25m)
        {
            decimal excess = currentShopping - avgHistoricalShopping;
            insights.Add(new BadHabitInsight
            {
                Title = "Hausse inhabituelle du poste Shopping & Loisirs",
                Severity = "danger",
                Description = $"Vos achats Shopping & Loisirs atteignent {currentShopping:0.000} TND ce mois-ci contre une moyenne mensuelle de {avgHistoricalShopping:0.000} TND (+{((currentShopping - avgHistoricalShopping) / avgHistoricalShopping) * 100m:0.0}%).",
                EstimatedMonthlyImpactTnd = Math.Round(excess, 3),
                Recommendation = "Appliquez la règle d'attente de 48 heures avant tout achat non essentiel supérieur à 50.000 TND."
            });
        }

        if (insights.Count == 0)
        {
            insights.Add(new BadHabitInsight
            {
                Title = "Rythme budgétaire maîtrisé",
                Severity = "info",
                Description = "Aucune dérive comportementale majeure (micro-dépenses excessives ou surchauffe de début de mois) n'a été détectée sur le mois en cours.",
                EstimatedMonthlyImpactTnd = 0m,
                Recommendation = "Continuez à catégoriser vos imports CSV chaque mois pour affiner vos prévisions."
            });
        }

        return insights;
    }
}

