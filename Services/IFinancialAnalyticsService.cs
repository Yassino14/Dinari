using Dinari.Models;

namespace Dinari.Services;

public interface IFinancialAnalyticsService
{
    Task<DashboardViewModel> BuildDashboardAsync(CancellationToken cancellationToken = default);
}

