using Microsoft.EntityFrameworkCore;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Models.Food;

namespace Household.Api.Services;

public class MealService : IMealService
{
    private readonly AppDbContext _context;
    private readonly IMealTypeHelper _mealTypeHelper;

    public MealService(AppDbContext context, IMealTypeHelper mealTypeHelper)
    {
        _context = context;
        _mealTypeHelper = mealTypeHelper;
    }

    public async Task<List<MealEntryDto>> GetAllAsync(Guid userId, DateTime? from, DateTime? to)
    {
        var query = _context.MealEntries
            .Include(me => me.Items)
                .ThenInclude(i => i.FoodItem)
            .Include(me => me.DishTemplate)
            .Where(me => me.UserId == userId);

        if (from.HasValue) query = query.Where(me => me.EatenAt == null || me.EatenAt >= from);
        if (to.HasValue) query = query.Where(me => me.EatenAt == null || me.EatenAt <= to);

        var entries = await query
            .OrderByDescending(me => me.EatenAt ?? me.CreatedAt)
            .ToListAsync();

        return entries.Select(ToDto).ToList();
    }

    public async Task<MealEntryDto?> GetByIdAsync(Guid id)
    {
        var entry = await LoadEntryAsync(id);
        return entry == null ? null : ToDto(entry);
    }

    public async Task<MealEntryDto> CreateAsync(CreateMealEntryRequest request, Guid userId)
    {
        var entry = new MealEntry
        {
            UserId = userId,
            EatenAt = request.EatenAt,
            Title = request.Title,
            DishTemplateId = request.DishTemplateId,
            Status = request.Status,
            Notes = request.Notes,
            MealType = ResolveMealType(request.EatenAt, request.Status)
        };

        if (request.Items != null)
        {
            entry.Items = request.Items.Select(i => new MealEntryItem
            {
                FoodItemId = i.FoodItemId,
                Grams = i.Grams
            }).ToList();
        }

        _context.MealEntries.Add(entry);
        await _context.SaveChangesAsync();

        return ToDto((await LoadEntryAsync(entry.Id))!);
    }

    public async Task<MealEntryDto?> UpdateAsync(Guid id, UpdateMealEntryRequest request, Guid userId)
    {
        var entry = await _context.MealEntries
            .Include(me => me.Items)
            .FirstOrDefaultAsync(me => me.Id == id && me.UserId == userId);

        if (entry == null) return null;

        var eatenAtChanged = entry.EatenAt != request.EatenAt;
        var statusChanged = entry.Status != request.Status;

        entry.EatenAt = request.EatenAt;
        entry.Title = request.Title;
        entry.DishTemplateId = request.DishTemplateId;
        entry.Status = request.Status;
        entry.Notes = request.Notes;

        // Recalculate MealType if EatenAt or Status changed
        if (eatenAtChanged || statusChanged)
        {
            entry.MealType = ResolveMealType(request.EatenAt, request.Status);
        }

        if (request.Items != null)
        {
            _context.MealEntryItems.RemoveRange(entry.Items);
            entry.Items = request.Items.Select(i => new MealEntryItem
            {
                MealEntryId = entry.Id,
                FoodItemId = i.FoodItemId,
                Grams = i.Grams
            }).ToList();
        }

        await _context.SaveChangesAsync();
        return ToDto((await LoadEntryAsync(id))!);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        var entry = await _context.MealEntries
            .FirstOrDefaultAsync(me => me.Id == id && me.UserId == userId);

        if (entry == null) return false;

        _context.MealEntries.Remove(entry);
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private MealType? ResolveMealType(DateTime? eatenAt, MealStatus status)
    {
        if (status != MealStatus.Final || eatenAt == null)
            return null;

        return _mealTypeHelper.ResolveMealType(eatenAt.Value);
    }

    private async Task<MealEntry?> LoadEntryAsync(Guid id) =>
        await _context.MealEntries
            .Include(me => me.Items)
                .ThenInclude(i => i.FoodItem)
            .Include(me => me.DishTemplate)
            .FirstOrDefaultAsync(me => me.Id == id);

    private static MealEntryDto ToDto(MealEntry me)
    {
        var items = me.Items.Select(i => new MealEntryItemDto(
            Id: i.Id,
            FoodItemId: i.FoodItemId,
            FoodItemName: i.FoodItem?.Name ?? string.Empty,
            Grams: i.Grams,
            KcalPer100g: i.FoodItem?.KcalPer100g ?? 0,
            ProteinPer100g: i.FoodItem?.ProteinPer100g ?? 0,
            CarbsPer100g: i.FoodItem?.CarbsPer100g ?? 0,
            FatPer100g: i.FoodItem?.FatPer100g ?? 0
        )).ToList();

        decimal totalKcal = items.Sum(i => i.Grams / 100m * i.KcalPer100g);
        decimal totalProtein = items.Sum(i => i.Grams / 100m * i.ProteinPer100g);
        decimal totalCarbs = items.Sum(i => i.Grams / 100m * i.CarbsPer100g);
        decimal totalFat = items.Sum(i => i.Grams / 100m * i.FatPer100g);

        return new MealEntryDto(
            Id: me.Id,
            UserId: me.UserId,
            EatenAt: me.EatenAt,
            MealType: me.MealType,
            Title: me.Title,
            DishTemplateId: me.DishTemplateId,
            DishTemplateName: me.DishTemplate?.Name,
            Status: me.Status,
            Notes: me.Notes,
            Items: items,
            TotalKcal: Math.Round(totalKcal, 1),
            TotalProtein: Math.Round(totalProtein, 1),
            TotalCarbs: Math.Round(totalCarbs, 1),
            TotalFat: Math.Round(totalFat, 1),
            CreatedAt: me.CreatedAt,
            UpdatedAt: me.UpdatedAt
        );
    }
}
