using Dinari.Models;

namespace Dinari.Services;

public interface ICsvImportService
{
    Task<CsvImportResult> ImportFromStreamAsync(
        Stream csvStream,
        CancellationToken cancellationToken = default,
        string fileName = "Relevé_CSV.csv");

    Task<List<CsvImportBatch>> GetImportHistoryAsync(CancellationToken cancellationToken = default);

    string ComputeTransactionHash(DateTime date, string description, decimal amountTnd, TransactionType type);
}
