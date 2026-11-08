using System.Data;
using Dinari.Models;
using Dinari.Services;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var csvService = scope.ServiceProvider.GetRequiredService<ICsvImportService>();
        var catService = scope.ServiceProvider.GetRequiredService<ICategorizationService>();

        await context.Database.EnsureCreatedAsync();

        if (context.Database.IsSqlite())
        {
            await EnsureSchemaUpgradedAsync(context);
        }

        if (await context.Transactions.AnyAsync())
        {
            // Ensure initial batch record exists if transactions were already seeded before
            if (!await context.CsvImportBatches.AnyAsync())
            {
                var allTx = await context.Transactions.ToListAsync();
                decimal inc = allTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd);
                decimal exp = allTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd);
                DateTime minDate = allTx.Min(t => t.Date);
                DateTime maxDate = allTx.Max(t => t.Date);

                var initialBatch = new CsvImportBatch
                {
                    FileName = "Historique_Initial_TND (Socle Protégé)",
                    ImportedAtUtc = DateTime.UtcNow.AddDays(-1),
                    TotalRowsRead = allTx.Count,
                    ImportedCount = allTx.Count,
                    SkippedDuplicatesCount = 0,
                    TotalIncomeTnd = inc,
                    TotalExpensesTnd = exp,
                    PeriodCovered = $"{minDate:dd/MM/yyyy} → {maxDate:dd/MM/yyyy}",
                    InterpretationSummary = $"Socle historique protégé ({allTx.Count} opérations sur 4 mois). Revenus : {inc:0.000} TND, Dépenses : {exp:0.000} TND, Épargne nette : {(inc - exp):0.000} TND."
                };

                context.CsvImportBatches.Add(initialBatch);
                await context.SaveChangesAsync();

                foreach (var tx in allTx.Where(t => t.ImportBatchId == null))
                {
                    tx.ImportBatchId = initialBatch.Id;
                    if (string.IsNullOrWhiteSpace(tx.SourceLabel) || tx.SourceLabel == "Saisie Manuelle")
                    {
                        tx.SourceLabel = "Socle Initial";
                    }
                }
                await context.SaveChangesAsync();
            }
            return;
        }

        var seedRows = new (string Date, string Desc, decimal Amount, TransactionType Type)[]
        {
            // Août 2026
            ("2026-08-01", "Virement Salaire Août", 2150.000m, TransactionType.Income),
            ("2026-08-02", "Loyer Appartement", 650.000m, TransactionType.Expense),
            ("2026-08-04", "Carrefour Market Courses", 142.350m, TransactionType.Expense),
            ("2026-08-06", "Facture Topnet ADSL/Fibre", 72.500m, TransactionType.Expense),
            ("2026-08-08", "Kiosque Agil Carburant", 80.000m, TransactionType.Expense),
            ("2026-08-11", "Café & Croissant Centre-Ville", 8.500m, TransactionType.Expense),
            ("2026-08-14", "Monoprix Alimentation", 96.400m, TransactionType.Expense),
            ("2026-08-16", "Péage Tunisie Autoroutes & Lavage", 45.000m, TransactionType.Expense),
            ("2026-08-18", "Forfait Ooredoo Mobile", 35.000m, TransactionType.Expense),
            ("2026-08-21", "Zen Vêtements", 89.900m, TransactionType.Expense),
            ("2026-08-24", "HA Prêt-à-Porter Promo", 45.000m, TransactionType.Expense),
            ("2026-08-25", "Pharmacie de Nuit", 34.200m, TransactionType.Expense),
            ("2026-08-28", "Pharmacie El Manar", 42.000m, TransactionType.Expense),

            // Septembre 2026
            ("2026-09-01", "Virement Salaire Septembre", 2150.000m, TransactionType.Income),
            ("2026-09-02", "Loyer Appartement", 650.000m, TransactionType.Expense),
            ("2026-09-03", "Facture STEG Électricité", 118.600m, TransactionType.Expense),
            ("2026-09-05", "Aziza Supermarché", 112.800m, TransactionType.Expense),
            ("2026-09-09", "Station Shell Essence", 85.000m, TransactionType.Expense),
            ("2026-09-12", "Fast Food Makla", 24.500m, TransactionType.Expense),
            ("2026-09-16", "Magasin Général Courses", 128.900m, TransactionType.Expense),
            ("2026-09-18", "Bolt Course Taxi Urbain", 50.000m, TransactionType.Expense),
            ("2026-09-19", "Forfait Ooredoo Mobile", 35.000m, TransactionType.Expense),
            ("2026-09-22", "Cinéma Pathé Tunis City", 48.000m, TransactionType.Expense),
            ("2026-09-25", "Exist Chaussures & Accessoires", 62.000m, TransactionType.Expense),
            ("2026-09-27", "Consultation Médecin", 70.000m, TransactionType.Expense),

            // Octobre 2026
            ("2026-10-01", "Virement Salaire Octobre", 2150.000m, TransactionType.Income),
            ("2026-10-02", "Loyer Appartement", 650.000m, TransactionType.Expense),
            ("2026-10-04", "Facture SONEDE Eau", 46.200m, TransactionType.Expense),
            ("2026-10-06", "Carrefour Courses Mensuelles", 154.200m, TransactionType.Expense),
            ("2026-10-10", "Kiosque Total Carburant", 90.000m, TransactionType.Expense),
            ("2026-10-13", "Restaurant Pizzeria", 42.000m, TransactionType.Expense),
            ("2026-10-17", "Monoprix Courses", 104.500m, TransactionType.Expense),
            ("2026-10-19", "Station OLA Energy Appoint", 55.000m, TransactionType.Expense),
            ("2026-10-20", "Forfait Ooredoo Mobile", 35.000m, TransactionType.Expense),
            ("2026-10-24", "Decathlon Sport", 95.000m, TransactionType.Expense),
            ("2026-10-27", "Pharmacie Bab Bhar", 38.500m, TransactionType.Expense),

            // Novembre 2026 (Mois courant avec mauvaises habitudes + dépenses inhabituelles)
            ("2026-11-01", "Virement Salaire Novembre", 2150.000m, TransactionType.Income),
            ("2026-11-02", "Loyer Appartement", 650.000m, TransactionType.Expense),
            ("2026-11-02", "MyTek Équipement Gaming & Écran (Anomalie)", 689.000m, TransactionType.Expense),
            ("2026-11-03", "Salon de Thé & Pâtisserie", 28.500m, TransactionType.Expense),
            ("2026-11-03", "Fast Food Burger Livraison", 36.000m, TransactionType.Expense),
            ("2026-11-04", "Café Lounge Matin", 14.500m, TransactionType.Expense),
            ("2026-11-04", "Restaurant Grillades Soir", 68.000m, TransactionType.Expense),
            ("2026-11-05", "Carrefour Supermarché", 136.000m, TransactionType.Expense),
            ("2026-11-05", "Café & Chicha Amis", 32.000m, TransactionType.Expense),
            ("2026-11-06", "Sandwich & Boisson", 19.500m, TransactionType.Expense),
            ("2026-11-06", "Kiosque Agil Réparation Pneu & Plein (Anomalie)", 340.000m, TransactionType.Expense),
            ("2026-11-07", "Pizzeria Livraison Soir", 44.000m, TransactionType.Expense),
            ("2026-11-07", "Salon de Thé Après-midi", 21.000m, TransactionType.Expense),
            ("2026-11-08", "Café Express & Croissant", 11.500m, TransactionType.Expense)
        };

        decimal totalInc = seedRows.Where(r => r.Type == TransactionType.Income).Sum(r => r.Amount);
        decimal totalExp = seedRows.Where(r => r.Type == TransactionType.Expense).Sum(r => r.Amount);

        var seedBatch = new CsvImportBatch
        {
            FileName = "Historique_Initial_TND (Socle Protégé)",
            ImportedAtUtc = DateTime.UtcNow.AddDays(-1),
            TotalRowsRead = seedRows.Length,
            ImportedCount = seedRows.Length,
            SkippedDuplicatesCount = 0,
            TotalIncomeTnd = totalInc,
            TotalExpensesTnd = totalExp,
            PeriodCovered = "01/08/2026 → 08/11/2026",
            InterpretationSummary = $"Socle historique protégé ({seedRows.Length} opérations sur 4 mois). Revenus : {totalInc:0.000} TND, Dépenses : {totalExp:0.000} TND, Épargne nette cumulée : {(totalInc - totalExp):0.000} TND."
        };

        context.CsvImportBatches.Add(seedBatch);
        await context.SaveChangesAsync();

        var entities = new List<Transaction>();
        foreach (var item in seedRows)
        {
            DateTime dt = DateTime.Parse(item.Date);
            var cat = catService.PredictCategory(item.Desc, item.Type);
            string hash = csvService.ComputeTransactionHash(dt, item.Desc, item.Amount, item.Type);

            entities.Add(new Transaction
            {
                Date = dt,
                Description = item.Desc,
                AmountTnd = item.Amount,
                Type = item.Type,
                Category = cat,
                IsAutoCategorized = true,
                ExternalHash = hash,
                ImportBatchId = seedBatch.Id,
                SourceLabel = "Socle Initial"
            });
        }

        await context.Transactions.AddRangeAsync(entities);
        await context.SaveChangesAsync();
    }

    private static async Task EnsureSchemaUpgradedAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""CsvImportBatches"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_CsvImportBatches"" PRIMARY KEY AUTOINCREMENT,
                ""FileName"" TEXT NOT NULL,
                ""ImportedAtUtc"" TEXT NOT NULL,
                ""TotalRowsRead"" INTEGER NOT NULL,
                ""ImportedCount"" INTEGER NOT NULL,
                ""SkippedDuplicatesCount"" INTEGER NOT NULL,
                ""TotalIncomeTnd"" TEXT NOT NULL,
                ""TotalExpensesTnd"" TEXT NOT NULL,
                ""PeriodCovered"" TEXT NOT NULL,
                ""InterpretationSummary"" TEXT NOT NULL
            );
        ");

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = context.Database.GetDbConnection();
        bool wasClosed = conn.State == ConnectionState.Closed;
        if (wasClosed)
        {
            await conn.OpenAsync();
        }

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('Transactions');";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }
        finally
        {
            if (wasClosed)
            {
                await conn.CloseAsync();
            }
        }

        if (!existingColumns.Contains("ImportBatchId"))
        {
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" ADD COLUMN ""ImportBatchId"" INTEGER NULL;");
        }

        if (!existingColumns.Contains("SourceLabel"))
        {
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" ADD COLUMN ""SourceLabel"" TEXT NOT NULL DEFAULT 'Socle Initial';");
        }
    }
}
