using Dinari.Models;

namespace Dinari.Services;

public interface IFinancialAnalyticsService
{
    Task<DashboardViewModel> BuildDashboardAsync(
        CancellationToken cancellationToken = default,
        string? selectedMonth = null,
        int? batchId = null);

    Task<JournalViewModel> BuildJournalAsync(
        string? month = null,
        TransactionType? type = null,
        TransactionCategory? category = null,
        int? batchId = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<BilanEpargneViewModel> BuildBilanEpargneAsync(
        CancellationToken cancellationToken = default);
}
