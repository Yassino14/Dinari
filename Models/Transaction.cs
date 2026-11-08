using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Dinari.Models;

public class Transaction
{
    public int Id { get; set; }

    [Required(ErrorMessage = "La date est obligatoire.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date")]
    public DateTime Date { get; set; }

    [Required(ErrorMessage = "Le libellé est obligatoire.")]
    [StringLength(250)]
    [Display(Name = "Libellé / Marchand")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le montant en TND est obligatoire.")]
    [Range(0.001, 1000000.000, ErrorMessage = "Le montant doit être supérieur à 0.000 TND.")]
    [Column(TypeName = "decimal(18, 3)")]
    [Display(Name = "Montant (TND)")]
    public decimal AmountTnd { get; set; }

    [Display(Name = "Type")]
    public TransactionType Type { get; set; } = TransactionType.Expense;

    [Display(Name = "Catégorie")]
    public TransactionCategory Category { get; set; } = TransactionCategory.Other;

    [Display(Name = "Catégorisée automatiquement")]
    public bool IsAutoCategorized { get; set; } = true;

    [StringLength(64)]
    public string ExternalHash { get; set; } = string.Empty;

    public int? ImportBatchId { get; set; }

    [StringLength(150)]
    [Display(Name = "Source / Relevé")]
    public string SourceLabel { get; set; } = "Saisie Manuelle";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
