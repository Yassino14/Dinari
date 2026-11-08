using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
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
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var tx = await _dbContext.Transactions.FindAsync(new object[] { id }, cancellationToken);
        if (tx == null)
        {
            return NotFound();
        }
        return View(tx);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Transaction model, bool autoCategorize = false, CancellationToken cancellationToken = default)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _dbContext.Transactions.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return NotFound();
        }

        existing.Date = model.Date;
        existing.Description = model.Description;
        existing.AmountTnd = Math.Round(model.AmountTnd, 3, MidpointRounding.AwayFromZero);
        existing.Type = model.Type;

        if (autoCategorize)
        {
            existing.Category = _categorizationService.PredictCategory(model.Description, model.Type);
            existing.IsAutoCategorized = true;
        }
        else
        {
            existing.Category = model.Category;
            existing.IsAutoCategorized = false;
        }

        existing.ExternalHash = _csvImportService.ComputeTransactionHash(existing.Date, existing.Description, existing.AmountTnd, existing.Type);

        bool duplicate = await _dbContext.Transactions.AnyAsync(t => t.ExternalHash == existing.ExternalHash && t.Id != id, cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError(string.Empty, "Une autre transaction identique existe déjà pour cette date.");
            return View(model);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = "Transaction modifiée avec succès.";

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

    [HttpGet]
    public async Task<IActionResult> ExportCsv(TransactionCategory? category, string? search, CancellationToken cancellationToken)
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

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        {
            csv.WriteField("Date");
            csv.WriteField("Description");
            csv.WriteField("Montant");
            csv.WriteField("Type");
            csv.WriteField("Categorie");
            await csv.NextRecordAsync();

            foreach (var t in list)
            {
                csv.WriteField(t.Date.ToString("yyyy-MM-dd"));
                csv.WriteField(t.Description);
                csv.WriteField(t.AmountTnd.ToString("0.000", CultureInfo.InvariantCulture));
                csv.WriteField(t.Type.ToString());
                csv.WriteField(_categorizationService.GetCategoryDisplayName(t.Category));
                await csv.NextRecordAsync();
            }
        }

        memoryStream.Position = 0;
        return File(memoryStream.ToArray(), "text/csv", $"transactions_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
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
            TempData["SuccessMessage"] = "Transaction supprimée.";
        }
        return RedirectToAction(nameof(Index));
    }
}
