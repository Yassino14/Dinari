using Dinari.Data;
using Dinari.Models;
using Dinari.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dinari.Controllers;

public class TransactionsController : Controller
{
    private readonly AppDbContext _dbContext;
    private readonly ICategorizationService _categorizationService;
    private readonly ICsvImportService _csvImportService;

    public TransactionsController(
        AppDbContext dbContext,
        ICategorizationService categorizationService,
        ICsvImportService csvImportService)
    {
        _dbContext = dbContext;
        _categorizationService = categorizationService;
        _csvImportService = csvImportService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(TransactionCategory? category, string? search, CancellationToken cancellationToken)
    {
        var query = _dbContext.Transactions.AsNoTracking().AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(t => t.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string pattern = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.Description, pattern));
        }

        var list = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);

        ViewBag.SelectedCategory = category;
        ViewBag.Search = search;
        return View(list);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Transaction { Date = DateTime.Today });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Transaction model, bool autoCategorize = false, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.AmountTnd = Math.Round(model.AmountTnd, 3, MidpointRounding.AwayFromZero);

        if (autoCategorize)
        {
            model.Category = _categorizationService.PredictCategory(model.Description, model.Type);
            model.IsAutoCategorized = true;
        }
        else
        {
            model.IsAutoCategorized = false;
        }

        model.ExternalHash = _csvImportService.ComputeTransactionHash(model.Date, model.Description, model.AmountTnd, model.Type);

        bool exists = await _dbContext.Transactions.AnyAsync(t => t.ExternalHash == model.ExternalHash, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Cette transaction exacte existe déjà pour cette date.");
            return View(model);
        }

        _dbContext.Transactions.Add(model);
        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = $"Transaction de {model.AmountTnd:0.000} TND enregistrée avec succès.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Import()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? csvFile, CancellationToken cancellationToken)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Veuillez sélectionner un fichier CSV valide.");
            return View();
        }

        using var stream = csvFile.OpenReadStream();
        var importResult = await _csvImportService.ImportFromStreamAsync(stream, cancellationToken);

        return View(importResult);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var tx = await _dbContext.Transactions.FindAsync(new object[] { id }, cancellationToken);
        if (tx != null)
        {
            _dbContext.Transactions.Remove(tx);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return RedirectToAction(nameof(Index));
    }
}

