using Household.Api.Models.Auth;
using Household.Api.Models.Food;
using Household.Api.Models.Home;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        ChangeTracker.LazyLoadingEnabled = false;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<UserPreference> UserPreferences { get; set; }
    public DbSet<UserInvitation> UserInvitations { get; set; }
    public DbSet<AuditEvent> AuditEvents { get; set; }

    // ── Food ──────────────────────────────────────────────────────────────────
    public DbSet<FoodItem> FoodItems { get; set; }
    public DbSet<DishTemplate> DishTemplates { get; set; }
    public DbSet<DishTemplateItem> DishTemplateItems { get; set; }
    public DbSet<MealEntry> MealEntries { get; set; }
    public DbSet<MealEntryItem> MealEntryItems { get; set; }

    // ── Home ──────────────────────────────────────────────────────────────────
    public DbSet<Room> Rooms { get; set; }
    public DbSet<TaskTemplate> TaskTemplates { get; set; }
    public DbSet<TaskInstance> TaskInstances { get; set; }
    public DbSet<HomeIssue> HomeIssues { get; set; }

    // ── Integrations ──────────────────────────────────────────────────────────
    public DbSet<Integration> Integrations { get; set; }
    public DbSet<IntegrationSecret> IntegrationSecrets { get; set; }
    public DbSet<DashboardWidget> DashboardWidgets { get; set; }
    public DbSet<IntegrationActionLog> IntegrationActionLogs { get; set; }
    public DbSet<AppLauncherItem> AppLauncherItems { get; set; }
    public DbSet<AllowedComposeApp> AllowedComposeApps { get; set; }
    public DbSet<HouseholdConsumerConnection> HouseholdConsumerConnections { get; set; }
    public DbSet<HouseholdAuthorizationAttempt> HouseholdAuthorizationAttempts { get; set; }
    public DbSet<UserAppFavorite> UserAppFavorites { get; set; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                switch (entry.Entity)
                {
                    case User u:
                        u.CreatedAt = now;
                        u.UpdatedAt = now;
                        break;
                    case FoodItem fi:
                        fi.CreatedAt = now;
                        fi.UpdatedAt = now;
                        break;
                    case DishTemplate dt:
                        dt.CreatedAt = now;
                        dt.UpdatedAt = now;
                        break;
                    case MealEntry me:
                        me.CreatedAt = now;
                        me.UpdatedAt = now;
                        break;
                    case TaskTemplate tt:
                        tt.CreatedAt = now;
                        tt.UpdatedAt = now;
                        break;
                    case Room r:
                        r.CreatedAt = now;
                        break;
                    case HomeIssue hi:
                        hi.CreatedAt = now;
                        break;
                    case RefreshToken rt:
                        rt.CreatedAt = now;
                        break;
                    case Integration i:
                        i.CreatedAt = now;
                        i.UpdatedAt = now;
                        break;
                    case IntegrationSecret s:
                        s.CreatedAt = now;
                        s.UpdatedAt = now;
                        break;
                    case DashboardWidget dw:
                        dw.CreatedAt = now;
                        dw.UpdatedAt = now;
                        break;
                    case IntegrationActionLog log:
                        log.StartedAt = now;
                        break;
                    case AppLauncherItem app:
                        app.CreatedAt = now;
                        app.UpdatedAt = now;
                        break;
                    case AllowedComposeApp composeApp:
                        composeApp.CreatedAt = now;
                        composeApp.UpdatedAt = now;
                        break;
                    case HouseholdConsumerConnection connection:
                        connection.CreatedAt = now;
                        connection.UpdatedAt = now;
                        break;
                    case HouseholdAuthorizationAttempt attempt:
                        attempt.CreatedAt = now;
                        break;
                    case UserPreference preference:
                        preference.CreatedAt = now;
                        preference.UpdatedAt = now;
                        break;
                    case UserInvitation invitation:
                        invitation.CreatedAt = now;
                        break;
                    case AuditEvent auditEvent:
                        auditEvent.CreatedAt = now;
                        break;
                    case UserAppFavorite favorite:
                        favorite.CreatedAt = now;
                        break;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                switch (entry.Entity)
                {
                    case User u:
                        u.UpdatedAt = now;
                        entry.Property(nameof(User.CreatedAt)).IsModified = false;
                        break;
                    case FoodItem fi:
                        fi.UpdatedAt = now;
                        entry.Property(nameof(FoodItem.CreatedAt)).IsModified = false;
                        break;
                    case DishTemplate dt:
                        dt.UpdatedAt = now;
                        entry.Property(nameof(DishTemplate.CreatedAt)).IsModified = false;
                        break;
                    case MealEntry me:
                        me.UpdatedAt = now;
                        entry.Property(nameof(MealEntry.CreatedAt)).IsModified = false;
                        break;
                    case TaskTemplate tt:
                        tt.UpdatedAt = now;
                        entry.Property(nameof(TaskTemplate.CreatedAt)).IsModified = false;
                        break;
                    case Integration i:
                        i.UpdatedAt = now;
                        entry.Property(nameof(Integration.CreatedAt)).IsModified = false;
                        break;
                    case IntegrationSecret s:
                        s.UpdatedAt = now;
                        entry.Property(nameof(IntegrationSecret.CreatedAt)).IsModified = false;
                        break;
                    case DashboardWidget dw:
                        dw.UpdatedAt = now;
                        entry.Property(nameof(DashboardWidget.CreatedAt)).IsModified = false;
                        break;
                    case AppLauncherItem app:
                        app.UpdatedAt = now;
                        entry.Property(nameof(AppLauncherItem.CreatedAt)).IsModified = false;
                        break;
                    case AllowedComposeApp composeApp:
                        composeApp.UpdatedAt = now;
                        entry.Property(nameof(AllowedComposeApp.CreatedAt)).IsModified = false;
                        break;
                    case HouseholdConsumerConnection connection:
                        connection.UpdatedAt = now;
                        entry.Property(nameof(HouseholdConsumerConnection.CreatedAt)).IsModified = false;
                        break;
                    case UserPreference preference:
                        preference.UpdatedAt = now;
                        entry.Property(nameof(UserPreference.CreatedAt)).IsModified = false;
                        break;
                }
            }
        }
    }

    // ── Model Configuration ───────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).IsRequired().HasMaxLength(320);
            e.Property(u => u.UserName).IsRequired().HasMaxLength(100);
            e.Property(u => u.PasswordHash).IsRequired();
        });

        // ── RefreshToken ──────────────────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(rt => rt.Id);
            e.HasIndex(rt => rt.TokenHash).IsUnique();
            e.HasIndex(rt => rt.UserId);
            e.Property(rt => rt.TokenHash).IsRequired();

            e.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── FoodItem ──────────────────────────────────────────────────────────
        modelBuilder.Entity<FoodItem>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.NameNormalized);
            e.Property(f => f.Name).IsRequired().HasMaxLength(200);
            e.Property(f => f.NameNormalized).IsRequired().HasMaxLength(200);
            e.Property(f => f.KcalPer100g).HasColumnType("decimal(8,2)");
            e.Property(f => f.ProteinPer100g).HasColumnType("decimal(8,2)");
            e.Property(f => f.CarbsPer100g).HasColumnType("decimal(8,2)");
            e.Property(f => f.FatPer100g).HasColumnType("decimal(8,2)");

            e.HasOne(f => f.CreatedByUser)
                .WithMany()
                .HasForeignKey(f => f.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── DishTemplate ──────────────────────────────────────────────────────
        modelBuilder.Entity<DishTemplate>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).IsRequired().HasMaxLength(200);

            e.HasOne(d => d.OwnerUser)
                .WithMany()
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        // ── DishTemplateItem ──────────────────────────────────────────────────
        modelBuilder.Entity<DishTemplateItem>(e =>
        {
            e.HasKey(dti => dti.Id);
            e.Property(dti => dti.Grams).HasColumnType("decimal(8,2)");

            e.HasOne(dti => dti.DishTemplate)
                .WithMany(d => d.Items)
                .HasForeignKey(dti => dti.DishTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(dti => dti.FoodItem)
                .WithMany(f => f.DishTemplateItems)
                .HasForeignKey(dti => dti.FoodItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MealEntry ─────────────────────────────────────────────────────────
        modelBuilder.Entity<MealEntry>(e =>
        {
            e.HasKey(me => me.Id);
            e.HasIndex(me => new { me.UserId, me.EatenAt });
            e.Property(me => me.Title).HasMaxLength(200);
            e.Property(me => me.Notes).HasMaxLength(2000);

            e.HasOne(me => me.User).WithMany().HasForeignKey(me => me.UserId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(me => me.DishTemplate)
                .WithMany(d => d.MealEntries)
                .HasForeignKey(me => me.DishTemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        // ── MealEntryItem ─────────────────────────────────────────────────────
        modelBuilder.Entity<MealEntryItem>(e =>
        {
            e.HasKey(mei => mei.Id);
            e.Property(mei => mei.Grams).HasColumnType("decimal(8,2)");

            e.HasOne(mei => mei.MealEntry)
                .WithMany(me => me.Items)
                .HasForeignKey(mei => mei.MealEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(mei => mei.FoodItem)
                .WithMany(f => f.MealEntryItems)
                .HasForeignKey(mei => mei.FoodItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Room ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Room>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Name).IsUnique();
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
        });

        // ── TaskTemplate ──────────────────────────────────────────────────────
        modelBuilder.Entity<TaskTemplate>(e =>
        {
            e.HasKey(tt => tt.Id);
            e.Property(tt => tt.Title).IsRequired().HasMaxLength(200);
            e.Property(tt => tt.Description).HasMaxLength(2000);
            e.HasIndex(tt => tt.OwnerUserId);

            e.HasOne(tt => tt.OwnerUser)
                .WithMany()
                .HasForeignKey(tt => tt.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            e.HasOne(tt => tt.Room)
                .WithMany(r => r.TaskTemplates)
                .HasForeignKey(tt => tt.RoomId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            e.HasOne(tt => tt.AssignedToUser)
                .WithMany()
                .HasForeignKey(tt => tt.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        // ── TaskInstance ──────────────────────────────────────────────────────
        modelBuilder.Entity<TaskInstance>(e =>
        {
            e.HasKey(ti => ti.Id);
            // Idempotency: only one instance per template per day
            e.HasIndex(ti => new { ti.TaskTemplateId, ti.DueDate }).IsUnique();
            // Query index: "show me all tasks for today, grouped by slot"
            e.HasIndex(ti => new { ti.DueDate, ti.TimeOfDaySlot });

            e.HasOne(ti => ti.TaskTemplate)
                .WithMany(tt => tt.Instances)
                .HasForeignKey(ti => ti.TaskTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ti => ti.AssignedToUser)
                .WithMany()
                .HasForeignKey(ti => ti.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            e.HasOne(ti => ti.CompletedByUser)
                .WithMany()
                .HasForeignKey(ti => ti.CompletedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        // ── HomeIssue ─────────────────────────────────────────────────────────
        modelBuilder.Entity<HomeIssue>(e =>
        {
            e.HasKey(hi => hi.Id);
            e.Property(hi => hi.Title).IsRequired().HasMaxLength(200);
            e.Property(hi => hi.Description).HasMaxLength(4000);

            e.HasOne(hi => hi.Room)
                .WithMany(r => r.HomeIssues)
                .HasForeignKey(hi => hi.RoomId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            e.HasOne(hi => hi.CreatedByUser)
                .WithMany()
                .HasForeignKey(hi => hi.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserPreference>(e =>
        {
            e.HasKey(preference => preference.UserId);
            e.Property(preference => preference.TimeZoneId).HasMaxLength(128);
            e.Property(preference => preference.VisualPreference).IsRequired().HasMaxLength(32);
            e.Property(preference => preference.PokemonSpriteSource).IsRequired().HasMaxLength(32);
            e.Property(preference => preference.GamesStatusOrderJson).IsRequired().HasMaxLength(4000);
            e.Property(preference => preference.HiddenGitHubReposJson).IsRequired().HasMaxLength(4000);
            e.Property(preference => preference.JellyfinUserId).HasMaxLength(128);
            e.HasIndex(preference => preference.SeerrUserIdOverride)
                .IsUnique()
                .HasFilter("\"SeerrUserIdOverride\" IS NOT NULL");
            e.HasIndex(preference => preference.SeerrResolvedUserId)
                .IsUnique()
                .HasFilter("\"SeerrResolvedUserId\" IS NOT NULL");
            e.HasIndex(preference => preference.JellyfinUserId)
                .IsUnique()
                .HasFilter("\"SeerrJellyfinMappingApproved\" = 1 AND \"JellyfinUserId\" IS NOT NULL");
            e.HasOne(preference => preference.User)
                .WithOne(user => user.Preference)
                .HasForeignKey<UserPreference>(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserInvitation>(e =>
        {
            e.HasKey(invitation => invitation.Id);
            e.HasIndex(invitation => invitation.TokenHash).IsUnique();
            e.HasIndex(invitation => new { invitation.Email, invitation.ExpiresAt });
            e.Property(invitation => invitation.Email).IsRequired().HasMaxLength(320);
            e.Property(invitation => invitation.UserName).IsRequired().HasMaxLength(100);
            e.Property(invitation => invitation.TokenHash).IsRequired().HasMaxLength(64);
            e.HasOne(invitation => invitation.CreatedByUser)
                .WithMany()
                .HasForeignKey(invitation => invitation.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(invitation => invitation.RedeemedUser)
                .WithMany()
                .HasForeignKey(invitation => invitation.RedeemedUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.HasKey(auditEvent => auditEvent.Id);
            e.HasIndex(auditEvent => auditEvent.CreatedAt);
            e.Property(auditEvent => auditEvent.Action).IsRequired().HasMaxLength(120);
            e.Property(auditEvent => auditEvent.SummaryJson).HasMaxLength(4000);
            e.HasOne(auditEvent => auditEvent.ActorUser)
                .WithMany()
                .HasForeignKey(auditEvent => auditEvent.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Integrations ──────────────────────────────────────────────────────
        modelBuilder.Entity<Integration>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => new { i.Type, i.Name }).IsUnique();
            e.Property(i => i.Type).HasConversion<string>().HasMaxLength(50);
            e.Property(i => i.LastHealthStatus).HasConversion<string>().HasMaxLength(50);
            e.Property(i => i.Name).IsRequired().HasMaxLength(120);
            e.Property(i => i.BaseUrl).HasMaxLength(500);
            e.Property(i => i.OpenUrl).HasMaxLength(500);
            e.Property(i => i.ConfigurationVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<IntegrationSecret>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.IntegrationId, s.SecretKey }).IsUnique();
            e.Property(s => s.SecretKey).IsRequired().HasMaxLength(120);
            e.Property(s => s.ProtectedValue).IsRequired();

            e.HasOne(s => s.Integration)
                .WithMany(i => i.Secrets)
                .HasForeignKey(s => s.IntegrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DashboardWidget>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => new { w.UserId, w.Position });
            e.HasIndex(w => new { w.UserId, w.WidgetType }).IsUnique();
            e.Property(w => w.WidgetType).IsRequired().HasMaxLength(120);
            e.Property(w => w.Size).IsRequired().HasMaxLength(20).HasDefaultValue("medium");
            e.Property(w => w.SchemaVersion).HasDefaultValue(1);

            e.HasOne(w => w.User)
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(w => w.Integration)
                .WithMany(i => i.DashboardWidgets)
                .HasForeignKey(w => w.IntegrationId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        modelBuilder.Entity<UserAppFavorite>(e =>
        {
            e.HasKey(favorite => favorite.Id);
            e.HasIndex(favorite => new { favorite.UserId, favorite.AppId }).IsUnique();
            e.Property(favorite => favorite.AppId).IsRequired().HasMaxLength(120);
            e.Property(favorite => favorite.Favorite).HasDefaultValue(true);
            e.HasOne(favorite => favorite.User)
                .WithMany(user => user.AppFavorites)
                .HasForeignKey(favorite => favorite.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationActionLog>(e =>
        {
            e.HasKey(log => log.Id);
            e.HasIndex(log => new { log.IntegrationId, log.StartedAt });
            e.HasIndex(log => new { log.AppId, log.StartedAt });
            e.Property(log => log.Action).IsRequired().HasMaxLength(120);
            e.Property(log => log.Status).HasConversion<string>().HasMaxLength(50);
            e.Property(log => log.Source).IsRequired().HasMaxLength(120);
            e.Property(log => log.AppId).HasMaxLength(120);
            e.Property(log => log.ErrorMessage).HasMaxLength(4000);

            e.HasOne(log => log.Integration)
                .WithMany(i => i.ActionLogs)
                .HasForeignKey(log => log.IntegrationId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        });

        modelBuilder.Entity<AppLauncherItem>(e =>
        {
            e.HasKey(app => app.Id);
            e.HasIndex(app => app.AppId).IsUnique();
            e.HasIndex(app => app.Category);
            e.Property(app => app.AppId).IsRequired().HasMaxLength(120);
            e.Property(app => app.Name).IsRequired().HasMaxLength(160);
            e.Property(app => app.Category).IsRequired().HasMaxLength(120);
            e.Property(app => app.Description).HasMaxLength(1000);
            e.Property(app => app.IconUrl).HasMaxLength(500);
            e.Property(app => app.InternalUrl).HasMaxLength(500);
            e.Property(app => app.ExternalUrl).HasMaxLength(500);
            e.Property(app => app.OpenUrl).HasMaxLength(500);
            e.Property(app => app.Enabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<AllowedComposeApp>(e =>
        {
            e.HasKey(app => app.Id);
            e.HasIndex(app => app.AppId).IsUnique();
            e.Property(app => app.AppId).IsRequired().HasMaxLength(120);
            e.Property(app => app.DisplayName).IsRequired().HasMaxLength(160);
            e.Property(app => app.ComposePath).IsRequired().HasMaxLength(1000);
            e.Property(app => app.ProjectName).HasMaxLength(160);
            e.Property(app => app.HealthCheckUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<HouseholdConsumerConnection>(e =>
        {
            e.HasKey(connection => connection.Id);
            e.HasIndex(connection => new { connection.UserId, connection.Provider }).IsUnique();
            e.Property(connection => connection.Provider).IsRequired().HasMaxLength(50);
            e.Property(connection => connection.ProtectedAccessToken).IsRequired().HasMaxLength(24000);
            e.Property(connection => connection.ProtectedRefreshToken).IsRequired().HasMaxLength(24000);
            e.Property(connection => connection.SourceConnectionId).IsRequired().HasMaxLength(200);
            e.Property(connection => connection.AccountId).IsRequired().HasMaxLength(500);
            e.Property(connection => connection.AccountDisplayName).IsRequired().HasMaxLength(200);
            e.Property(connection => connection.GrantedScopes).IsRequired().HasMaxLength(1000);
            e.Property(connection => connection.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(connection => connection.LastError).HasMaxLength(100);

            e.HasOne(connection => connection.User)
                .WithMany(user => user.HouseholdConsumerConnections)
                .HasForeignKey(connection => connection.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HouseholdAuthorizationAttempt>(e =>
        {
            e.HasKey(attempt => attempt.Id);
            e.HasIndex(attempt => attempt.StateHash).IsUnique();
            e.HasIndex(attempt => new { attempt.UserId, attempt.Provider, attempt.ExpiresAt });
            e.Property(attempt => attempt.Provider).IsRequired().HasMaxLength(50);
            e.Property(attempt => attempt.StateHash).IsRequired().HasMaxLength(64);
            e.Property(attempt => attempt.ProtectedCodeVerifier).IsRequired().HasMaxLength(1000);
            e.Property(attempt => attempt.RedirectUri).IsRequired().HasMaxLength(1000);
            e.Property(attempt => attempt.RequestedScopes).IsRequired().HasMaxLength(1000);

            e.HasOne(attempt => attempt.User)
                .WithMany(user => user.HouseholdAuthorizationAttempts)
                .HasForeignKey(attempt => attempt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
