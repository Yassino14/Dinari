using Dinari.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dinari.Controllers;

public class DashboardController : Controller
{
    private readonly IFinancialAnalyticsService _analyticsService;

    public DashboardController(IFinancialAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var viewModel = await _analyticsService.BuildDashboardAsync(cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ChartData(CancellationToken cancellationToken)
    {
        var vm = await _analyticsService.BuildDashboardAsync(cancellationToken);
        return Json(new
        {
            categories = vm.CategoryBreakdown.Select(c => new { c.CategoryName, c.TotalTnd, c.ForecastNextMonthTnd }),
            trends = vm.MonthlyTrends.Select(m => new { m.MonthLabel, m.TotalExpensesTnd, m.TotalIncomeTnd, m.IsForecast })
        });
    }
}

