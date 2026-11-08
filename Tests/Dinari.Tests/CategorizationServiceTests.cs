using Dinari.Models;
using Dinari.Services;
using Xunit;

namespace Dinari.Tests;

public class CategorizationServiceTests
{
    private readonly CategorizationService _service = new();

    [Theory]
    [InlineData("Facture STEG Électricité", TransactionCategory.UtilitiesAndRent)]
    [InlineData("Facture SONEDE Eau", TransactionCategory.UtilitiesAndRent)]
    [InlineData("Abonnement Topnet Fibre", TransactionCategory.UtilitiesAndRent)]
    [InlineData("Aziza Supermarché", TransactionCategory.Groceries)]
    [InlineData("Monoprix Alimentation", TransactionCategory.Groceries)]
    [InlineData("Carrefour Market", TransactionCategory.Groceries)]
    [InlineData("Station Agil Carburant", TransactionCategory.Transport)]
    [InlineData("Station Shell Essence", TransactionCategory.Transport)]
    [InlineData("Bolt Course Taxi", TransactionCategory.Transport)]
    [InlineData("Café Express & Croissant", TransactionCategory.DiningOut)]
    [InlineData("Pizzeria Livraison Soir", TransactionCategory.DiningOut)]
    [InlineData("Salon de Thé & Pâtisserie", TransactionCategory.DiningOut)]
    [InlineData("Forfait Ooredoo Mobile", TransactionCategory.Subscriptions)]
    [InlineData("Abonnement Orange Box", TransactionCategory.Subscriptions)]
    [InlineData("Pharmacie de Nuit", TransactionCategory.Health)]
    [InlineData("Consultation Docteur", TransactionCategory.Health)]
    [InlineData("Zara Mall du Lac", TransactionCategory.ShoppingAndEntertainment)]
    [InlineData("MyTek Écran Gaming", TransactionCategory.ShoppingAndEntertainment)]
    public void PredictCategory_ShouldMatchKnownTunisianMerchants(string description, TransactionCategory expected)
    {
        var category = _service.PredictCategory(description, TransactionType.Expense);
        Assert.Equal(expected, category);
    }

    [Fact]
    public void PredictCategory_IncomeType_AlwaysReturnsIncomeCategory()
    {
        var category = _service.PredictCategory("Virement Salaire Novembre", TransactionType.Income);
        Assert.Equal(TransactionCategory.Income, category);
    }

    [Fact]
    public void PredictCategory_UnknownMerchant_ReturnsOther()
    {
        var category = _service.PredictCategory("Paiement Marchand Inconnu XYZ", TransactionType.Expense);
        Assert.Equal(TransactionCategory.Other, category);
    }
}

