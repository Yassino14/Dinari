using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dinari.Models;

namespace Dinari.Services;

public class CategorizationService : ICategorizationService
{
    private static readonly Dictionary<TransactionCategory, string[]> KeywordRules = new()
    {
        [TransactionCategory.Groceries] = new[]
        {
            "aziza", "monoprix", "carrefour", "mg ", "magasin general", "geant",
            "supermarche", "epicerie", "boucherie", "marche", "legumes", "boulangerie"
        },
        [TransactionCategory.DiningOut] = new[]
        {
            "cafe", "salon de the", "patisserie", "resto", "restaurant", "pizzeria", "fast food",
            "makla", "sandwich", "glovo", "yassir express", "jumia food", "baguette",
            "coffee", "chawarma", "burger", "creperie"
        },
        [TransactionCategory.UtilitiesAndRent] = new[]
        {
            "steg", "sonede", "loyer", "syndic", "topnet", "hexabyte", "globalnet",
            "electricite", "eau", "gaz"
        },
        [TransactionCategory.Transport] = new[]
        {
            "agil", "shell", "total", "ola energy", "staroil", "kiosque", "carburant",
            "essence", "peage", "tunisie autoroutes", "taxi", "bolt", "indrive",
            "sncft", "transtu", "louage", "parking", "visite technique", "lavage"
        },
        [TransactionCategory.Subscriptions] = new[]
        {
            "ooredoo", "tunisie telecom", "orange", "recharge", "forfait", "netflix",
            "spotify", "shahid", "abonnement", "salle de sport", "gym"
        },
        [TransactionCategory.Health] = new[]
        {
            "pharmacie", "medecin", "docteur", "clinique", "laboratoire", "analyse",
            "dentiste", "opticien", "cnam", "hopital"
        },
        [TransactionCategory.ShoppingAndEntertainment] = new[]
        {
            "zara", "bershka", "pull&bear", "exist", "ha ", "zen ", "decathlon",
            "mytek", "tunisianet", "wiki", "sbs", "cinema", "pathe", "mall",
            "sortie", "cadeau", "vetement", "chaussures", "gaming"
        },
        [TransactionCategory.Income] = new[]
        {
            "salaire", "virement recu", "bourse", "prime", "remboursement", "freelance", "revenu"
        }
    };

    public TransactionCategory PredictCategory(string description, TransactionType type)
    {
        if (type == TransactionType.Income)
        {
            return TransactionCategory.Income;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return TransactionCategory.Other;
        }

        string normalized = RemoveDiacritics(description.Trim()).ToLowerInvariant();

        foreach (var (category, keywords) in KeywordRules)
        {
            foreach (var keyword in keywords)
            {
                if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }
        }

        return TransactionCategory.Other;
    }

    public string GetCategoryDisplayName(TransactionCategory category)
    {
        var memberInfo = typeof(TransactionCategory).GetMember(category.ToString()).FirstOrDefault();
        var displayAttr = memberInfo?.GetCustomAttribute<DisplayAttribute>();
        return displayAttr?.Name ?? category.ToString();
    }

    private static string RemoveDiacritics(string text)
    {
        string normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        foreach (char c in normalizedString)
        {
            UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}

