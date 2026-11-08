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

    public async Task<CsvImportResult> ImportFromStreamAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        var result = new CsvImportResult();

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string content = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            result.Errors.Add("Le fichier CSV est vide.");
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
                    ExternalHash = hash
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Ligne {rowNumber} : Erreur inattendue ({ex.Message}).");
            }
        }

        if (newTransactions.Count > 0)
        {
            await _dbContext.Transactions.AddRangeAsync(newTransactions, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result.ImportedCount = newTransactions.Count;
        }

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

