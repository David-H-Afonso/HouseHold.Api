using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Food;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Services;

public class DishService : IDishService
{
    private readonly AppDbContext _context;

    public DishService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DishTemplateDto>> GetAllAsync(Guid requestingUserId)
    {
        var dishes = await _context
            .DishTemplates.Include(d => d.Items)
                .ThenInclude(i => i.FoodItem)
            .Where(d => d.IsShared || d.OwnerUserId == null || d.OwnerUserId == requestingUserId)
            .OrderBy(d => d.Name)
            .ToListAsync();

        return dishes.Select(ToDto).ToList();
    }

    public async Task<DishTemplateDto?> GetByIdAsync(Guid id)
    {
        var dish = await _context
            .DishTemplates.Include(d => d.Items)
                .ThenInclude(i => i.FoodItem)
            .FirstOrDefaultAsync(d => d.Id == id);

        return dish == null ? null : ToDto(dish);
    }

    public async Task<DishTemplateDto> CreateAsync(CreateDishTemplateRequest request, Guid ownerUserId)
    {
        var dish = new DishTemplate
        {
            Name = request.Name.Trim(),
            OwnerUserId = ownerUserId,
            IsShared = request.IsShared,
        };

        if (request.Items != null)
        {
            dish.Items = request
                .Items.Select(
                    (i, idx) =>
                        new DishTemplateItem
                        {
                            FoodItemId = i.FoodItemId,
                            Grams = i.Grams,
                            SortOrder = i.SortOrder == 0 ? idx : i.SortOrder,
                        }
                )
                .ToList();
        }

        _context.DishTemplates.Add(dish);
        await _context.SaveChangesAsync();

        // Reload with navigation props
        return (await GetByIdAsync(dish.Id))!;
    }

    public async Task<DishTemplateDto?> UpdateAsync(Guid id, UpdateDishTemplateRequest request, Guid requestingUserId)
    {
        var dish = await _context.DishTemplates.Include(d => d.Items).FirstOrDefaultAsync(d => d.Id == id);

        if (dish == null)
            return null;

        // Only owner or a shared dish (editable by anyone) can be updated
        if (dish.OwnerUserId != null && dish.OwnerUserId != requestingUserId)
            throw new UnauthorizedAccessException("You can only edit your own dishes.");

        dish.Name = request.Name.Trim();
        dish.IsShared = request.IsShared;

        if (request.Items != null)
        {
            _context.DishTemplateItems.RemoveRange(dish.Items);
            dish.Items = request
                .Items.Select(
                    (i, idx) =>
                        new DishTemplateItem
                        {
                            DishTemplateId = dish.Id,
                            FoodItemId = i.FoodItemId,
                            Grams = i.Grams,
                            SortOrder = i.SortOrder == 0 ? idx : i.SortOrder,
                        }
                )
                .ToList();
        }

        await _context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid requestingUserId)
    {
        var dish = await _context.DishTemplates.FindAsync(id);
        if (dish == null)
            return false;

        if (dish.OwnerUserId != null && dish.OwnerUserId != requestingUserId)
            throw new UnauthorizedAccessException("You can only delete your own dishes.");

        _context.DishTemplates.Remove(dish);
        await _context.SaveChangesAsync();
        return true;
    }

    private static DishTemplateDto ToDto(DishTemplate d) =>
        new(
            Id: d.Id,
            Name: d.Name,
            OwnerUserId: d.OwnerUserId,
            IsShared: d.IsShared,
            Items: d.Items.OrderBy(i => i.SortOrder)
                .Select(i => new DishTemplateItemDto(
                    Id: i.Id,
                    FoodItemId: i.FoodItemId,
                    FoodItemName: i.FoodItem?.Name ?? string.Empty,
                    Grams: i.Grams,
                    SortOrder: i.SortOrder,
                    KcalPer100g: i.FoodItem?.KcalPer100g ?? 0,
                    ProteinPer100g: i.FoodItem?.ProteinPer100g ?? 0,
                    CarbsPer100g: i.FoodItem?.CarbsPer100g ?? 0,
                    FatPer100g: i.FoodItem?.FatPer100g ?? 0
                ))
                .ToList(),
            CreatedAt: d.CreatedAt,
            UpdatedAt: d.UpdatedAt
        );
}
