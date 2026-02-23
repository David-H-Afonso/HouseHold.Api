using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Home;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Services;

public class TaskService : ITaskService
{
    private readonly AppDbContext _context;

    public TaskService(AppDbContext context)
    {
        _context = context;
    }

    // ── TaskTemplate CRUD ─────────────────────────────────────────────────────

    public async Task<List<TaskTemplateDto>> GetAllTemplatesAsync()
    {
        var templates = await _context
            .TaskTemplates.Include(tt => tt.Room)
            .Include(tt => tt.AssignedToUser)
            .OrderBy(tt => tt.Title)
            .ToListAsync();

        return templates.Select(ToTemplateDto).ToList();
    }

    public async Task<TaskTemplateDto?> GetTemplateByIdAsync(Guid id)
    {
        var tt = await _context
            .TaskTemplates.Include(t => t.Room)
            .Include(t => t.AssignedToUser)
            .FirstOrDefaultAsync(t => t.Id == id);

        return tt == null ? null : ToTemplateDto(tt);
    }

    public async Task<TaskTemplateDto> CreateTemplateAsync(CreateTaskTemplateRequest request)
    {
        var tt = new TaskTemplate
        {
            Title = request.Title.Trim(),
            RoomId = request.RoomId,
            Description = request.Description,
            AssignedToUserId = request.AssignedToUserId,
            ScheduleType = request.ScheduleType,
            TimeOfDaySlot = request.TimeOfDaySlot,
            DaysOfWeekMask = request.DaysOfWeekMask,
            DayOfMonth = request.DayOfMonth,
            IntervalDays = request.IntervalDays,
            StartDate = request.StartDate,
            CarryOverIfMissed = request.CarryOverIfMissed,
            IsActive = request.IsActive,
        };

        _context.TaskTemplates.Add(tt);
        await _context.SaveChangesAsync();
        return (await GetTemplateByIdAsync(tt.Id))!;
    }

    public async Task<TaskTemplateDto?> UpdateTemplateAsync(Guid id, UpdateTaskTemplateRequest request)
    {
        var tt = await _context.TaskTemplates.FindAsync(id);
        if (tt == null)
            return null;

        tt.Title = request.Title.Trim();
        tt.RoomId = request.RoomId;
        tt.Description = request.Description;
        tt.AssignedToUserId = request.AssignedToUserId;
        tt.ScheduleType = request.ScheduleType;
        tt.TimeOfDaySlot = request.TimeOfDaySlot;
        tt.DaysOfWeekMask = request.DaysOfWeekMask;
        tt.DayOfMonth = request.DayOfMonth;
        tt.IntervalDays = request.IntervalDays;
        tt.StartDate = request.StartDate;
        tt.CarryOverIfMissed = request.CarryOverIfMissed;
        tt.IsActive = request.IsActive;

        await _context.SaveChangesAsync();
        return await GetTemplateByIdAsync(id);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id)
    {
        var tt = await _context.TaskTemplates.FindAsync(id);
        if (tt == null)
            return false;

        _context.TaskTemplates.Remove(tt);
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Today's tasks ─────────────────────────────────────────────────────────

    public async Task<TodayTasksResponse> GetTodayTasksAsync()
    {
        // Respect TZ env var (Linux/Docker). Falls back to local time if unset or unknown.
        var tzId = Environment.GetEnvironmentVariable("TZ");
        TimeZoneInfo tz;
        try
        {
            tz = string.IsNullOrEmpty(tzId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            tz = TimeZoneInfo.Local;
        }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));

        var templates = await _context
            .TaskTemplates.Include(tt => tt.Room)
            .Include(tt => tt.AssignedToUser)
            .Where(tt => tt.IsActive)
            .ToListAsync();

        var todayTemplateIds = templates.Where(tt => AppliesToDate(tt, today)).Select(tt => tt.Id).ToHashSet();

        // Ensure instances exist for today's applicable templates
        var existingToday = await _context
            .TaskInstances.Where(ti => ti.DueDate == today && todayTemplateIds.Contains(ti.TaskTemplateId))
            .Select(ti => ti.TaskTemplateId)
            .ToHashSetAsync();

        var toCreate = todayTemplateIds
            .Except(existingToday)
            .Select(templateId =>
            {
                var tt = templates.First(t => t.Id == templateId);
                return new TaskInstance
                {
                    TaskTemplateId = templateId,
                    DueDate = today,
                    TimeOfDaySlot = tt.TimeOfDaySlot,
                    AssignedToUserId = tt.AssignedToUserId,
                    Status = TaskInstanceStatus.Pending,
                };
            })
            .ToList();

        if (toCreate.Count > 0)
        {
            _context.TaskInstances.AddRange(toCreate);
            await _context.SaveChangesAsync();
        }

        // Load today instances + overdue pending ones
        var allInstances = await _context
            .TaskInstances.Include(ti => ti.TaskTemplate)
                .ThenInclude(tt => tt.Room)
            .Include(ti => ti.AssignedToUser)
            .Where(ti => (ti.DueDate == today) || (ti.DueDate < today && ti.Status == TaskInstanceStatus.Pending))
            .ToListAsync();

        var todayInstances = allInstances.Where(ti => ti.DueDate == today).ToList();

        var overdueInstances = allInstances.Where(ti => ti.DueDate < today).OrderBy(ti => ti.DueDate).ToList();

        return new TodayTasksResponse(
            Date: today,
            Morning: todayInstances
                .Where(ti => ti.TimeOfDaySlot == TimeOfDaySlot.Morning)
                .Select(ti => ToInstanceDto(ti, today))
                .ToList(),
            Night: todayInstances
                .Where(ti => ti.TimeOfDaySlot == TimeOfDaySlot.Night)
                .Select(ti => ToInstanceDto(ti, today))
                .ToList(),
            Anytime: todayInstances
                .Where(ti => ti.TimeOfDaySlot == TimeOfDaySlot.Anytime)
                .Select(ti => ToInstanceDto(ti, today))
                .ToList(),
            Overdue: overdueInstances.Select(ti => ToInstanceDto(ti, today)).ToList()
        );
    }

    public async Task<TaskInstanceDto?> CompleteTaskInstanceAsync(
        Guid instanceId,
        Guid completedByUserId,
        string? notes
    )
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var instance = await _context
            .TaskInstances.Include(ti => ti.TaskTemplate)
                .ThenInclude(tt => tt.Room)
            .Include(ti => ti.AssignedToUser)
            .FirstOrDefaultAsync(ti => ti.Id == instanceId);

        if (instance == null)
            return null;

        instance.Status = TaskInstanceStatus.Completed;
        instance.CompletedAt = DateTime.UtcNow;
        instance.CompletedByUserId = completedByUserId;
        if (notes != null)
            instance.Notes = notes;

        await _context.SaveChangesAsync();
        return ToInstanceDto(instance, today);
    }

    // ── Schedule logic ────────────────────────────────────────────────────────

    private static bool AppliesToDate(TaskTemplate tt, DateOnly date)
    {
        if (date < tt.StartDate)
            return false;

        return tt.ScheduleType switch
        {
            ScheduleType.Daily => true,

            ScheduleType.Weekly when tt.DaysOfWeekMask.HasValue => (
                tt.DaysOfWeekMask.Value & (1 << (int)date.DayOfWeek)
            ) != 0,

            ScheduleType.Monthly when tt.DayOfMonth.HasValue => date.Day == tt.DayOfMonth.Value,

            ScheduleType.IntervalDays when tt.IntervalDays.HasValue => (date.DayNumber - tt.StartDate.DayNumber)
                % tt.IntervalDays.Value
                == 0,

            _ => false,
        };
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static TaskTemplateDto ToTemplateDto(TaskTemplate tt) =>
        new(
            Id: tt.Id,
            Title: tt.Title,
            RoomId: tt.RoomId,
            RoomName: tt.Room?.Name,
            Description: tt.Description,
            AssignedToUserId: tt.AssignedToUserId,
            AssignedToUserName: tt.AssignedToUser?.UserName,
            IsActive: tt.IsActive,
            ScheduleType: tt.ScheduleType,
            TimeOfDaySlot: tt.TimeOfDaySlot,
            DaysOfWeekMask: tt.DaysOfWeekMask,
            DayOfMonth: tt.DayOfMonth,
            IntervalDays: tt.IntervalDays,
            StartDate: tt.StartDate,
            CarryOverIfMissed: tt.CarryOverIfMissed,
            CreatedAt: tt.CreatedAt,
            UpdatedAt: tt.UpdatedAt
        );

    private static TaskInstanceDto ToInstanceDto(TaskInstance ti, DateOnly today) =>
        new(
            Id: ti.Id,
            TaskTemplateId: ti.TaskTemplateId,
            TaskTitle: ti.TaskTemplate?.Title ?? string.Empty,
            RoomName: ti.TaskTemplate?.Room?.Name,
            DueDate: ti.DueDate,
            TimeOfDaySlot: ti.TimeOfDaySlot,
            AssignedToUserId: ti.AssignedToUserId,
            AssignedToUserName: ti.AssignedToUser?.UserName,
            Status: ti.Status,
            CompletedAt: ti.CompletedAt,
            CompletedByUserId: ti.CompletedByUserId,
            Notes: ti.Notes,
            IsOverdue: ti.DueDate < today && ti.Status == TaskInstanceStatus.Pending
        );
}
