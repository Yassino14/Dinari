using System.ComponentModel.DataAnnotations;

namespace Dinari.Models;

public enum TransactionType
{
    [Display(Name = "Dépense")]
    Expense = 0,

    [Display(Name = "Revenu")]
    Income = 1
}

public enum TransactionCategory
{
    [Display(Name = "Alimentation & Supermarché")]
    Groceries = 0,

    [Display(Name = "Cafés, Restaurants & Fast-Food")]
    DiningOut = 1,

    [Display(Name = "Logement & Factures (STEG, SONEDE, Internet)")]
    UtilitiesAndRent = 2,

    [Display(Name = "Transport & Carburant")]
    Transport = 3,

    [Display(Name = "Shopping & Loisirs")]
    ShoppingAndEntertainment = 4,

    [Display(Name = "Santé & Pharmacie")]
    Health = 5,

    [Display(Name = "Abonnements & Télécoms")]
    Subscriptions = 6,

    [Display(Name = "Revenu / Salaire / Virement")]
    Income = 7,

    [Display(Name = "Autres")]
    Other = 8
}

