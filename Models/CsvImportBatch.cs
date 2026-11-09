using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dinari.Models;

public class CsvImportBatch
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string FileName { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    public int TotalRowsRead { get; set; }

    public int ImportedCount { get; set; }

    public int SkippedDuplicatesCount { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal TotalIncomeTnd { get; set; }

    [Column(TypeName = "decimal(18, 3)")]
    public decimal TotalExpensesTnd { get; set; }

    [StringLength(120)]
    public string PeriodCovered { get; set; } = string.Empty;

    [StringLength(600)]
    public string InterpretationSummary { get; set; } = string.Empty;
}

