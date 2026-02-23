namespace Household.Api.Models.Home;

public class TaskInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskTemplateId { get; set; }
    public DateOnly DueDate { get; set; }
    public TimeOfDaySlot TimeOfDaySlot { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public TaskInstanceStatus Status { get; set; } = TaskInstanceStatus.Pending;
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public TaskTemplate TaskTemplate { get; set; } = null!;
    public Auth.User? AssignedToUser { get; set; }
    public Auth.User? CompletedByUser { get; set; }
}
