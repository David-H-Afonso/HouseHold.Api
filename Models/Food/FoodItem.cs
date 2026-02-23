namespace Household.Api.Models.Food;

public class FoodItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public decimal KcalPer100g { get; set; }
    public decimal ProteinPer100g { get; set; }
    public decimal CarbsPer100g { get; set; }
    public decimal FatPer100g { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Auth.User CreatedByUser { get; set; } = null!;
    public ICollection<DishTemplateItem> DishTemplateItems { get; set; } = [];
    public ICollection<MealEntryItem> MealEntryItems { get; set; } = [];
}
