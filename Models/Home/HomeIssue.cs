namespace Household.Api.Models.Home;

public class HomeIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RoomId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.Open;
    public int Priority { get; set; } = 0;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Navigation
    public Room? Room { get; set; }
    public Auth.User CreatedByUser { get; set; } = null!;
}
