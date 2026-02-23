namespace Household.Api.Models.Food;

public class DishTemplateItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DishTemplateId { get; set; }
    public Guid FoodItemId { get; set; }
    public decimal Grams { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    public DishTemplate DishTemplate { get; set; } = null!;
    public FoodItem FoodItem { get; set; } = null!;
}
