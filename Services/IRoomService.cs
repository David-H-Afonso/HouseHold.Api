using Household.Api.DTOs;

namespace Household.Api.Services;

public interface IRoomService
{
    Task<List<RoomDto>> GetAllAsync();
    Task<RoomDto?> GetByIdAsync(Guid id);
    Task<RoomDto> CreateAsync(CreateRoomRequest request);
    Task<RoomDto?> UpdateAsync(Guid id, UpdateRoomRequest request);
    Task<bool> DeleteAsync(Guid id);
}
