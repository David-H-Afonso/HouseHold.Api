using Household.Api.DTOs;

namespace Household.Api.Services;

public interface IFoodItemService
{
    Task<List<FoodItemDto>> GetAllAsync(string? search);
    Task<FoodItemDto?> GetByIdAsync(Guid id);
    Task<FoodItemDto> CreateAsync(CreateFoodItemRequest request, Guid createdByUserId);
    Task<FoodItemDto?> UpdateAsync(Guid id, UpdateFoodItemRequest request);
    Task<bool> DeleteAsync(Guid id);
}
