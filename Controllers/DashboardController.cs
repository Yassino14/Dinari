using Dinari.Models;
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
    public async Task<IActionResult> Index(string? month, int? batchId, CancellationToken cancellationToken)
    {
        var viewModel = await _analyticsService.BuildDashboardAsync(cancellationToken, month, batchId);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Journal(
        string? month,
        TransactionType? type,
        TransactionCategory? category,
        int? batchId,
        string? search,
        CancellationToken cancellationToken)
    {
        var viewModel = await _analyticsService.BuildJournalAsync(month, type, category, batchId, search, cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Bilan(CancellationToken cancellationToken)
    {
        var viewModel = await _analyticsService.BuildBilanEpargneAsync(cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public IActionResult About()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ChartData(string? month, int? batchId, CancellationToken cancellationToken)
    {
        var vm = await _analyticsService.BuildDashboardAsync(cancellationToken, month, batchId);
        return Json(new
        {
            categories = vm.CategoryBreakdown.Select(c => new { c.CategoryName, c.TotalTnd, c.ForecastNextMonthTnd }),
            trends = vm.MonthlyTrends.Select(m => new { m.MonthLabel, m.TotalExpensesTnd, m.TotalIncomeTnd, m.IsForecast })
        });
    }
}
