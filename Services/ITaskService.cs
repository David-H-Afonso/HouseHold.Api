using Household.Api.DTOs;

namespace Household.Api.Services;

public interface ITaskService
{
    Task<List<TaskTemplateDto>> GetAllTemplatesAsync(Guid userId);
    Task<TaskTemplateDto?> GetTemplateByIdAsync(Guid id, Guid userId);
    Task<TaskTemplateDto> CreateTemplateAsync(CreateTaskTemplateRequest request, Guid userId);
    Task<TaskTemplateDto?> UpdateTemplateAsync(Guid id, UpdateTaskTemplateRequest request, Guid userId);
    Task<bool> DeleteTemplateAsync(Guid id, Guid userId);

    /// <summary>
    /// Idempotently generates TaskInstances for today and returns them grouped by slot.
    /// Includes overdue Pending instances.
    /// </summary>
    Task<TodayTasksResponse> GetTodayTasksAsync(Guid userId);

    Task<TaskInstanceDto?> CompleteTaskInstanceAsync(Guid instanceId, Guid completedByUserId, string? notes);
}
