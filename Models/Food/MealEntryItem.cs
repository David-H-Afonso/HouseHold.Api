namespace Household.Api.Models.Food;

public class MealEntryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MealEntryId { get; set; }
    public Guid FoodItemId { get; set; }
    public decimal Grams { get; set; }

    // Navigation
    public MealEntry MealEntry { get; set; } = null!;
    public FoodItem FoodItem { get; set; } = null!;
}
