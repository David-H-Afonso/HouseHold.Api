using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Household.Api.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Household.Api.Services;

public class AuthService : IAuthService
{
    private static readonly SemaphoreSlim SessionGate = new(1, 1);
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("Household!TimingOnly9", workFactor: 12);
    private readonly AppDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext context, IOptions<JwtSettings> jwtSettings, ILogger<AuthService> logger)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(string email, string password, string? userAgent, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) || email.Length > 320 || password.Length > 1024)
            return null;
        await SessionGate.WaitAsync();
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() && u.IsActive);

            var passwordMatches = VerifyPassword(password, user?.PasswordHash ?? DummyPasswordHash);
            if (user == null || !passwordMatches)
                return null;

            var (accessToken, accessExpires) = GenerateAccessToken(user);
            var (rawRefresh, refreshToken) = await CreateRefreshTokenAsync(user.Id, userAgent, deviceName);

            return new LoginResponse(
                UserId: user.Id,
                Email: user.Email,
                UserName: user.UserName,
                IsAdmin: user.IsAdmin,
                RequiresPasswordChange: user.RequiresPasswordChange,
                AccessToken: accessToken,
                RefreshToken: rawRefresh,
                AccessTokenExpiresAt: accessExpires
            );
        }
        finally
        {
            SessionGate.Release();
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task<RefreshResponse?> RefreshAsync(string rawRefreshToken, string? userAgent, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken) || rawRefreshToken.Length > 1024)
            return null;
        await SessionGate.WaitAsync();
        try
        {
            var tokenHash = HashToken(rawRefreshToken);

            var existing = await _context
                .RefreshTokens.Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (existing == null)
                return null;

        // Refresh token reuse detection: if token is already revoked → possible theft
            if (!existing.IsActive)
            {
                _logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}. Revoking all active tokens.",
                    existing.UserId
                );
                var allActive = await _context
                    .RefreshTokens.Where(rt => rt.UserId == existing.UserId && rt.RevokedAt == null)
                    .ToListAsync();
                foreach (var t in allActive)
                    t.RevokedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return null;
            }

            if (!existing.User.IsActive || existing.User.RequiresPasswordChange)
                return null;

        // Revoke old token
            existing.RevokedAt = DateTime.UtcNow;

        // Issue new refresh token
            var (newRawRefresh, newRefreshToken) = await CreateRefreshTokenAsync(existing.UserId, userAgent, deviceName);

        // Link for audit trail
            existing.ReplacedByTokenId = newRefreshToken.Id;

            await _context.SaveChangesAsync();

            var (accessToken, accessExpires) = GenerateAccessToken(existing.User);

            return new RefreshResponse(
                AccessToken: accessToken,
                RefreshToken: newRawRefresh,
                AccessTokenExpiresAt: accessExpires
            );
        }
        finally
        {
            SessionGate.Release();
        }
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    public async Task<bool> LogoutAsync(string rawRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken) || rawRefreshToken.Length > 1024)
            return false;
        var tokenHash = HashToken(rawRefreshToken);
        var token = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (token == null)
            return false;

        token.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Logout All ────────────────────────────────────────────────────────────

    public async Task<int> LogoutAllAsync(Guid userId)
    {
        var tokens = await _context
            .RefreshTokens.Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();
        foreach (var t in tokens)
            t.RevokedAt = DateTime.UtcNow;
        var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == userId);
        if (user is not null)
            user.SessionVersion++;
        await _context.SaveChangesAsync();
        return tokens.Count;
    }

    public async Task<string?> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 1024)
            return "invalid_current_password";
        if (
            string.IsNullOrEmpty(newPassword)
            || newPassword.Length > 128
            || !AdminRecoveryCommand.PasswordMeetsRequirements(newPassword)
        )
            return "password_too_weak";

        await SessionGate.WaitAsync(cancellationToken);
        try
        {
            var user = await _context.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
            var passwordMatches = VerifyPassword(currentPassword, user?.PasswordHash ?? DummyPasswordHash);
            if (user == null || !passwordMatches)
                return "invalid_current_password";

            user.PasswordHash = HashPassword(newPassword);
            user.RequiresPasswordChange = false;
            user.SessionVersion++;

            var sessions = await _context
                .RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var session in sessions)
                session.RevokedAt = now;

            _context.AuditEvents.Add(
                new AuditEvent
                {
                    ActorUserId = userId,
                    TargetUserId = userId,
                    Action = "user.password_changed",
                }
            );
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }
        finally
        {
            SessionGate.Release();
        }
    }

    // ── Create User ───────────────────────────────────────────────────────────

    public async Task<User?> CreateUserAsync(string email, string userName, string password, bool isAdmin)
    {
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            return null;

        var user = new User
        {
            Email = email,
            UserName = userName,
            PasswordHash = HashPassword(password),
            IsAdmin = isAdmin,
            IsActive = true,
            RequiresPasswordChange = false,
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    private (string accessToken, DateTime expiresAt) GenerateAccessToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.UserName),
            new(HouseholdClaimTypes.SessionVersion, user.SessionVersion.ToString()),
            new(HouseholdClaimTypes.RequiresPasswordChange, user.RequiresPasswordChange.ToString().ToLowerInvariant()),
        };
        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            ),
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return (tokenHandler.WriteToken(token), expires);
    }

    private async Task<(string rawToken, RefreshToken entity)> CreateRefreshTokenAsync(
        Guid userId,
        string? userAgent,
        string? deviceName
    )
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = HashToken(rawToken);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenDays),
            UserAgent = NormalizeMetadata(userAgent, 512),
            DeviceName = NormalizeMetadata(deviceName, 200),
        };

        _context.RefreshTokens.Add(entity);
        await _context.SaveChangesAsync();

        return (rawToken, entity);
    }

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizeMetadata(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }
}
