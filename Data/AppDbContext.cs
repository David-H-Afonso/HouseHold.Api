using Household.Api.Models.Auth;
using Household.Api.Models.Food;
using Household.Api.Models.Home;
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
    }
}
