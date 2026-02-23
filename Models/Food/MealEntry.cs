namespace Household.Api.Models.Food;

public class MealEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>Null when entry is still a draft.</summary>
    public DateTime? EatenAt { get; set; }

    /// <summary>
    /// Calculated from EatenAt using configurable time ranges and persisted here
    /// to preserve history even if ranges change in the future.
    /// Only set when Status=Final and EatenAt != null.
    /// </summary>
    public MealType? MealType { get; set; }

    public string? Title { get; set; }
    public Guid? DishTemplateId { get; set; }
    public MealStatus Status { get; set; } = MealStatus.Draft;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Auth.User User { get; set; } = null!;
    public DishTemplate? DishTemplate { get; set; }
    public ICollection<MealEntryItem> Items { get; set; } = [];
}
