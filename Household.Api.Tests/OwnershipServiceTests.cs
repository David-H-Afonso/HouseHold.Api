using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Models.Food;
using Household.Api.Models.Home;
using Household.Api.Services;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class OwnershipServiceTests
{
    [Fact]
    public async Task UserBCannotReadUserAMealOrPrivateDish()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        var dish = new DishTemplate { Name = "Private", OwnerUserId = userA.Id };
        var meal = new MealEntry { UserId = userA.Id, Title = "Secret" };
        fixture.Db.AddRange(dish, meal);
        await fixture.Db.SaveChangesAsync();
        var mealService = new MealService(fixture.Db, new MealTypeHelper(Options.Create(new Configuration.MealTypeSettings())));
        var dishService = new DishService(fixture.Db);

        Assert.Null(await mealService.GetByIdAsync(meal.Id, userB.Id));
        Assert.Null(await dishService.GetByIdAsync(dish.Id, userB.Id));
        Assert.NotNull(await mealService.GetByIdAsync(meal.Id, userA.Id));
        Assert.NotNull(await dishService.GetByIdAsync(dish.Id, userA.Id));
    }

    [Fact]
    public async Task UserBCannotCompleteUserATaskInstance()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        var template = new TaskTemplate
        {
            OwnerUserId = userA.Id,
            Title = "Private task",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleType = ScheduleType.Daily,
        };
        var instance = new TaskInstance
        {
            TaskTemplate = template,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
        };
        fixture.Db.Add(instance);
        await fixture.Db.SaveChangesAsync();
        var service = new TaskService(fixture.Db);

        Assert.Null(await service.CompleteTaskInstanceAsync(instance.Id, userB.Id, null));
        Assert.NotNull(await service.CompleteTaskInstanceAsync(instance.Id, userA.Id, null));
    }

    [Fact]
    public async Task UserACannotAttachUserBPrivateDishToMeal()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        var dish = new DishTemplate { Name = "B private", OwnerUserId = userB.Id };
        fixture.Db.Add(dish);
        await fixture.Db.SaveChangesAsync();
        var service = new MealService(fixture.Db, new MealTypeHelper(Options.Create(new Configuration.MealTypeSettings())));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            new CreateMealEntryRequest(null, "Meal", dish.Id, MealStatus.Draft, null), userA.Id));
    }

    [Fact]
    public async Task TaskCannotBeAssignedToUnknownUserId()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("a@example.test");
        var service = new TaskService(fixture.Db);
        var request = new CreateTaskTemplateRequest(
            "Task", null, null, Guid.NewGuid(), ScheduleType.Daily, TimeOfDaySlot.Anytime,
            null, null, null, DateOnly.FromDateTime(DateTime.UtcNow), false, true
        );

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTemplateAsync(request, user.Id));
    }
}
