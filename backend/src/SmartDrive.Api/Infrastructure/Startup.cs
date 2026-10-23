using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartDrive.Application.Ai;
using SmartDrive.Application.Data;
using SmartDrive.Application.Data.Seed;
using SmartDrive.Application.Identity;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Recommendations = "recommendations";
    public const string Payments = "payments";
    public const string Contact = "contact";
}

public static class Startup
{
    /// <summary>Registers every service. Business logic lives in SmartDrive.Application; this only wires it up.</summary>
    public static WebApplicationBuilder AddSmartDrive(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;
        using var loggerFactory = LoggerFactory.Create(l => l.AddConsole());
        var startupLogger = loggerFactory.CreateLogger("SmartDrive.Startup");

        // ---------------------------------------------------------------- persistence
        var provider = config["Database:Provider"] ?? "SqlServer";
        var connection = config.GetConnectionString("SmartDrive");
        services.AddDbContext<SmartDriveDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(string.IsNullOrWhiteSpace(connection) ? "Data Source=smartdrive-demo.db" : connection);
            }
            else
            {
                options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3));
            }
        });

        // ---------------------------------------------------------------- identity and authentication
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDriveDbContext>();

        var jwt = JwtOptions.Resolve(config, builder.Environment, startupLogger);
        services.AddSingleton(jwt);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.SecurityKey,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthConstants.SubjectClaim,
                    RoleClaimType = AuthConstants.RoleClaim,
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = JwtEvents.OnTokenValidated };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthConstants.AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(Roles.Administrator));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // ---------------------------------------------------------------- application services
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(config.GetSection("Rental").Get<RentalPolicy>() ?? new RentalPolicy());
        services.AddSingleton(config.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions());

        var webRoot = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
        services.AddSingleton(new MediaStorageOptions
        {
            RootPath = Path.Combine(webRoot, "media", "uploads"),
            PublicBasePath = "/media/uploads",
            MaxBytes = config.GetValue<long?>("Media:MaxBytes") ?? 5 * 1024 * 1024,
        });
        services.AddSingleton<IMediaStorage, LocalMediaStorage>();

        services.AddScoped<AuditWriter>();
        services.AddScoped<Notifier>();
        services.AddScoped<AvailabilityService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<BookingService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<AccountService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<FleetAdminService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<CustomerAdminService>();
        services.AddScoped<CatalogAdminService>();
        services.AddScoped<DataSeeder>();

        // Natural-language preferences: the rule-based interpreter always works; a language model is used
        // only when Ai:LanguageModel is configured, and it falls back to the rules on any failure.
        var languageModel = config.GetSection("Ai:LanguageModel").Get<LanguageModelOptions>() ?? new LanguageModelOptions();
        services.AddSingleton(languageModel);
        services.AddSingleton<RuleBasedPreferenceInterpreter>();
        services.AddHttpClient<IPreferenceInterpreter, LanguageModelPreferenceInterpreter>(c =>
            c.Timeout = TimeSpan.FromSeconds(Math.Clamp(languageModel.TimeoutSeconds, 3, 60)));

        // ---------------------------------------------------------------- HTTP
        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);
        services.AddControllers(o => o.Filters.Add<ValidationFilter>())
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ProblemExceptionHandler>();

        services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithExposedHeaders("Content-Disposition")));

        // Permits per window; configurable so automated tests (and busy deployments) can tune them.
        var limits = config.GetSection("RateLimiting");
        var authPermits = limits.GetValue("AuthPerMinute", 10);
        var recommendationPermits = limits.GetValue("RecommendationsPerMinute", 40);
        var paymentPermits = limits.GetValue("PaymentsPerMinute", 10);
        var contactPermits = limits.GetValue("ContactPerTenMinutes", 5);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitPolicies.Auth, http => FixedWindow(http, authPermits, TimeSpan.FromMinutes(1)));
            o.AddPolicy(RateLimitPolicies.Recommendations, http => FixedWindow(http, recommendationPermits, TimeSpan.FromMinutes(1)));
            o.AddPolicy(RateLimitPolicies.Payments, http => FixedWindow(http, paymentPermits, TimeSpan.FromMinutes(1)));
            o.AddPolicy(RateLimitPolicies.Contact, http => FixedWindow(http, contactPermits, TimeSpan.FromMinutes(10)));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 429,
                    Title = "Too many requests",
                    Detail = "You are sending requests too quickly. Please wait a moment and try again.",
                }, ct);
            };
        });

        services.AddHttpLogging(o =>
        {
            // Method, path, status and duration only: no headers (tokens) and no bodies (personal data).
            o.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath |
                              HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            o.CombineLogs = true;
        });

        services.AddHealthChecks().AddDbContextCheck<SmartDriveDbContext>("database");

        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.AddJsonConsole();
        }

        return builder;
    }

    /// <summary>Partitions by signed-in user when known, otherwise by client IP address.</summary>
    private static RateLimitPartition<string> FixedWindow(HttpContext http, int permits, TimeSpan window)
    {
        var key = http.User.FindFirst(AuthConstants.SubjectClaim)?.Value
                  ?? http.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
    }

    /// <summary>Creates or migrates the database and seeds roles, demo accounts and sample data when configured.</summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartDriveDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SmartDrive.Database");

        if (db.IsSqlite)
        {
            // The SQLite quick-demo and test mode builds the schema directly; migrations target SQL Server.
            await db.Database.EnsureCreatedAsync();
        }
        else if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync();
        }

        if (scope.ServiceProvider.GetRequiredService<SeedOptions>().Enabled)
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
        }
    }

    public static WebApplication UseSmartDrive(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.Use(async (http, next) =>
        {
            var headers = http.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (http.Request.Path.StartsWithSegments("/media"))
            {
                // Media are served as plain images; scripts inside files are never executed.
                headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            }

            await next();
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpLogging();
        app.UseStaticFiles();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        if (app.Configuration.GetValue<bool>("Api:ExposeDocumentation"))
        {
            app.MapOpenApi();
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/openapi/v1.json", "SmartDrive API v1");
                o.RoutePrefix = "swagger";
                o.DocumentTitle = "SmartDrive API";
            });
        }

        app.MapControllers();
        app.MapHealthChecks("/health");
        app.MapGet("/", () => Results.Redirect(app.Configuration.GetValue<bool>("Api:ExposeDocumentation") ? "/swagger" : "/health"))
            .ExcludeFromDescription();
        return app;
    }
}
