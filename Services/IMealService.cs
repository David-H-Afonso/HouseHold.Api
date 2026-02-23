using Household.Api.DTOs;

namespace Household.Api.Services;

public interface IMealService
{
    Task<List<MealEntryDto>> GetAllAsync(Guid userId, DateTime? from, DateTime? to);
    Task<MealEntryDto?> GetByIdAsync(Guid id);
    Task<MealEntryDto> CreateAsync(CreateMealEntryRequest request, Guid userId);
    Task<MealEntryDto?> UpdateAsync(Guid id, UpdateMealEntryRequest request, Guid userId);
    Task<bool> DeleteAsync(Guid id, Guid userId);
}
