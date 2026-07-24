using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IUserAdministrationService
{
    Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken cancellationToken);
    Task<(AdminUserDto? User, string? TemporaryPassword, string? Error)> CreateUserAsync(
        Guid actorUserId,
        AdminCreateUserRequest request,
        CancellationToken cancellationToken
    );
    Task<(AdminUserDto? User, string? Error)> UpdateUserAsync(
        Guid actorUserId,
        Guid userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken
    );
    Task<(string? TemporaryPassword, string? Error)> ResetPasswordAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken
    );
    Task<(InvitationCreatedDto? Invitation, string? Error)> CreateInvitationAsync(
        Guid actorUserId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken
    );
    Task<(AdminUserDto? User, string? Error)> RedeemInvitationAsync(
        RedeemInvitationRequest request,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<object>> GetAuditEventsAsync(CancellationToken cancellationToken);
}
