using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Dinari.Data;
using Dinari.Models;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Services;

public class CsvImportService : ICsvImportService
{
    private readonly AppDbContext _dbContext;
    private readonly ICategorizationService _categorizationService;

    public CsvImportService(AppDbContext dbContext, ICategorizationService categorizationService)
    {
        _dbContext = dbContext;
        _categorizationService = categorizationService;
    }

    public async Task<List<CsvImportBatch>> GetImportHistoryAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.CsvImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.ImportedAtUtc)
            .ThenByDescending(b => b.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CsvImportResult> ImportFromStreamAsync(
        Stream csvStream,
        CancellationToken cancellationToken = default,
        string fileName = "Relevé_CSV.csv")
    {
        string cleanFileName = string.IsNullOrWhiteSpace(fileName) ? "Relevé_CSV.csv" : Path.GetFileName(fileName.Trim());
        var result = new CsvImportResult { FileName = cleanFileName };

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string content = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            result.Errors.Add("Le fichier CSV est vide.");
            result.ImportHistory = await GetImportHistoryAsync(cancellationToken);
            return result;
        }

        // Détection automatique du délimiteur (; très fréquent sur Excel FR/TN, sinon ,)
        string firstLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        string delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ";" : ",";

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true
        };

        var existingHashes = await _dbContext.Transactions
            .AsNoTracking()
            .Select(t => t.ExternalHash)
            .ToHashSetAsync(cancellationToken);

        var newTransactions = new List<Transaction>();

        using var stringReader = new StringReader(content);
        using var csv = new CsvReader(stringReader, config);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            result.Errors.Add("Impossible de lire l'en-tête du fichier CSV.");
            result.ImportHistory = await GetImportHistoryAsync(cancellationToken);
            return result;
        }

        int rowNumber = 1;
        while (await csv.ReadAsync())
        {
            rowNumber++;
            result.TotalRowsRead++;

            try
            {
                string rawDate = GetFieldFlexible(csv, "Date", "date", "DATE", "DateOperation");
                string rawDesc = GetFieldFlexible(csv, "Description", "Libelle", "Libellé", "Marchand", "Label");
                string rawAmount = GetFieldFlexible(csv, "Montant", "Amount", "MontantTND", "AmountTnd", "Valeur");
                string rawType = GetFieldFlexible(csv, "Type", "Sens", "Nature");
                string rawCategory = GetFieldFlexible(csv, "Categorie", "Catégorie", "Category");

                if (string.IsNullOrWhiteSpace(rawDate) || string.IsNullOrWhiteSpace(rawDesc) || string.IsNullOrWhiteSpace(rawAmount))
                {
                    result.Errors.Add($"Ligne {rowNumber} ignorée : Date, Description ou Montant manquant.");
                    continue;
                }

                if (!TryParseFlexibleDate(rawDate, out DateTime parsedDate))
                {
                    result.Errors.Add($"Ligne {rowNumber} ignorée : Format de date invalide ('{rawDate}').");
                    continue;
                }

                if (!TryParseFlexibleDecimal(rawAmount, out decimal parsedAmount) || parsedAmount == 0m)
                {
                    result.Errors.Add($"Ligne {rowNumber} ignorée : Montant invalide ou nul ('{rawAmount}').");
                    continue;
                }

                // Détermination du type (Dépense vs Revenu)
                TransactionType txType = TransactionType.Expense;
                if (parsedAmount < 0)
                {
                    txType = TransactionType.Expense;
                    parsedAmount = Math.Abs(parsedAmount);
                }
                else if (!string.IsNullOrWhiteSpace(rawType))
                {
                    string normType = rawType.Trim().ToLowerInvariant();
                    if (normType.Contains("rev") || normType.Contains("inc") || normType.Contains("cred") || normType.Contains("crédit"))
                    {
                        txType = TransactionType.Income;
                    }
                }
                else if (_categorizationService.PredictCategory(rawDesc, TransactionType.Expense) == TransactionCategory.Income)
                {
                    txType = TransactionType.Income;
                }

                parsedAmount = Math.Round(parsedAmount, 3, MidpointRounding.AwayFromZero);

                // Catégorisation : manuelle si présente et valide dans le CSV, sinon automatique
                TransactionCategory category;
                bool isAuto = true;
                if (!string.IsNullOrWhiteSpace(rawCategory) && Enum.TryParse<TransactionCategory>(rawCategory, ignoreCase: true, out var explicitCat))
                {
                    category = explicitCat;
                    isAuto = false;
                }
                else
                {
                    category = _categorizationService.PredictCategory(rawDesc, txType);
                }

                string hash = ComputeTransactionHash(parsedDate, rawDesc, parsedAmount, txType);
                if (existingHashes.Contains(hash))
                {
                    result.SkippedDuplicatesCount++;
                    continue;
                }

                existingHashes.Add(hash);
                newTransactions.Add(new Transaction
                {
                    Date = parsedDate.Date,
                    Description = rawDesc.Trim(),
                    AmountTnd = parsedAmount,
                    Type = txType,
                    Category = category,
                    IsAutoCategorized = isAuto,
                    ExternalHash = hash,
                    SourceLabel = $"CSV: {cleanFileName}"
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Ligne {rowNumber} : Erreur inattendue ({ex.Message}).");
            }
        }

        if (newTransactions.Count > 0)
        {
            decimal batchIncome = newTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.AmountTnd);
            decimal batchExpense = newTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.AmountTnd);
            DateTime minDate = newTransactions.Min(t => t.Date);
            DateTime maxDate = newTransactions.Max(t => t.Date);

            var topCategoryGroup = newTransactions
                .Where(t => t.Type == TransactionType.Expense)
                .GroupBy(t => t.Category)
                .OrderByDescending(g => g.Sum(x => x.AmountTnd))
                .FirstOrDefault();

            string topCatInfo = topCategoryGroup != null
                ? $"Poste majeur : {_categorizationService.GetCategoryDisplayName(topCategoryGroup.Key)} ({topCategoryGroup.Sum(x => x.AmountTnd):0.000} TND)."
                : "Aucune dépense dans ce lot.";

            var maxTx = newTransactions
                .Where(t => t.Type == TransactionType.Expense)
                .OrderByDescending(t => t.AmountTnd)
                .FirstOrDefault();

            string maxTxInfo = maxTx != null
                ? $" Plus forte dépense : {maxTx.Description} ({maxTx.AmountTnd:0.000} TND)."
                : "";

            string summary = $"Import protégé de {newTransactions.Count} opération(s). Flux net du relevé : {(batchIncome - batchExpense):+0.000;-0.000;0.000} TND. {topCatInfo}{maxTxInfo}";

            var batch = new CsvImportBatch
            {
                FileName = cleanFileName,
                ImportedAtUtc = DateTime.UtcNow,
                TotalRowsRead = result.TotalRowsRead,
                ImportedCount = newTransactions.Count,
                SkippedDuplicatesCount = result.SkippedDuplicatesCount,
                TotalIncomeTnd = batchIncome,
                TotalExpensesTnd = batchExpense,
                PeriodCovered = $"{minDate:dd/MM/yyyy} → {maxDate:dd/MM/yyyy}",
                InterpretationSummary = summary
            };

            _dbContext.CsvImportBatches.Add(batch);
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var tx in newTransactions)
            {
                tx.ImportBatchId = batch.Id;
            }

            await _dbContext.Transactions.AddRangeAsync(newTransactions, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            result.ImportedCount = newTransactions.Count;
            result.BatchId = batch.Id;
            result.InterpretationSummary = summary;
        }
        else if (result.SkippedDuplicatesCount > 0)
        {
            result.InterpretationSummary = $"Protection anti-doublon SHA-256 active : les {result.SkippedDuplicatesCount} opération(s) de ce relevé sont déjà présentes et protégées dans la base.";
        }

        result.ImportHistory = await GetImportHistoryAsync(cancellationToken);
        return result;
    }

    public string ComputeTransactionHash(DateTime date, string description, decimal amountTnd, TransactionType type)
    {
        string raw = $"{date:yyyy-MM-dd}|{description.Trim().ToLowerInvariant()}|{amountTnd:0.000}|{(int)type}";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static string GetFieldFlexible(CsvReader csv, params string[] candidateNames)
    {
        if (csv.HeaderRecord == null) return string.Empty;
        foreach (string name in candidateNames)
        {
            string? matchedHeader = csv.HeaderRecord.FirstOrDefault(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            if (matchedHeader != null)
            {
                return csv.GetField(matchedHeader) ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static bool TryParseFlexibleDate(string input, out DateTime date)
    {
        string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "MM/dd/yyyy" };
        return DateTime.TryParseExact(input.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateTime.TryParse(input.Trim(), new CultureInfo("fr-TN"), DateTimeStyles.None, out date);
    }

    private static bool TryParseFlexibleDecimal(string input, out decimal value)
    {
        string cleaned = input.Replace("TND", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("DT", "", StringComparison.OrdinalIgnoreCase)
                              .Replace(" ", "")
                              .Replace("\u00A0", "")
                              .Replace(",", ".")
                              .Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
