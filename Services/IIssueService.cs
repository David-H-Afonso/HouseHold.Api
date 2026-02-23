using Household.Api.DTOs;

namespace Household.Api.Services;

public interface IIssueService
{
    Task<List<HomeIssueDto>> GetAllAsync();
    Task<HomeIssueDto?> GetByIdAsync(Guid id);
    Task<HomeIssueDto> CreateAsync(CreateHomeIssueRequest request, Guid createdByUserId);
    Task<HomeIssueDto?> UpdateAsync(Guid id, UpdateHomeIssueRequest request);
    Task<bool> DeleteAsync(Guid id);
}
