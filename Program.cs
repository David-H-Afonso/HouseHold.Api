using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.Endpoints;
using Household.Api.Helpers;
using Household.Api.Middleware;
using Household.Api.Models.Auth;
using Household.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Env vars: .NET Core already calls AddEnvironmentVariables() in CreateBuilder.
// Native nested format: JwtSettings__SecretKey, CorsSettings__AllowedOrigins__0, etc.
// Our custom snake_case aliases (JWT_SECRET_KEY, CORS_ALLOWED_ORIGINS, …) are mapped below.

// ── .env file loading (local development) ────────────────────────────────────
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }
}

// ── Environment variable overrides (12-factor) ────────────────────────────────
ApplyEnvOverride(builder.Configuration, "DatabaseSettings:DatabasePath", "DATABASE_PATH");
ApplyEnvOverride(builder.Configuration, "JwtSettings:SecretKey", "JWT_SECRET_KEY");
ApplyEnvOverride(builder.Configuration, "JwtSettings:Issuer", "JWT_ISSUER");
ApplyEnvOverride(builder.Configuration, "JwtSettings:Audience", "JWT_AUDIENCE");
ApplyEnvOverrideInt(builder.Configuration, "JwtSettings:AccessTokenMinutes", "JWT_ACCESS_TOKEN_MINUTES");
ApplyEnvOverrideInt(builder.Configuration, "JwtSettings:RefreshTokenDays", "JWT_REFRESH_TOKEN_DAYS");

ApplyEnvOverride(builder.Configuration, "SeedSettings:AdminEmail", "SEED_ADMIN_EMAIL");
ApplyEnvOverride(builder.Configuration, "SeedSettings:AdminUsername", "SEED_ADMIN_USERNAME");
ApplyEnvOverride(builder.Configuration, "SeedSettings:AdminPassword", "SEED_ADMIN_PASSWORD");
ApplyEnvOverrideBool(builder.Configuration, "SeedSettings:AdminEnabled", "SEED_ADMIN_ENABLED");
ApplyEnvOverrideBool(builder.Configuration, "SeedSettings:DemoDataEnabled", "DEMO_DATA_ENABLED");

// CORS: support comma-separated FRONTEND_URL or CORS_ALLOWED_ORIGINS
var corsOriginEnv = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")
                 ?? Environment.GetEnvironmentVariable("FRONTEND_URL");
if (!string.IsNullOrWhiteSpace(corsOriginEnv))
{
    var origins = corsOriginEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (int i = 0; i < origins.Length; i++)
        builder.Configuration[$"CorsSettings:AllowedOrigins:{i}"] = origins[i];
}

// MealType range overrides
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:BreakfastStart", "MEAL_BREAKFAST_START");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:BreakfastEnd", "MEAL_BREAKFAST_END");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:MorningSnackStart", "MEAL_MORNING_SNACK_START");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:MorningSnackEnd", "MEAL_MORNING_SNACK_END");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:LunchStart", "MEAL_LUNCH_START");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:LunchEnd", "MEAL_LUNCH_END");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:AfternoonSnackStart", "MEAL_AFTERNOON_SNACK_START");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:AfternoonSnackEnd", "MEAL_AFTERNOON_SNACK_END");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:DinnerStart", "MEAL_DINNER_START");
ApplyEnvOverride(builder.Configuration, "MealTypeSettings:DinnerEnd", "MEAL_DINNER_END");

// ── Configuration binding ─────────────────────────────────────────────────────
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<SeedSettings>(builder.Configuration.GetSection(SeedSettings.SectionName));
builder.Services.Configure<MealTypeSettings>(builder.Configuration.GetSection(MealTypeSettings.SectionName));

// ── Database ──────────────────────────────────────────────────────────────────
var dbSettings = builder.Configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new();

var dbPath = dbSettings.DatabasePath;
if (!Path.IsPathRooted(dbPath))
    dbPath = Path.GetFullPath(dbPath);

// Ensure directory exists (important for Docker volumes)
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir))
    Directory.CreateDirectory(dbDir);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
    if (dbSettings.EnableSensitiveDataLogging)
        options.EnableSensitiveDataLogging();
});

// ── Authentication & Authorization ────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new();

if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
    throw new InvalidOperationException(
        "JWT SecretKey is not configured. Set JWT_SECRET_KEY environment variable.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                if (ctx.Exception.Message.Contains("expired"))
                    logger.LogWarning("JWT token expired");
                else
                    logger.LogError(ctx.Exception, "JWT authentication failed");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new();

if (corsSettings.AllowedOrigins.Count == 0 && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "CORS: no AllowedOrigins configured for production. " +
        "Set CORS_ALLOWED_ORIGINS env var or CorsSettings:AllowedOrigins[]. " +
        "Refusing to start with AllowAnyOrigin in production.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        if (corsSettings.AllowedOrigins.Count > 0)
        {
            // JWT Bearer: AllowCredentials() is NOT needed.
            // If you ever switch to cookies, add AllowCredentials() + correct SameSite.
            policy.WithOrigins(corsSettings.AllowedOrigins.ToArray())
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            // Development only: allow any origin when no specific origins are configured.
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });

    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Household API",
        Version = "v1",
        Description = "Backend para gestión doméstica: comida, tareas del hogar e incidencias."
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Bearer. Formato: Bearer {token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFoodItemService, FoodItemService>();
builder.Services.AddScoped<IDishService, DishService>();
builder.Services.AddScoped<IMealService, MealService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IIssueService, IssueService>();
builder.Services.AddScoped<IMealTypeHelper, MealTypeHelper>();

builder.Services.AddHttpContextAccessor();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Migrate & seed ────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var seedCfg = scope.ServiceProvider.GetRequiredService<IConfiguration>()
        .GetSection(SeedSettings.SectionName).Get<SeedSettings>() ?? new();

    await SeedAsync(db, seedCfg, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

// ── Middleware pipeline ────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Household API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowAll");
else
    app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

// ── Health ────────────────────────────────────────────────────────────────────
app.MapGet("/health", async (AppDbContext db, ILogger<Program> logger) =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    string dbStatus;
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        dbStatus = pending.Count == 0 ? "ok" : $"ok ({pending.Count} pending migration(s))";
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check: DB connectivity failed");
        dbStatus = "error";
    }
    var overall = dbStatus.StartsWith("error") ? "degraded" : "healthy";
    var payload = new { status = overall, version, db = dbStatus, timestamp = DateTime.UtcNow };
    return overall == "healthy"
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: 503);
}).AllowAnonymous().WithTags("System");

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapFoodItemEndpoints();
app.MapDishEndpoints();
app.MapMealEndpoints();
app.MapRoomEndpoints();
app.MapTaskEndpoints();
app.MapIssueEndpoints();

app.Run();

// ── Seed helper ───────────────────────────────────────────────────────────────
static async Task SeedAsync(AppDbContext db, SeedSettings settings, ILogger logger)
{
    if (!settings.AdminEnabled) return;

    var adminExists = await db.Users.AnyAsync(u => u.IsAdmin);
    if (adminExists) return;

    var passwordHash = BCrypt.Net.BCrypt.HashPassword(settings.AdminPassword, workFactor: 12);

    var admin = new User
    {
        Email = settings.AdminEmail,
        UserName = settings.AdminUsername,
        PasswordHash = passwordHash,
        IsAdmin = true,
        IsActive = true
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();
    logger.LogInformation("Seed: admin user created with email {Email}", settings.AdminEmail);
}

// ── Env override helpers ──────────────────────────────────────────────────────
static void ApplyEnvOverride(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value)) config[key] = value;
}

static void ApplyEnvOverrideInt(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (int.TryParse(value, out _)) config[key] = value!;
}

static void ApplyEnvOverrideBool(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value)) config[key] = value;
}
