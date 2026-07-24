namespace Household.Api.DTOs;

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string UserName,
    bool IsAdmin,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record AdminCreateUserRequest(
    string Email,
    string UserName,
    string? TemporaryPassword,
    bool IsAdmin = false
);

public sealed record AdminUpdateUserRequest(string Email, string UserName, bool IsAdmin, bool IsActive);
public sealed record TemporaryPasswordDto(Guid UserId, string TemporaryPassword);
public sealed record CreateInvitationRequest(string Email, string UserName, bool IsAdmin = false, int ExpiresInHours = 24);
public sealed record InvitationCreatedDto(Guid Id, string Token, DateTime ExpiresAt);
public sealed record RedeemInvitationRequest(string Token, string Password);
