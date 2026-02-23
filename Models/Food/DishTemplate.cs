namespace Household.Api.Models.Food;

public class DishTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Null means the dish belongs to the household (shared by all).</summary>
    public Guid? OwnerUserId { get; set; }
    public bool IsShared { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Auth.User? OwnerUser { get; set; }
    public ICollection<DishTemplateItem> Items { get; set; } = [];
    public ICollection<MealEntry> MealEntries { get; set; } = [];
}
