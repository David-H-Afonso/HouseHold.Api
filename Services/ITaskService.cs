using Household.Api.DTOs;

namespace Household.Api.Services;

public interface ITaskService
{
    Task<List<TaskTemplateDto>> GetAllTemplatesAsync();
    Task<TaskTemplateDto?> GetTemplateByIdAsync(Guid id);
    Task<TaskTemplateDto> CreateTemplateAsync(CreateTaskTemplateRequest request);
    Task<TaskTemplateDto?> UpdateTemplateAsync(Guid id, UpdateTaskTemplateRequest request);
    Task<bool> DeleteTemplateAsync(Guid id);

    /// <summary>
    /// Idempotently generates TaskInstances for today and returns them grouped by slot.
    /// Includes overdue Pending instances.
    /// </summary>
    Task<TodayTasksResponse> GetTodayTasksAsync();

    Task<TaskInstanceDto?> CompleteTaskInstanceAsync(Guid instanceId, Guid completedByUserId, string? notes);
}
