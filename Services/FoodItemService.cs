using Microsoft.EntityFrameworkCore;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Food;

namespace Household.Api.Services;

public class FoodItemService : IFoodItemService
{
    private readonly AppDbContext _context;

    public FoodItemService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<FoodItemDto>> GetAllAsync(string? search)
    {
        var query = _context.FoodItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = NormalizeName(search);
            query = query.Where(f => f.NameNormalized.Contains(normalized));
        }

        return await query
            .OrderBy(f => f.Name)
            .Select(f => ToDto(f))
            .ToListAsync();
    }

    public async Task<FoodItemDto?> GetByIdAsync(Guid id)
    {
        var f = await _context.FoodItems.FindAsync(id);
        return f == null ? null : ToDto(f);
    }

    public async Task<FoodItemDto> CreateAsync(CreateFoodItemRequest request, Guid createdByUserId)
    {
        var item = new FoodItem
        {
            Name = request.Name.Trim(),
            NameNormalized = NormalizeName(request.Name),
            KcalPer100g = Math.Round(request.KcalPer100g, 2),
            ProteinPer100g = Math.Round(request.ProteinPer100g, 2),
            CarbsPer100g = Math.Round(request.CarbsPer100g, 2),
            FatPer100g = Math.Round(request.FatPer100g, 2),
            CreatedByUserId = createdByUserId
        };

        _context.FoodItems.Add(item);
        await _context.SaveChangesAsync();
        return ToDto(item);
    }

    public async Task<FoodItemDto?> UpdateAsync(Guid id, UpdateFoodItemRequest request)
    {
        var item = await _context.FoodItems.FindAsync(id);
        if (item == null) return null;

        item.Name = request.Name.Trim();
        item.NameNormalized = NormalizeName(request.Name);
        item.KcalPer100g = Math.Round(request.KcalPer100g, 2);
        item.ProteinPer100g = Math.Round(request.ProteinPer100g, 2);
        item.CarbsPer100g = Math.Round(request.CarbsPer100g, 2);
        item.FatPer100g = Math.Round(request.FatPer100g, 2);

        await _context.SaveChangesAsync();
        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var item = await _context.FoodItems.FindAsync(id);
        if (item == null) return false;

        _context.FoodItems.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }

    private static string NormalizeName(string name)
    {
        // 1. Trim + collapse multiple whitespace to a single space, then uppercase (invariant)
        var s = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();
        // 2. Decompose Unicode + strip diacritic marks (á→a, ñ→n, ü→u, etc.)
        return new string(
            s.Normalize(System.Text.NormalizationForm.FormD)
             .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                         != System.Globalization.UnicodeCategory.NonSpacingMark)
             .ToArray());
    }

    private static FoodItemDto ToDto(FoodItem f) => new(
        f.Id, f.Name, f.KcalPer100g, f.ProteinPer100g,
        f.CarbsPer100g, f.FatPer100g, f.CreatedByUserId,
        f.CreatedAt, f.UpdatedAt);
}
