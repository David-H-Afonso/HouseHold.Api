namespace Household.Api.Configuration;

/// <summary>
/// Configurable time-of-day ranges for MealType classification.
/// All times use 24-hour format (e.g. "07:00", "10:30").
/// </summary>
public class MealTypeSettings
{
    public const string SectionName = "MealTypeSettings";

    public string BreakfastStart { get; set; } = "07:00";
    public string BreakfastEnd { get; set; } = "10:30";

    public string MorningSnackStart { get; set; } = "10:30";
    public string MorningSnackEnd { get; set; } = "12:00";

    public string LunchStart { get; set; } = "12:00";
    public string LunchEnd { get; set; } = "15:30";

    public string AfternoonSnackStart { get; set; } = "15:30";
    public string AfternoonSnackEnd { get; set; } = "20:00";

    public string DinnerStart { get; set; } = "20:00";
    public string DinnerEnd { get; set; } = "23:59";
}
