using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Home;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Services;

public class RoomService : IRoomService
{
    private readonly AppDbContext _context;

    public RoomService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<RoomDto>> GetAllAsync() =>
        await _context.Rooms.OrderBy(r => r.Name).Select(r => new RoomDto(r.Id, r.Name, r.CreatedAt)).ToListAsync();

    public async Task<RoomDto?> GetByIdAsync(Guid id)
    {
        var r = await _context.Rooms.FindAsync(id);
        return r == null ? null : new RoomDto(r.Id, r.Name, r.CreatedAt);
    }

    public async Task<RoomDto> CreateAsync(CreateRoomRequest request)
    {
        var room = new Room { Name = request.Name.Trim() };
        _context.Rooms.Add(room);
        await _context.SaveChangesAsync();
        return new RoomDto(room.Id, room.Name, room.CreatedAt);
    }

    public async Task<RoomDto?> UpdateAsync(Guid id, UpdateRoomRequest request)
    {
        var room = await _context.Rooms.FindAsync(id);
        if (room == null)
            return null;

        room.Name = request.Name.Trim();
        await _context.SaveChangesAsync();
        return new RoomDto(room.Id, room.Name, room.CreatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var room = await _context.Rooms.FindAsync(id);
        if (room == null)
            return false;

        _context.Rooms.Remove(room);
        await _context.SaveChangesAsync();
        return true;
    }
}
