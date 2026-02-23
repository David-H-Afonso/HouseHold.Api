using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Household.Api.Services;

public class AuthService : IAuthService
{
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
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() && u.IsActive);

        if (user == null || !VerifyPassword(password, user.PasswordHash))
            return null;

        var (accessToken, accessExpires) = GenerateAccessToken(user);
        var (rawRefresh, refreshToken) = await CreateRefreshTokenAsync(user.Id, userAgent, deviceName);

        return new LoginResponse(
            UserId: user.Id,
            Email: user.Email,
            UserName: user.UserName,
            IsAdmin: user.IsAdmin,
            AccessToken: accessToken,
            RefreshToken: rawRefresh,
            AccessTokenExpiresAt: accessExpires
        );
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task<RefreshResponse?> RefreshAsync(string rawRefreshToken, string? userAgent, string? deviceName)
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

        if (!existing.User.IsActive)
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

    // ── Logout ────────────────────────────────────────────────────────────────

    public async Task<bool> LogoutAsync(string rawRefreshToken)
    {
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
        await _context.SaveChangesAsync();
        return tokens.Count;
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
            UserAgent = userAgent,
            DeviceName = deviceName,
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
}
