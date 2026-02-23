using Household.Api.DTOs;

namespace Household.Api.Services;

public interface IDishService
{
    Task<List<DishTemplateDto>> GetAllAsync(Guid requestingUserId);
    Task<DishTemplateDto?> GetByIdAsync(Guid id);
    Task<DishTemplateDto> CreateAsync(CreateDishTemplateRequest request, Guid ownerUserId);
    Task<DishTemplateDto?> UpdateAsync(Guid id, UpdateDishTemplateRequest request, Guid requestingUserId);
    Task<bool> DeleteAsync(Guid id, Guid requestingUserId);
}
