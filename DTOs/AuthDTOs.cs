namespace Household.Api.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken, string? DeviceName = null);

public record LogoutRequest(string RefreshToken);

public record CreateUserRequest(
    string Email,
    string UserName,
    string Password,
    bool IsAdmin = false
);

// ── Responses ─────────────────────────────────────────────────────────────────

public record LoginResponse(
    Guid UserId,
    string Email,
    string UserName,
    bool IsAdmin,
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt
);

public record RefreshResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt
);

public record UserDto(
    Guid Id,
    string Email,
    string UserName,
    bool IsAdmin,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record MeResponse(
    Guid UserId,
    string Email,
    string UserName,
    bool IsAdmin
);
