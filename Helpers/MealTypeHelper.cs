using Household.Api.Configuration;
using Household.Api.Models.Food;
using Microsoft.Extensions.Options;

namespace Household.Api.Helpers;

public interface IMealTypeHelper
{
    MealType? ResolveMealType(DateTime eatenAt);
}

public class MealTypeHelper : IMealTypeHelper
{
    private readonly MealTypeSettings _settings;

    public MealTypeHelper(IOptions<MealTypeSettings> settings)
    {
        _settings = settings.Value;
    }

    public MealType? ResolveMealType(DateTime eatenAt)
    {
        var time = TimeOnly.FromDateTime(eatenAt);

        if (InRange(time, _settings.BreakfastStart, _settings.BreakfastEnd))
            return MealType.Breakfast;

        if (InRange(time, _settings.MorningSnackStart, _settings.MorningSnackEnd))
            return MealType.MorningSnack;

        if (InRange(time, _settings.LunchStart, _settings.LunchEnd))
            return MealType.Lunch;

        if (InRange(time, _settings.AfternoonSnackStart, _settings.AfternoonSnackEnd))
            return MealType.AfternoonSnack;

        if (InRange(time, _settings.DinnerStart, _settings.DinnerEnd))
            return MealType.Dinner;

        return MealType.Other;
    }

    private static bool InRange(TimeOnly time, string startStr, string endStr)
    {
        if (!TimeOnly.TryParse(startStr, out var start) || !TimeOnly.TryParse(endStr, out var end))
            return false;

        // Cross-midnight range (e.g. Dinner 20:00 – 07:30): end wraps to next day
        if (end <= start)
            return time >= start || time < end;

        return time >= start && time < end;
    }
}
