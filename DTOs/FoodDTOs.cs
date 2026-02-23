using Household.Api.Models.Food;

namespace Household.Api.DTOs;

// ── FoodItem ──────────────────────────────────────────────────────────────────

public record CreateFoodItemRequest(
    string Name,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g
);

public record UpdateFoodItemRequest(
    string Name,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g
);

public record FoodItemDto(
    Guid Id,
    string Name,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── DishTemplate ──────────────────────────────────────────────────────────────

public record DishTemplateItemRequest(Guid FoodItemId, decimal Grams, int SortOrder = 0);

public record CreateDishTemplateRequest(
    string Name,
    bool IsShared = false,
    List<DishTemplateItemRequest>? Items = null
);

public record UpdateDishTemplateRequest(string Name, bool IsShared, List<DishTemplateItemRequest>? Items = null);

public record DishTemplateItemDto(
    Guid Id,
    Guid FoodItemId,
    string FoodItemName,
    decimal Grams,
    int SortOrder,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g
);

public record DishTemplateDto(
    Guid Id,
    string Name,
    Guid? OwnerUserId,
    bool IsShared,
    List<DishTemplateItemDto> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── MealEntry ─────────────────────────────────────────────────────────────────

public record MealEntryItemRequest(Guid FoodItemId, decimal Grams);

public record CreateMealEntryRequest(
    DateTime? EatenAt,
    string? Title,
    Guid? DishTemplateId,
    MealStatus Status,
    string? Notes,
    List<MealEntryItemRequest>? Items = null
);

public record UpdateMealEntryRequest(
    DateTime? EatenAt,
    string? Title,
    Guid? DishTemplateId,
    MealStatus Status,
    string? Notes,
    List<MealEntryItemRequest>? Items = null
);

public record MealEntryItemDto(
    Guid Id,
    Guid FoodItemId,
    string FoodItemName,
    decimal Grams,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g
);

public record MealEntryDto(
    Guid Id,
    Guid UserId,
    DateTime? EatenAt,
    MealType? MealType,
    string? Title,
    Guid? DishTemplateId,
    string? DishTemplateName,
    MealStatus Status,
    string? Notes,
    List<MealEntryItemDto> Items,
    decimal TotalKcal,
    decimal TotalProtein,
    decimal TotalCarbs,
    decimal TotalFat,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
