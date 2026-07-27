using System.Text;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Household.Api.Application.Interfaces;
using Household.Api.Application.Services;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.Endpoints;
using Household.Api.Helpers;
using Household.Api.Infrastructure.AppLauncher;
using Household.Api.Infrastructure.Integrations.BeastVault;
using Household.Api.Infrastructure.Integrations.DoIt;
using Household.Api.Infrastructure.Integrations.Docker;
using Household.Api.Infrastructure.Integrations.GamesDatabase;
using Household.Api.Infrastructure.Integrations.Jellywatch;
using Household.Api.Infrastructure.Integrations.WarcraftArchive;
using Household.Api.Infrastructure.Integrations.Jellyfin;
using Household.Api.Infrastructure.Integrations.GitHub;
using Household.Api.Infrastructure.Integrations.CasaOs;
using Household.Api.Middleware;
using Household.Api.Models.Auth;
using Household.Api.Operations;
using Household.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

// ── Env vars: .NET Core already calls AddEnvironmentVariables() in CreateBuilder.
// Native nested format: JwtSettings__SecretKey, CorsSettings__AllowedOrigins__0, etc.
// Our custom snake_case aliases (JWT_SECRET_KEY, CORS_ALLOWED_ORIGINS, …) are mapped below.

// ── .env file loading (local development) ────────────────────────────────────
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2 && Environment.GetEnvironmentVariable(parts[0].Trim()) is null)
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
ApplyEnvOverride(builder.Configuration, "AppLauncherSettings:ConfigPath", "APP_LAUNCHER_CONFIG_PATH");
ApplyEnvOverride(builder.Configuration, "DockerSettings:Mode", "DOCKER_MODE");
ApplyEnvOverride(builder.Configuration, "DockerSettings:DockerHost", "DOCKER_HOST");
ApplyEnvOverride(builder.Configuration, "DockerSettings:ComposeBin", "DOCKER_COMPOSE_BIN");
ApplyEnvOverrideInt(builder.Configuration, "DockerSettings:CommandTimeoutSeconds", "DOCKER_COMMAND_TIMEOUT_SECONDS");
ApplyEnvOverrideInt(builder.Configuration, "DockerSettings:LogTailLines", "DOCKER_LOG_TAIL_LINES");
ApplyEnvOverride(builder.Configuration, "GamesDatabaseSettings:BaseUrl", "GAMESDATABASE_BASE_URL");
ApplyEnvOverride(builder.Configuration, "GamesDatabaseSettings:OpenUrl", "GAMESDATABASE_OPEN_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:PublicUrl", "HOUSEHOLD_PUBLIC_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:ApiPublicUrl", "HOUSEHOLD_API_PUBLIC_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:ClientId", "HOUSEHOLD_CLIENT_ID");
ApplyEnvOverride(
    builder.Configuration,
    "HouseholdConnectionSettings:DataProtectionKeysPath",
    "DATA_PROTECTION_KEYS_PATH"
);
ApplyEnvOverrideInt(builder.Configuration, "ExternalIntegrationSettings:GitHubPollSeconds", "GITHUB_ACTIONS_POLL_SECONDS");
ApplyEnvOverrideInt(builder.Configuration, "ExternalIntegrationSettings:GitHubConcurrency", "GITHUB_ACTIONS_CONCURRENCY");
ApplyEnvOverride(builder.Configuration, "ExternalIntegrationSettings:WarcraftStatusPathTemplate", "WARCRAFT_STATUS_PATH_TEMPLATE");
ApplyEnvOverride(builder.Configuration, "ExternalIntegrationSettings:PokemonDownloadPathTemplate", "POKEMON_DOWNLOAD_PATH_TEMPLATE");
ApplyEnvOverride(builder.Configuration, "CasaOsUpdateSettings:BackupRoot", "CASAOS_COMPOSE_BACKUP_ROOT");
ApplyEnvOverrideInt(builder.Configuration, "CasaOsUpdateSettings:RequestTimeoutSeconds", "CASAOS_UPDATE_TIMEOUT_SECONDS");
ApplyEnvOverrideInt(builder.Configuration, "CasaOsUpdateSettings:MaxYamlBytes", "CASAOS_MAX_YAML_BYTES");
ApplyEnvOverrideInt(builder.Configuration, "CasaOsUpdateSettings:MaxJsonBytes", "CASAOS_MAX_JSON_BYTES");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:DoItBaseUrl", "DOIT_BASE_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:DoItOpenUrl", "DOIT_OPEN_URL");
ApplyEnvOverride(
    builder.Configuration,
    "HouseholdConnectionSettings:GamesDatabaseBaseUrl",
    "GAMESDATABASE_BASE_URL"
);
ApplyEnvOverride(
    builder.Configuration,
    "HouseholdConnectionSettings:GamesDatabaseOpenUrl",
    "GAMESDATABASE_OPEN_URL"
);
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:JellywatchBaseUrl", "JELLYWATCH_BASE_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:JellywatchOpenUrl", "JELLYWATCH_OPEN_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:BeastVaultBaseUrl", "BEASTVAULT_BASE_URL");
ApplyEnvOverride(builder.Configuration, "HouseholdConnectionSettings:BeastVaultOpenUrl", "BEASTVAULT_OPEN_URL");
ApplyEnvOverride(
    builder.Configuration,
    "HouseholdConnectionSettings:WarcraftArchiveBaseUrl",
    "WARCRAFTARCHIVE_BASE_URL"
);
ApplyEnvOverride(
    builder.Configuration,
    "HouseholdConnectionSettings:WarcraftArchiveOpenUrl",
    "WARCRAFTARCHIVE_OPEN_URL"
);

// CORS: support comma-separated FRONTEND_URL or CORS_ALLOWED_ORIGINS
var corsOriginEnv =
    Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? Environment.GetEnvironmentVariable("FRONTEND_URL");
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
builder.Services.Configure<AppLauncherSettings>(builder.Configuration.GetSection(AppLauncherSettings.SectionName));
builder.Services.Configure<DockerSettings>(builder.Configuration.GetSection(DockerSettings.SectionName));
builder.Services.Configure<GamesDatabaseSettings>(builder.Configuration.GetSection(GamesDatabaseSettings.SectionName));
builder.Services.Configure<HouseholdConnectionSettings>(
    builder.Configuration.GetSection(HouseholdConnectionSettings.SectionName)
);
builder.Services.Configure<ExternalIntegrationSettings>(
    builder.Configuration.GetSection(ExternalIntegrationSettings.SectionName)
);
builder.Services.Configure<CasaOsUpdateSettings>(
    builder.Configuration.GetSection(CasaOsUpdateSettings.SectionName)
);

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
    throw new InvalidOperationException("JWT SecretKey is not configured. Set JWT_SECRET_KEY environment variable.");

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            ClockSkew = TimeSpan.FromSeconds(30),
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
            },
            OnTokenValidated = async ctx =>
            {
                var userId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var version = ctx.Principal?.FindFirstValue(HouseholdClaimTypes.SessionVersion);
                if (!Guid.TryParse(userId, out var parsedUserId) || !int.TryParse(version, out var parsedVersion))
                {
                    ctx.Fail("Session is invalid.");
                    return;
                }
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userState = await db.Users
                    .AsNoTracking()
                    .Where(user => user.Id == parsedUserId && user.IsActive)
                    .Select(user => new { user.SessionVersion, user.RequiresPasswordChange })
                    .SingleOrDefaultAsync(ctx.HttpContext.RequestAborted);
                if (userState is null || userState.SessionVersion != parsedVersion)
                {
                    ctx.Fail("Session is no longer valid.");
                    return;
                }

                if (ctx.Principal?.Identity is ClaimsIdentity identity)
                {
                    var existingClaim = identity.FindFirst(HouseholdClaimTypes.RequiresPasswordChange);
                    if (existingClaim is not null)
                        identity.RemoveClaim(existingClaim);
                    identity.AddClaim(
                        new Claim(
                            HouseholdClaimTypes.RequiresPasswordChange,
                            userState.RequiresPasswordChange.ToString().ToLowerInvariant()
                        )
                    );
                }
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "integration-authorize",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }
            )
    );
    static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(10, TimeSpan.FromMinutes(5))));
    options.AddPolicy("invite", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(10, TimeSpan.FromMinutes(10))));
    options.AddPolicy("admin", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => Window(30, TimeSpan.FromMinutes(1))));
    options.AddPolicy("mutation", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => Window(60, TimeSpan.FromMinutes(1))));
    options.AddPolicy("app-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => Window(12, TimeSpan.FromMinutes(1))));
    options.AddPolicy("asset", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => Window(240, TimeSpan.FromMinutes(1))));
    options.AddPolicy("download", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => Window(10, TimeSpan.FromMinutes(1))));
    options.AddPolicy("casaos-admin-action", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"}:{context.Request.RouteValues["appId"] ?? "unknown"}",
        _ => Window(2, TimeSpan.FromMinutes(10))));
    options.AddPolicy(
        "integration-callback",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }
            )
    );
});

// ── CORS ──────────────────────────────────────────────────────────────────────
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new();

if (corsSettings.AllowedOrigins.Count == 0 && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "CORS: no AllowedOrigins configured for production. "
            + "Set CORS_ALLOWED_ORIGINS env var or CorsSettings:AllowedOrigins[]. "
            + "Refusing to start with AllowAnyOrigin in production."
    );

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowSpecificOrigins",
        policy =>
        {
            if (corsSettings.AllowedOrigins.Count > 0)
            {
                // JWT Bearer: AllowCredentials() is NOT needed.
                // If you ever switch to cookies, add AllowCredentials() + correct SameSite.
                policy.WithOrigins(corsSettings.AllowedOrigins.ToArray()).AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                // Development only: allow any origin when no specific origins are configured.
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
        }
    );

    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new()
        {
            Title = "Household API",
            Version = "v1",
            Description = "Backend para gestión doméstica: comida, tareas del hogar e incidencias.",
        }
    );

    c.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT Bearer. Formato: Bearer {token}",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        }
    );

    c.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                Array.Empty<string>()
            },
        }
    );
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
var householdConnectionSettings =
    builder.Configuration.GetSection(HouseholdConnectionSettings.SectionName).Get<HouseholdConnectionSettings>()
    ?? new();
var dataProtectionKeysPath = householdConnectionSettings.DataProtectionKeysPath;
if (!Path.IsPathRooted(dataProtectionKeysPath))
    dataProtectionKeysPath = Path.GetFullPath(dataProtectionKeysPath);
Directory.CreateDirectory(dataProtectionKeysPath);
builder
    .Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("Household.Api");
builder.Services.AddScoped<ISecretProtector, SecretProtector>();
builder.Services.AddSingleton<IIntegrationRegistry, IntegrationRegistry>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
builder.Services.AddScoped<IIntegrationHealthService, IntegrationHealthService>();
builder.Services.AddScoped<IDashboardAggregationService, DashboardAggregationService>();
builder.Services.AddScoped<IIntegrationActionLogService, IntegrationActionLogService>();
builder.Services.AddScoped<IUserSettingsService, UserSettingsService>();
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
builder.Services.AddScoped<IAppLauncherConfigLoader, AppLauncherConfigLoader>();
builder.Services.AddScoped<IAppCatalogService, AppCatalogService>();
builder.Services.AddScoped<IDockerClient, DockerClient>();
builder.Services.AddScoped<IContainerStatusService, ContainerStatusService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationIdHandler>();
builder.Services.ConfigureHttpClientDefaults(
    httpClientBuilder => httpClientBuilder.AddHttpMessageHandler<CorrelationIdHandler>()
);
builder.Services.AddHttpClient<IDoItClient, DoItClient>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHttpClient<IGamesDatabaseClient, GamesDatabaseClient>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHttpClient<IJellywatchClient, JellywatchClient>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHttpClient<IBeastVaultClient, BeastVaultClient>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHttpClient<IWarcraftArchiveClient, WarcraftArchiveClient>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddSingleton<JellyfinImageGrants>();
builder.Services.AddHttpClient<IJellyfinService, JellyfinService>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddSingleton<GitHubActionsRuntimeCache>();
builder.Services.AddHttpClient<IGitHubActionsMonitor, GitHubActionsMonitor>().ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHostedService<GitHubActionsPollingService>();
builder.Services.AddSingleton<CasaOsUpdateLocks>();
builder.Services.AddHttpClient<ICasaOsUpdateService, CasaOsUpdateService>()
    .ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddHttpClient("AppHealth", client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);
builder.Services.AddSingleton<HouseholdProviderRegistry>();
builder.Services.AddSingleton<HouseholdConnectionCoordinator>();
builder.Services.AddScoped<HouseholdConsumerConnectionService>();
builder.Services.AddScoped<IHouseholdProviderAccessService>(services =>
    services.GetRequiredService<HouseholdConsumerConnectionService>()
);
builder.Services.AddHttpClient("HouseholdProviders", client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(NoRedirectHandler);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Migrate & seed ────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var seedCfg =
        scope
            .ServiceProvider.GetRequiredService<IConfiguration>()
            .GetSection(SeedSettings.SectionName)
            .Get<SeedSettings>()
        ?? new();

    await SeedAsync(db, seedCfg, scope.ServiceProvider.GetRequiredService<ILogger<Program>>());
}

var adminCommandExitCode = await AdminRecoveryCommand.TryRunAsync(args, app.Services);
if (adminCommandExitCode.HasValue)
{
    Environment.ExitCode = adminCommandExitCode.Value;
    return;
}

// ── Middleware pipeline ────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Household API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        await next(context);
    }
);

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(
    async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/integrations/callback"))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        }
        await next(context);
    }
);

if (app.Environment.IsDevelopment())
    app.UseCors("AllowAll");
else
    app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

// ── Health ────────────────────────────────────────────────────────────────────
app.MapGet(
        "/health",
        async (AppDbContext db, ILogger<Program> logger) =>
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
            var payload = new
            {
                status = overall,
                version,
                db = dbStatus,
                timestamp = DateTime.UtcNow,
            };
            return overall == "healthy" ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
        }
    )
    .AllowAnonymous()
    .WithTags("System");

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapFoodItemEndpoints();
app.MapDishEndpoints();
app.MapMealEndpoints();
app.MapRoomEndpoints();
app.MapTaskEndpoints();
app.MapIssueEndpoints();
app.MapIntegrationEndpoints();
app.MapDashboardEndpoints();
app.MapAppsModuleEndpoints();
app.MapDoItModuleEndpoints();
app.MapGamesModuleEndpoints();
app.MapJellywatchModuleEndpoints();
app.MapPokemonModuleEndpoints();
app.MapWarcraftModuleEndpoints();
app.MapHouseholdConnectionEndpoints();
app.MapSettingsEndpoints();
app.MapJellyfinEndpoints();
app.MapGitHubActionsEndpoints();
app.MapCasaOsUpdateEndpoints();

app.Run();

// ── Seed helper ───────────────────────────────────────────────────────────────
static async Task SeedAsync(AppDbContext db, SeedSettings settings, ILogger logger)
{
    if (!settings.AdminEnabled)
        return;

    var adminExists = await db.Users.AnyAsync(u => u.IsAdmin);
    if (adminExists)
        return;

    if (!AdminRecoveryCommand.PasswordMeetsRequirements(settings.AdminPassword))
        throw new InvalidOperationException("Seed admin password does not meet the minimum password policy.");
    var passwordHash = BCrypt.Net.BCrypt.HashPassword(settings.AdminPassword, workFactor: 12);

    var admin = new User
    {
        Email = settings.AdminEmail,
        UserName = settings.AdminUsername,
        PasswordHash = passwordHash,
        IsAdmin = true,
        IsActive = true,
    };

    db.Users.Add(admin);
    await db.SaveChangesAsync();
    logger.LogInformation("Seed: admin user created with email {Email}", settings.AdminEmail);
}

// ── Env override helpers ──────────────────────────────────────────────────────
static void ApplyEnvOverride(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value))
        config[key] = value;
}

static void ApplyEnvOverrideInt(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (int.TryParse(value, out _))
        config[key] = value!;
}

static void ApplyEnvOverrideBool(IConfigurationRoot config, string key, string envVar)
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value))
        config[key] = value;
}

static HttpMessageHandler NoRedirectHandler() => new HttpClientHandler { AllowAutoRedirect = false };
