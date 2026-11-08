using Dinari.Models;

namespace Dinari.Services;

public interface ICategorizationService
{
    TransactionCategory PredictCategory(string description, TransactionType type);
    string GetCategoryDisplayName(TransactionCategory category);
}

