namespace Household.Api.Models.Home;

public class TaskTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RoomId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public ScheduleType ScheduleType { get; set; }
    public TimeOfDaySlot TimeOfDaySlot { get; set; } = TimeOfDaySlot.Anytime;

    /// <summary>Bitmask: Sun=1, Mon=2, Tue=4, Wed=8, Thu=16, Fri=32, Sat=64. Used when ScheduleType=Weekly.</summary>
    public int? DaysOfWeekMask { get; set; }

    /// <summary>Day of month (1-31). Used when ScheduleType=Monthly.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Repeat every N days. Used when ScheduleType=IntervalDays.</summary>
    public int? IntervalDays { get; set; }

    public DateOnly StartDate { get; set; }
    public bool CarryOverIfMissed { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Room? Room { get; set; }
    public Auth.User? AssignedToUser { get; set; }
    public ICollection<TaskInstance> Instances { get; set; } = [];
}
