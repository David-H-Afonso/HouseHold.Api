namespace Household.Api.Models.Home;

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public ICollection<TaskTemplate> TaskTemplates { get; set; } = [];
    public ICollection<HomeIssue> HomeIssues { get; set; } = [];
}
