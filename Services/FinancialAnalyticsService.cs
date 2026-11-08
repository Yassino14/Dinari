using Dinari.Data;
using Dinari.Models;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Services;

public class FinancialAnalyticsService : IFinancialAnalyticsService
{
    private readonly AppDbContext _dbContext;
    private readonly ICategorizationService _categorizationService;

    private static readonly HashSet<TransactionCategory> EssentialCategories = new()
    {
        TransactionCategory.UtilitiesAndRent,
        TransactionCategory.Groceries,
        TransactionCategory.Transport,
        TransactionCategory.Health
    };

    private static readonly HashSet<TransactionCategory> LifestyleCategories = new()
    {
        TransactionCategory.DiningOut,
        TransactionCategory.ShoppingAndEntertainment,
        TransactionCategory.Subscriptions,
        TransactionCategory.Other
    };

    public FinancialAnalyticsService(AppDbContext dbContext, ICategorizationService categorizationService)
    {
        _dbContext = dbContext;
        _categorizationService = categorizationService;
    }

    public async Task<DashboardViewModel> BuildDashboardAsync(
        CancellationToken cancellationToken = default,
        string? selectedMonth = null,
        int? batchId = null)
    {
        var allDatabaseTransactions = await _dbContext.Transactions
            .AsNoTracking()
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        var batches = await _dbContext.CsvImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.ImportedAtUtc)
            .ThenByDescending(b => b.Id)
            .ToListAsync(cancellationToken);

        var vm = new DashboardViewModel
        {
            ImportBatches = batches,
            SelectedBatchId = batchId,
            TotalProtectedTransactionsCount = allDatabaseTransactions.Count,
            CumulativeNetWorthTnd = allDatabaseTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd)
                                  - allDatabaseTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd)
        };

        if (batchId.HasValue)
        {
            var matchedBatch = batches.FirstOrDefault(b => b.Id == batchId.Value);
            vm.SelectedBatchName = matchedBatch?.FileName;
        }

        var scopedTransactions = batchId.HasValue
            ? allDatabaseTransactions.Where(t => t.ImportBatchId == batchId.Value).ToList()
            : allDatabaseTransactions;

        if (scopedTransactions.Count == 0)
        {
            vm.CurrentMonthLabel = DateTime.Today.ToString("yyyy-MM");
            vm.FinancialHealthScore = 50;
            vm.FinancialHealthLabel = "En attente de données";
            return vm;
        }

        // Liste de tous les mois disponibles (du plus récent au plus ancien)
        vm.AvailableMonths = scopedTransactions
            .Select(t => $"{t.Date.Year:D4}-{t.Date.Month:D2}")
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        // Détermination du mois de référence (soit choisi par l'utilisateur, soit le plus récent)
        int refYear;
        int refMonth;
        if (!string.IsNullOrWhiteSpace(selectedMonth) &&
            vm.AvailableMonths.Contains(selectedMonth.Trim()) &&
            TryParseYearMonth(selectedMonth.Trim(), out refYear, out refMonth))
        {
            vm.CurrentMonthLabel = $"{refYear:D4}-{refMonth:D2}";
        }
        else
        {
            DateTime latestDate = scopedTransactions.Max(t => t.Date);
            refYear = latestDate.Year;
            refMonth = latestDate.Month;
            vm.CurrentMonthLabel = $"{refYear:D4}-{refMonth:D2}";
        }

        var currentMonthTx = scopedTransactions
            .Where(t => t.Date.Year == refYear && t.Date.Month == refMonth)
            .ToList();

        vm.CurrentMonthExpensesTnd = currentMonthTx
            .Where(t => t.Type == TransactionType.Expense)
            .Sum(t => t.AmountTnd);

        vm.CurrentMonthIncomeTnd = currentMonthTx
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.AmountTnd);

        vm.RecentTransactions = currentMonthTx.Take(10).ToList();

        // 1. Agrégation mensuelle historique
        var monthlyGroups = scopedTransactions
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

        // 2. Prévision du mois suivant par Régression Linéaire OLS
        var expenseHistory = monthlyGroups.Select(m => (double)m.TotalExpensesTnd).ToList();
        decimal nextMonthForecast = ComputeLinearRegressionForecast(expenseHistory);
        vm.ForecastNextMonthTotalTnd = nextMonthForecast;

        if (vm.CurrentMonthExpensesTnd > 0)
        {
            vm.ForecastGrowthPercent = Math.Round(
                (double)((nextMonthForecast - vm.CurrentMonthExpensesTnd) / vm.CurrentMonthExpensesTnd) * 100.0, 1);
        }

        vm.ForecastMethodSummary = monthlyGroups.Count >= 3
            ? $"Régression linéaire OLS combinée à une moyenne pondérée sur {monthlyGroups.Count} mois protégés."
            : "Moyenne mobile sur les mois disponibles (ajoutez au moins 3 mois pour affiner la régression).";

        DateTime latestMonthInScope = scopedTransactions.Max(t => t.Date);
        DateTime nextMonthDate = new DateTime(latestMonthInScope.Year, latestMonthInScope.Month, 1).AddMonths(1);
        monthlyGroups.Add(new MonthlyTrendPoint
        {
            MonthLabel = $"{nextMonthDate:yyyy-MM} (Prév.)",
            TotalExpensesTnd = nextMonthForecast,
            TotalIncomeTnd = monthlyGroups.LastOrDefault()?.TotalIncomeTnd ?? 0m,
            IsForecast = true
        });
        vm.MonthlyTrends = monthlyGroups;

        // 3. Répartition par catégorie sur le mois de référence + prévision par catégorie
        var allExpenses = allDatabaseTransactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var scopedExpenses = scopedTransactions.Where(t => t.Type == TransactionType.Expense).ToList();
        int distinctMonthsCount = Math.Max(1, scopedExpenses.Select(t => $"{t.Date.Year}-{t.Date.Month}").Distinct().Count());

        var currentMonthExpenses = currentMonthTx.Where(t => t.Type == TransactionType.Expense).ToList();
        decimal totalRefExpense = Math.Max(0.001m, vm.CurrentMonthExpensesTnd);

        vm.CategoryBreakdown = scopedExpenses
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                decimal currentCatTotal = currentMonthExpenses
                    .Where(c => c.Category == g.Key)
                    .Sum(c => c.AmountTnd);

                decimal historicalMonthlyAvg = g.Sum(x => x.AmountTnd) / distinctMonthsCount;
                decimal catForecast = Math.Round((currentCatTotal * 0.6m) + (historicalMonthlyAvg * 0.4m), 3);

                return new CategoryExpenseSummary
                {
                    Category = g.Key,
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

        // 4. Détection des dépenses inhabituelles (Z-Score calculé sur tout le socle historique pour protéger la référence statistique)
        var allAnomalies = DetectUnusualExpenses(allExpenses);
        vm.UnusualExpenses = batchId.HasValue
            ? allAnomalies.Where(a => scopedExpenses.Any(s => s.Id == a.TransactionId)).ToList()
            : allAnomalies;

        // 5. Détection des mauvaises habitudes financières sur le mois sélectionné
        vm.BadHabits = DetectBadHabits(allExpenses, currentMonthExpenses, Math.Max(1, allExpenses.Select(t => $"{t.Date.Year}-{t.Date.Month}").Distinct().Count()));

        // 6. Score de santé financière
        var (score, label, _) = ComputeHealthScore(vm.CurrentMonthIncomeTnd, vm.CurrentMonthExpensesTnd, vm.BadHabits.Count(b => b.Severity != "info"));
        vm.FinancialHealthScore = score;
        vm.FinancialHealthLabel = label;

        return vm;
    }

    public async Task<JournalViewModel> BuildJournalAsync(
        string? month = null,
        TransactionType? type = null,
        TransactionCategory? category = null,
        int? batchId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var allTxChronological = await _dbContext.Transactions
            .AsNoTracking()
            .OrderBy(t => t.Date)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var batches = await _dbContext.CsvImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.ImportedAtUtc)
            .ToListAsync(cancellationToken);

        var availableMonths = allTxChronological
            .Select(t => $"{t.Date.Year:D4}-{t.Date.Month:D2}")
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        // Calcul du solde progressif (Running Balance) sur l'intégralité du livre-journal
        decimal runningBalance = 0m;
        var fullLedger = new List<JournalEntryItem>(allTxChronological.Count);

        foreach (var tx in allTxChronological)
        {
            if (tx.Type == TransactionType.Income)
            {
                runningBalance += tx.AmountTnd;
            }
            else
            {
                runningBalance -= tx.AmountTnd;
            }

            fullLedger.Add(new JournalEntryItem
            {
                Id = tx.Id,
                Date = tx.Date,
                Description = tx.Description,
                Category = tx.Category,
                CategoryName = _categorizationService.GetCategoryDisplayName(tx.Category),
                Type = tx.Type,
                AmountTnd = tx.AmountTnd,
                RunningBalanceTnd = runningBalance,
                SourceLabel = string.IsNullOrWhiteSpace(tx.SourceLabel) ? "Socle Initial" : tx.SourceLabel,
                IsAutoCategorized = tx.IsAutoCategorized
            });
        }

        IEnumerable<JournalEntryItem> filtered = fullLedger;

        decimal openingBalance = 0m;
        if (!string.IsNullOrWhiteSpace(month) && TryParseYearMonth(month.Trim(), out int y, out int m))
        {
            var startOfMonth = new DateTime(y, m, 1);
            var lastBeforeMonth = fullLedger.LastOrDefault(e => e.Date < startOfMonth);
            openingBalance = lastBeforeMonth?.RunningBalanceTnd ?? 0m;

            filtered = filtered.Where(e => e.Date.Year == y && e.Date.Month == m);
        }

        if (batchId.HasValue)
        {
            var batchTxIds = allTxChronological
                .Where(t => t.ImportBatchId == batchId.Value)
                .Select(t => t.Id)
                .ToHashSet();
            filtered = filtered.Where(e => batchTxIds.Contains(e.Id));
        }

        if (type.HasValue)
        {
            filtered = filtered.Where(e => e.Type == type.Value);
        }

        if (category.HasValue)
        {
            filtered = filtered.Where(e => e.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            filtered = filtered.Where(e => e.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
                                        || e.SourceLabel.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .ToList();

        decimal totalCredits = filteredList.Sum(e => e.CreditIncomeTnd);
        decimal totalDebits = filteredList.Sum(e => e.DebitExpenseTnd);
        decimal closingBalance = filteredList.FirstOrDefault()?.RunningBalanceTnd ?? openingBalance;

        return new JournalViewModel
        {
            SelectedMonth = month,
            SelectedType = type,
            SelectedCategory = category,
            SelectedBatchId = batchId,
            SearchQuery = search,
            AvailableMonths = availableMonths,
            ImportBatches = batches,
            OpeningBalanceTnd = openingBalance,
            TotalCreditIncomeTnd = totalCredits,
            TotalDebitExpenseTnd = totalDebits,
            ClosingBalanceTnd = closingBalance,
            TotalEntriesCount = filteredList.Count,
            Entries = filteredList
        };
    }

    public async Task<BilanEpargneViewModel> BuildBilanEpargneAsync(CancellationToken cancellationToken = default)
    {
        var allTx = await _dbContext.Transactions
            .AsNoTracking()
            .OrderBy(t => t.Date)
            .ToListAsync(cancellationToken);

        var vm = new BilanEpargneViewModel
        {
            TotalTransactionsCount = allTx.Count
        };

        if (allTx.Count == 0)
        {
            vm.FinancialHealthScore = 50;
            vm.FinancialHealthLabel = "En attente de données";
            return vm;
        }

        var monthlyHistory = allTx
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyTrendPoint
            {
                MonthLabel = $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                TotalIncomeTnd = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd),
                TotalExpensesTnd = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd),
                IsForecast = false
            })
            .ToList();

        int monthsCount = Math.Max(1, monthlyHistory.Count);
        vm.TotalMonthsAnalyzed = monthsCount;
        vm.MonthlyBilanHistory = monthlyHistory;

        vm.CumulativeIncomeTnd = allTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd);
        vm.CumulativeExpensesTnd = allTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd);

        vm.AverageMonthlyIncomeTnd = Math.Round(vm.CumulativeIncomeTnd / monthsCount, 3);
        vm.AverageMonthlyExpensesTnd = Math.Round(vm.CumulativeExpensesTnd / monthsCount, 3);

        var bestMonth = monthlyHistory.OrderByDescending(m => m.NetSavingsTnd).First();
        vm.BestSavingsMonthLabel = bestMonth.MonthLabel;
        vm.BestSavingsMonthAmountTnd = bestMonth.NetSavingsTnd;

        var highestExpMonth = monthlyHistory.OrderByDescending(m => m.TotalExpensesTnd).First();
        vm.HighestExpenseMonthLabel = highestExpMonth.MonthLabel;
        vm.HighestExpenseMonthAmountTnd = highestExpMonth.TotalExpensesTnd;

        // Calcul des postes Essentiels (50%), Envies/Loisirs (30%) et Épargne (20%) en moyenne mensuelle
        var allExpenses = allTx.Where(t => t.Type == TransactionType.Expense).ToList();

        decimal totalEssentials = allExpenses.Where(e => EssentialCategories.Contains(e.Category)).Sum(e => e.AmountTnd);
        decimal totalLifestyle = allExpenses.Where(e => LifestyleCategories.Contains(e.Category)).Sum(e => e.AmountTnd);

        decimal avgMonthlyEssentials = Math.Round(totalEssentials / monthsCount, 3);
        decimal avgMonthlyLifestyle = Math.Round(totalLifestyle / monthsCount, 3);
        decimal avgMonthlySavings = Math.Max(0m, vm.AverageMonthlySavingsTnd);

        vm.EssentialMonthlyAvgTnd = avgMonthlyEssentials;

        decimal refIncome = Math.Max(1m, vm.AverageMonthlyIncomeTnd);
        double essentialPct = Math.Round((double)(avgMonthlyEssentials / refIncome) * 100.0, 1);
        double lifestylePct = Math.Round((double)(avgMonthlyLifestyle / refIncome) * 100.0, 1);
        double savingsPct = Math.Round((double)(vm.AverageMonthlySavingsTnd / refIncome) * 100.0, 1);

        vm.Rule503020 = new List<BudgetRuleBucket>
        {
            new BudgetRuleBucket
            {
                Name = "50 % · Besoins Essentiels & Charges Fixes",
                Subtitle = "Logement, STEG, SONEDE, Topnet, Alimentation, Transport, Santé",
                TargetPercent = 50.0,
                ActualPercent = essentialPct,
                TargetAmountTnd = Math.Round(vm.AverageMonthlyIncomeTnd * 0.50m, 3),
                ActualAmountTnd = avgMonthlyEssentials,
                StatusBadge = essentialPct <= 52.0 ? "success" : (essentialPct <= 60.0 ? "warning" : "danger"),
                Advice = essentialPct <= 50.0
                    ? "Excellent : vos charges vitales restent sous le seuil de 50 % de vos revenus."
                    : "Optimisez vos courses hebdomadaires (promotions Aziza/Monoprix) et surveillez la consommation STEG."
            },
            new BudgetRuleBucket
            {
                Name = "30 % · Envies, Cafés, Sorties & Shopping",
                Subtitle = "Cafés, Restaurants, Livraisons, Shopping, Cinéma, Abonnements",
                TargetPercent = 30.0,
                ActualPercent = lifestylePct,
                TargetAmountTnd = Math.Round(vm.AverageMonthlyIncomeTnd * 0.30m, 3),
                ActualAmountTnd = avgMonthlyLifestyle,
                StatusBadge = lifestylePct <= 30.0 ? "success" : (lifestylePct <= 38.0 ? "warning" : "danger"),
                Advice = lifestylePct <= 30.0
                    ? "Vos dépenses plaisir sont bien proportionnées par rapport à votre revenu en TND."
                    : "Poste en dépassement : réduisez les achats impulsifs et fixez un budget hebdomadaire cafés/restos."
            },
            new BudgetRuleBucket
            {
                Name = "20 % · Épargne & Investissement",
                Subtitle = "Épargne de précaution, Compte Épargne en TND, Projets futurs",
                TargetPercent = 20.0,
                ActualPercent = savingsPct,
                TargetAmountTnd = Math.Round(vm.AverageMonthlyIncomeTnd * 0.20m, 3),
                ActualAmountTnd = vm.AverageMonthlySavingsTnd,
                StatusBadge = savingsPct >= 20.0 ? "success" : (savingsPct >= 10.0 ? "warning" : "danger"),
                Advice = savingsPct >= 20.0
                    ? "Bravo ! Vous épargnez plus de 20 % de vos revenus chaque mois."
                    : "Programmez un virement automatique vers votre compte épargne dès le 1er du mois (Pay Yourself First)."
            }
        };

        // Génération des Conseils d'Épargne Personnalisés Chiffrés
        var advices = new List<SavingsAdviceTip>();

        decimal avgMonthlyDining = Math.Round(
            allExpenses.Where(e => e.Category == TransactionCategory.DiningOut).Sum(e => e.AmountTnd) / monthsCount, 3);
        if (avgMonthlyDining > 40m)
        {
            decimal diningSave = Math.Round(avgMonthlyDining * 0.35m, 3);
            advices.Add(new SavingsAdviceTip
            {
                Icon = "bi-cup-hot-fill",
                CategoryTag = "Cafés, Restos & Livraisons",
                Title = "Optimiser le rituel Cafés, Salons de Thé & Fast-Food",
                Detail = $"Vous dépensez en moyenne {avgMonthlyDining:0.000} TND/mois en restauration extérieure. En remplaçant 2 livraisons/sorties par semaine par du fait-maison, vous réduisez ce poste de 35 % sans priver votre vie sociale.",
                PotentialMonthlySavingsTnd = diningSave,
                Priority = "Haute"
            });
        }

        decimal avgMonthlyShopping = Math.Round(
            allExpenses.Where(e => e.Category == TransactionCategory.ShoppingAndEntertainment).Sum(e => e.AmountTnd) / monthsCount, 3);
        if (avgMonthlyShopping > 60m)
        {
            decimal shoppingSave = Math.Round(avgMonthlyShopping * 0.30m, 3);
            advices.Add(new SavingsAdviceTip
            {
                Icon = "bi-bag-check-fill",
                CategoryTag = "Shopping & Équipement",
                Title = "Règle des 48h sur les achats High-Tech & Mode",
                Detail = $"Votre moyenne mensuelle Shopping & Loisirs est de {avgMonthlyShopping:0.000} TND. Attendre 48h et comparer les prix (MyTek, Tunisianet, Wiki) avant tout achat supérieur à 80.000 TND permet d'économiser environ 30 %.",
                PotentialMonthlySavingsTnd = shoppingSave,
                Priority = "Haute"
            });
        }

        decimal avgMonthlyTransport = Math.Round(
            allExpenses.Where(e => e.Category == TransactionCategory.Transport).Sum(e => e.AmountTnd) / monthsCount, 3);
        if (avgMonthlyTransport > 50m)
        {
            decimal transportSave = Math.Round(avgMonthlyTransport * 0.15m, 3);
            advices.Add(new SavingsAdviceTip
            {
                Icon = "bi-fuel-pump-fill",
                CategoryTag = "Transport & Carburant",
                Title = "Entretien préventif & Éco-conduite",
                Detail = $"Vos frais de transport s'élèvent à {avgMonthlyTransport:0.000} TND/mois. Vérifier la pression des pneus chaque mois en station (Agil, Shell, Total) et regrouper les déplacements réduit la facture carburant et évite les pannes coûteuses.",
                PotentialMonthlySavingsTnd = transportSave,
                Priority = "Moyenne"
            });
        }

        decimal avgMonthlyGroceries = Math.Round(
            allExpenses.Where(e => e.Category == TransactionCategory.Groceries).Sum(e => e.AmountTnd) / monthsCount, 3);
        if (avgMonthlyGroceries > 80m)
        {
            decimal groceriesSave = Math.Round(avgMonthlyGroceries * 0.12m, 3);
            advices.Add(new SavingsAdviceTip
            {
                Icon = "bi-cart-check-fill",
                CategoryTag = "Alimentation & Courses",
                Title = "Courses sur liste & Marques distributeurs",
                Detail = $"Avec {avgMonthlyGroceries:0.000} TND/mois en supermarché, privilégier une liste hebdomadaire stricte et les marchés de produits frais locaux permet d'économiser 12 % sur le panier moyen.",
                PotentialMonthlySavingsTnd = groceriesSave,
                Priority = "Moyenne"
            });
        }

        vm.SavingsAdvices = advices;
        vm.PotentialExtraMonthlySavingsTnd = advices.Sum(a => a.PotentialMonthlySavingsTnd);

        // Score de santé globale
        var (healthScore, healthLabel, healthSummary) = ComputeHealthScore(
            vm.AverageMonthlyIncomeTnd,
            vm.AverageMonthlyExpensesTnd,
            vm.Rule503020.Count(r => r.StatusBadge == "danger"));
        vm.FinancialHealthScore = healthScore;
        vm.FinancialHealthLabel = healthLabel;
        vm.FinancialHealthSummary = healthSummary;

        // Projections d'objectifs d'épargne en TND
        decimal baseMonthlySave = Math.Max(50m, vm.AverageMonthlySavingsTnd);
        decimal optMonthlySave = Math.Max(100m, vm.OptimizedMonthlySavingsTnd);

        var goals = new (string Title, string Icon, decimal TargetTnd)[]
        {
            ("Fonds d'Urgence (3 Mois de Sécurité)", "bi-shield-lock-fill", Math.Max(1500m, vm.EmergencyFund3MonthsTnd)),
            ("Vacances & Voyage Détente", "bi-airplane-fill", 2500.000m),
            ("Setup Informatique / Équipement Maison", "bi-laptop", 4200.000m),
            ("Apport Voiture ou Projet Immobilier", "bi-car-front-fill", 15000.000m)
        };

        foreach (var g in goals)
        {
            int mCurrent = (int)Math.Ceiling(g.TargetTnd / baseMonthlySave);
            int mOpt = (int)Math.Ceiling(g.TargetTnd / optMonthlySave);
            vm.GoalProjections.Add(new SavingsGoalProjection
            {
                GoalTitle = g.Title,
                Icon = g.Icon,
                TargetAmountTnd = g.TargetTnd,
                MonthsWithCurrentSavings = mCurrent,
                MonthsWithOptimizedSavings = mOpt
            });
        }

        return vm;
    }

    private static (int Score, string Label, string Summary) ComputeHealthScore(decimal income, decimal expenses, int warningCount)
    {
        if (income <= 0)
        {
            return (45, "Revenus non renseignés", "Ajoutez vos virements de salaire ou revenus pour calculer votre taux d'épargne réel.");
        }

        double savingsRate = (double)((income - expenses) / income) * 100.0;
        int baseScore;
        if (savingsRate >= 30) baseScore = 92;
        else if (savingsRate >= 20) baseScore = 84;
        else if (savingsRate >= 10) baseScore = 72;
        else if (savingsRate >= 0) baseScore = 58;
        else baseScore = 38;

        int finalScore = Math.Clamp(baseScore - (warningCount * 6), 15, 98);

        string label = finalScore >= 80 ? "Excellente Santé Financière"
                     : finalScore >= 65 ? "Équilibre Budgétaire Solide"
                     : finalScore >= 50 ? "Budget sous Tension Modérée"
                     : "Alerte Déficit / Surchauffe";

        string summary = finalScore >= 80
            ? $"Votre taux d'épargne ({savingsRate:0.0}%) dépasse les standards recommandés. Continuez à capitaliser votre excédent en TND."
            : finalScore >= 65
            ? $"Votre budget dégage un solde positif ({savingsRate:0.0}% d'épargne), avec une marge d'optimisation intéressante sur les dépenses variables."
            : $"Vos dépenses absorbent {100.0 - savingsRate:0.0}% de vos revenus. Appliquez le plan d'épargne ci-dessous pour reconstituer votre marge de sécurité.";

        return (finalScore, label, summary);
    }

    private static bool TryParseYearMonth(string input, out int year, out int month)
    {
        year = 0;
        month = 0;
        var parts = input.Split('-');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out year)
            && int.TryParse(parts[1], out month)
            && year >= 2000 && year <= 2100
            && month >= 1 && month <= 12;
    }

    private static decimal ComputeLinearRegressionForecast(List<double> monthlyTotals)
    {
        int n = monthlyTotals.Count;
        if (n == 0) return 0m;
        if (n == 1) return Math.Round((decimal)monthlyTotals[0], 3);

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
                        Reason = $"Dépasse de {diff:0.000} TND la moyenne habituelle ({mean:0.000} TND) de cette catégorie (Z = {zScore:0.00}).",
                        SourceLabel = string.IsNullOrWhiteSpace(tx.SourceLabel) ? "Socle Initial" : tx.SourceLabel
                    });
                }
            }
        }

        return alerts.OrderByDescending(a => a.ZScore).Take(10).ToList();
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
                Description = $"Vous avez effectué {diningTx.Count} dépenses de restauration sur cette période pour un total de {diningTotal:0.000} TND ({diningShare:0.0}% des dépenses).",
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
                Description = $"Vos achats Shopping & Loisirs atteignent {currentShopping:0.000} TND sur ce mois contre une moyenne mensuelle de {avgHistoricalShopping:0.000} TND (+{((currentShopping - avgHistoricalShopping) / avgHistoricalShopping) * 100m:0.0}%).",
                EstimatedMonthlyImpactTnd = Math.Round(excess, 3),
                Recommendation = "Appliquez la règle d'attente de 48 heures avant tout achat non essentiel supérieur à 50.000 TND."
            });
        }

        if (insights.Count == 0)
        {
            insights.Add(new BadHabitInsight
            {
                Title = "Rythme budgétaire maîtrisé sur cette période",
                Severity = "info",
                Description = "Aucune dérive comportementale majeure (micro-dépenses excessives ou surchauffe de début de mois) n'a été détectée sur ce mois.",
                EstimatedMonthlyImpactTnd = 0m,
                Recommendation = "Continuez à catégoriser vos imports CSV chaque mois pour affiner vos prévisions."
            });
        }

        return insights;
    }
}
