using Microsoft.EntityFrameworkCore;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Home;

namespace Household.Api.Services;

public class IssueService : IIssueService
{
    private readonly AppDbContext _context;

    public IssueService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<HomeIssueDto>> GetAllAsync()
    {
        var issues = await _context.HomeIssues
            .Include(hi => hi.Room)
            .Include(hi => hi.CreatedByUser)
            .OrderByDescending(hi => hi.Priority)
            .ThenBy(hi => hi.CreatedAt)
            .ToListAsync();

        return issues.Select(ToDto).ToList();
    }

    public async Task<HomeIssueDto?> GetByIdAsync(Guid id)
    {
        var hi = await _context.HomeIssues
            .Include(h => h.Room)
            .Include(h => h.CreatedByUser)
            .FirstOrDefaultAsync(h => h.Id == id);

        return hi == null ? null : ToDto(hi);
    }

    public async Task<HomeIssueDto> CreateAsync(CreateHomeIssueRequest request, Guid createdByUserId)
    {
        var issue = new HomeIssue
        {
            Title = request.Title.Trim(),
            RoomId = request.RoomId,
            Description = request.Description,
            Status = request.Status,
            Priority = request.Priority,
            CreatedByUserId = createdByUserId
        };

        _context.HomeIssues.Add(issue);
        await _context.SaveChangesAsync();

        return (await GetByIdAsync(issue.Id))!;
    }

    public async Task<HomeIssueDto?> UpdateAsync(Guid id, UpdateHomeIssueRequest request)
    {
        var issue = await _context.HomeIssues.FindAsync(id);
        if (issue == null) return null;

        issue.Title = request.Title.Trim();
        issue.RoomId = request.RoomId;
        issue.Description = request.Description;
        issue.Priority = request.Priority;

        var wasOpen = issue.Status != IssueStatus.Done && issue.Status != IssueStatus.WontFix;
        var isNowClosed = request.Status == IssueStatus.Done || request.Status == IssueStatus.WontFix;

        issue.Status = request.Status;

        if (wasOpen && isNowClosed)
            issue.ResolvedAt ??= DateTime.UtcNow;
        else if (!isNowClosed)
            issue.ResolvedAt = null;

        await _context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var issue = await _context.HomeIssues.FindAsync(id);
        if (issue == null) return false;

        _context.HomeIssues.Remove(issue);
        await _context.SaveChangesAsync();
        return true;
    }

    private static HomeIssueDto ToDto(HomeIssue hi) => new(
        Id: hi.Id,
        Title: hi.Title,
        RoomId: hi.RoomId,
        RoomName: hi.Room?.Name,
        Description: hi.Description,
        Status: hi.Status,
        Priority: hi.Priority,
        CreatedByUserId: hi.CreatedByUserId,
        CreatedByUserName: hi.CreatedByUser?.UserName ?? string.Empty,
        CreatedAt: hi.CreatedAt,
        ResolvedAt: hi.ResolvedAt
    );
}
