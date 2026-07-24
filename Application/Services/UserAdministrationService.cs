using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Household.Api.Operations;
using Household.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public sealed class UserAdministrationService(AppDbContext db) : IUserAdministrationService
{
    private static readonly SemaphoreSlim AdministrationGate = new(1, 1);
    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(user => user.UserName).Select(user => ToDto(user)).ToListAsync(cancellationToken);

    public async Task<(AdminUserDto? User, string? TemporaryPassword, string? Error)> CreateUserAsync(
        Guid actorUserId,
        AdminCreateUserRequest request,
        CancellationToken cancellationToken
    )
    {
        var validation = ValidateIdentity(request.Email, request.UserName);
        if (validation is not null) return (null, null, validation);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(user => user.Email.ToLower() == email, cancellationToken))
            return (null, null, "email_exists");
        var generatedPassword = string.IsNullOrWhiteSpace(request.TemporaryPassword);
        var password = generatedPassword ? GenerateTemporaryPassword() : request.TemporaryPassword!;
        if (!AdminRecoveryCommand.PasswordMeetsRequirements(password))
            return (null, null, "password_too_weak");

        var user = new User
        {
            Email = email,
            UserName = request.UserName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            IsAdmin = request.IsAdmin,
            IsActive = true,
            RequiresPasswordChange = true,
        };
        db.Users.Add(user);
        AddAudit(actorUserId, user.Id, "user.created");
        await db.SaveChangesAsync(cancellationToken);
        return (ToDto(user), generatedPassword ? password : null, null);
    }

    public async Task<(AdminUserDto? User, string? Error)> UpdateUserAsync(
        Guid actorUserId,
        Guid userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken
    )
    {
        await AdministrationGate.WaitAsync(cancellationToken);
        try
        {
            var validation = ValidateIdentity(request.Email, request.UserName);
            if (validation is not null) return (null, validation);
            var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
            if (user is null) return (null, "not_found");
            var email = request.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(item => item.Id != userId && item.Email.ToLower() == email, cancellationToken))
                return (null, "email_exists");

            var removesActiveAdmin = user.IsAdmin && user.IsActive && (!request.IsAdmin || !request.IsActive);
            if (removesActiveAdmin && await db.Users.CountAsync(item => item.IsAdmin && item.IsActive, cancellationToken) <= 1)
                return (null, "last_admin");

            var identityChanged = user.Email != email || user.UserName != request.UserName.Trim();
            var roleChanged = user.IsAdmin != request.IsAdmin;
            var activationChanged = user.IsActive != request.IsActive;
            var criticalChange = identityChanged || roleChanged || activationChanged;
            user.Email = email;
            user.UserName = request.UserName.Trim();
            user.IsAdmin = request.IsAdmin;
            user.IsActive = request.IsActive;
            if (criticalChange)
            {
                user.SessionVersion++;
                await RevokeSessionsAsync(user.Id, cancellationToken);
            }
            if (identityChanged) AddAudit(actorUserId, user.Id, "user.identity_changed");
            if (roleChanged) AddAudit(actorUserId, user.Id, request.IsAdmin ? "user.role_admin_granted" : "user.role_admin_revoked");
            if (activationChanged) AddAudit(actorUserId, user.Id, request.IsActive ? "user.activated" : "user.deactivated");
            await db.SaveChangesAsync(cancellationToken);
            return (ToDto(user), null);
        }
        finally
        {
            AdministrationGate.Release();
        }
    }

    public async Task<(string? TemporaryPassword, string? Error)> ResetPasswordAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return (null, "not_found");
        var password = GenerateTemporaryPassword();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        user.RequiresPasswordChange = true;
        user.SessionVersion++;
        await RevokeSessionsAsync(user.Id, cancellationToken);
        AddAudit(actorUserId, user.Id, "user.password_reset");
        await db.SaveChangesAsync(cancellationToken);
        return (password, null);
    }

    public async Task<(InvitationCreatedDto? Invitation, string? Error)> CreateInvitationAsync(
        Guid actorUserId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken
    )
    {
        var validation = ValidateIdentity(request.Email, request.UserName);
        if (validation is not null) return (null, validation);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(user => user.Email.ToLower() == email, cancellationToken))
            return (null, "email_exists");

        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = new UserInvitation
        {
            Email = email,
            UserName = request.UserName.Trim(),
            IsAdmin = request.IsAdmin,
            TokenHash = AuthService.HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.AddHours(Math.Clamp(request.ExpiresInHours, 1, 168)),
            CreatedByUserId = actorUserId,
        };
        db.UserInvitations.Add(invitation);
        AddAudit(actorUserId, null, "invitation.created");
        await db.SaveChangesAsync(cancellationToken);
        return (new InvitationCreatedDto(invitation.Id, rawToken, invitation.ExpiresAt), null);
    }

    public async Task<(AdminUserDto? User, string? Error)> RedeemInvitationAsync(
        RedeemInvitationRequest request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 256)
            return (null, "invalid_invitation");
        if (!AdminRecoveryCommand.PasswordMeetsRequirements(request.Password))
            return (null, "password_too_weak");

        var hash = AuthService.HashToken(request.Token);
        var now = DateTime.UtcNow;
        var invitation = await db.UserInvitations.SingleOrDefaultAsync(
            item => item.TokenHash == hash && item.RedeemedAt == null && item.ExpiresAt > now,
            cancellationToken
        );
        if (invitation is null) return (null, "invalid_invitation");
        if (await db.Users.AnyAsync(user => user.Email.ToLower() == invitation.Email, cancellationToken))
            return (null, "invalid_invitation");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db.UserInvitations
            .Where(item => item.Id == invitation.Id && item.RedeemedAt == null && item.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RedeemedAt, now), cancellationToken);
        if (claimed != 1) return (null, "invalid_invitation");

        db.Entry(invitation).State = EntityState.Detached;

        var user = new User
        {
            Email = invitation.Email,
            UserName = invitation.UserName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            IsAdmin = invitation.IsAdmin,
            IsActive = true,
            RequiresPasswordChange = false,
        };
        db.Users.Add(user);
        AddAudit(null, user.Id, "invitation.redeemed");
        await db.SaveChangesAsync(cancellationToken);
        await db.UserInvitations.Where(item => item.Id == invitation.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RedeemedUserId, user.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (ToDto(user), null);
    }

    public async Task<IReadOnlyList<object>> GetAuditEventsAsync(CancellationToken cancellationToken) =>
        await db.AuditEvents.AsNoTracking().OrderByDescending(item => item.CreatedAt).Take(200)
            .Select(item => (object)new { item.Id, item.ActorUserId, item.TargetUserId, item.Action, item.CreatedAt })
            .ToListAsync(cancellationToken);

    private void AddAudit(Guid? actorUserId, Guid? targetUserId, string action) => db.AuditEvents.Add(new AuditEvent
    {
        ActorUserId = actorUserId,
        TargetUserId = targetUserId,
        Action = action,
    });

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sessions = await db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var session in sessions) session.RevokedAt = now;
    }

    private static string? ValidateIdentity(string email, string userName)
    {
        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (normalizedEmail.Length > 320 || !new EmailAddressAttribute().IsValid(normalizedEmail)) return "invalid_email";
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > 100
            || userName.Any(char.IsControl)) return "invalid_name";
        return null;
    }

    private static string GenerateTemporaryPassword() => $"Hh!{Base64Url(RandomNumberGenerator.GetBytes(18))}9a";
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static AdminUserDto ToDto(User user) => new(
        user.Id, user.Email, user.UserName, user.IsAdmin, user.IsActive, user.CreatedAt, user.UpdatedAt
    );
}
