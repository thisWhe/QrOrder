global using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QrOrder.Application.Common;
using QrOrder.Application.Orders;
using QrOrder.Application.Public;
using QrOrder.Application.ServiceCalls;
using QrOrder.Infrastructure.Auth;
using QrOrder.Infrastructure.Data;
using QrOrder.Infrastructure.Services;
using QrOrder.Web.Realtime;
using QrOrder.Web.Seed;
using QrOrder.Web.Storage;
using QrOrder.Web.Health;
using QrOrder.Web.Middleware;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;



var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("QrOrder")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Serilog
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext();
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("mssql", failureStatus: HealthStatus.Unhealthy);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "Çok fazla istek",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Kısa sürede çok fazla işlem yapıldı. Lütfen biraz bekleyip tekrar deneyin.",
            traceId = context.HttpContext.TraceIdentifier
        }, cancellationToken);
    };
    options.AddPolicy("staff-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("public-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddSingleton<QrOrder.Infrastructure.Auth.JwtTokenService>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();



builder.Services.AddHttpClient("samehost", client =>
{
    var baseUrl = builder.Configuration["PublicBaseUrl"] ?? "http://localhost:5140";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<QrOrder.Web.Ui.PublicApiClient>();




builder.Services.AddScoped<QrOrder.Web.Ui.LocalStorageService>();

builder.Services.AddScoped<IPublicMenuService, PublicMenuService>();
builder.Services.AddScoped<IPublicTableSessionService, PublicTableSessionService>();
builder.Services.AddScoped<IPublicOrderService, PublicOrderService>();
builder.Services.AddScoped<IStaffOrderService, StaffOrderService>();
builder.Services.AddScoped<IPublicServiceCallService, PublicServiceCallService>();
builder.Services.AddScoped<IStaffServiceCallService, StaffServiceCallService>();
builder.Services.AddScoped<IBusinessHoursService, BusinessHoursService>();
builder.Services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();
builder.Services.AddSingleton<ITenantBrandingStorage, LocalTenantBrandingStorage>();


// Swagger + JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => 
{
    c.CustomSchemaIds(type =>
    {
        // Nested type ise (Controller+CreateReq gibi) tam adını kullan
        // Değilse normal Name yeter
        return type.FullName?.Replace("+", ".") ?? type.Name;
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
        
    });
  

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id="Bearer" }
            },
            new string[] {}
        }
    });
});

// Tenant context (Scoped)
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddScoped<QrOrder.Infrastructure.Data.TenantSaveChangesInterceptor>();

// EF Core SQL Server
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    opt.AddInterceptors(sp.GetRequiredService<QrOrder.Infrastructure.Data.TenantSaveChangesInterceptor>());
}
);

// Identity (Guid keys)
builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.Password.RequiredLength = 10;
    opt.Password.RequiredUniqueChars = 4;
    opt.Password.RequireUppercase = true;
    opt.Password.RequireLowercase = true;
    opt.Password.RequireDigit = true;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Lockout.AllowedForNewUsers = true;
    opt.Lockout.MaxFailedAccessAttempts = 5;
    opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>();

// JWT Auth
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new System.Exception("JWT Key missing. Check appsettings.json / user-secrets / env vars.");

var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/staff-orders"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var tenantIdValue = context.Principal?.FindFirst("tenant_id")?.Value;
                var tokenSecurityStamp = context.Principal?.FindFirst("security_stamp")?.Value;

                if (!Guid.TryParse(userIdValue, out var userId) ||
                    !Guid.TryParse(tenantIdValue, out var tenantId) ||
                    string.IsNullOrWhiteSpace(tokenSecurityStamp))
                {
                    context.Fail("Invalid staff token.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(u => u.Id == userId);
                var tenantIsActive = await db.Tenants
                    .AnyAsync(t => t.Id == tenantId && t.IsActive);

                if (user == null ||
                    user.TenantId != tenantId ||
                    !tenantIsActive ||
                    !string.Equals(user.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
                {
                    context.Fail("Staff token has been revoked.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// SignalR
builder.Services.AddSignalR();


// ✅ BURASI: Build artık en sonda
var app = builder.Build();

ValidateProductionConfiguration(app);

var uploadsPath = ResolveUploadsPath(app);
Directory.CreateDirectory(uploadsPath);
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    Directory.CreateDirectory(dataProtectionKeysPath);

app.UseForwardedHeaders();

// Swagger sadece dev'de
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tenantId))
            diagnosticContext.Set("TenantId", tenantId);

        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
    };
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();

// Tenant’i JWT claim’den set eden middleware
app.Use(async (ctx, next) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
    {
        var tenantIdClaim = ctx.User.FindFirst("tenant_id")?.Value;
        if (System.Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            var tenantContext = ctx.RequestServices.GetRequiredService<ITenantContext>();
            tenantContext.TenantId = tenantId;
        }
    }

    await next();
});

app.UseAuthorization();

var seedDemoData = app.Configuration.GetValue<bool?>("Seed:DemoData") ?? app.Environment.IsDevelopment();
var applyMigrations = app.Configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup") ?? app.Environment.IsDevelopment();
await DBSeeder.SeedAsync(
    app.Services,
    applyMigrations,
    seedDemoData,
    app.Configuration["Bootstrap:SuperAdminEmail"],
    app.Configuration["Bootstrap:SuperAdminPassword"]);


app.MapControllers();
app.MapHub<StaffOrdersHub>("/hubs/staff-orders");
app.MapHub<PublicOrdersHub>("/hubs/public-orders");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            }),
            traceId = context.TraceIdentifier
        }));
    }
});


app.MapRazorPages();
app.MapBlazorHub();

app.MapFallbackToPage("/_Host");

app.Run();

static void ValidateProductionConfiguration(WebApplication app)
{
    if (!app.Environment.IsProduction())
        return;

    var publicBaseUrl = app.Configuration["PublicBaseUrl"];
    if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri) || publicUri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("Production requires PublicBaseUrl with an absolute https URL.");

    var allowedHosts = app.Configuration["AllowedHosts"];
    var configuredHosts = allowedHosts?
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];
    if (configuredHosts.Length == 0 || configuredHosts.Any(host => host == "*"))
        throw new InvalidOperationException("Production requires AllowedHosts to contain the public host instead of '*'.");

    if (!configuredHosts.Any(host => string.Equals(host, publicUri.Host, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("Production AllowedHosts must include the PublicBaseUrl host.");

    var connectionString = app.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Production requires ConnectionStrings:Default.");

    if (connectionString.Contains("YOUR_SQL_SERVER", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Production database connection string still contains placeholder values.");

    var jwtKey = app.Configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 48)
        throw new InvalidOperationException("Production requires a strong Jwt:Key with at least 48 characters.");

    if (app.Configuration.GetValue<bool>("Seed:DemoData"))
        throw new InvalidOperationException("Seed:DemoData must be false in Production.");

    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        throw new InvalidOperationException("Database:ApplyMigrationsOnStartup must be false in Production.");

    ValidateExternalDirectory(app, "Storage:UploadsPath");
    ValidateExternalDirectory(app, "DataProtection:KeysPath");
    ValidateExternalFile(app, "Serilog:WriteTo:1:Args:path");
}

static string ResolveUploadsPath(WebApplication app)
{
    var configuredPath = app.Configuration["Storage:UploadsPath"];
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return Path.GetFullPath(configuredPath);

    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    return Path.Combine(webRoot, "uploads");
}

static void ValidateExternalFile(WebApplication app, string configurationKey)
{
    var configuredPath = app.Configuration[configurationKey];
    if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath))
        throw new InvalidOperationException($"Production requires {configurationKey} as an absolute path.");

    var directory = Path.GetDirectoryName(Path.GetFullPath(configuredPath));
    if (string.IsNullOrWhiteSpace(directory))
        throw new InvalidOperationException($"Production requires a valid directory for {configurationKey}.");

    var contentRoot = Path.GetFullPath(app.Environment.ContentRootPath)
        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (directory.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{configurationKey} must be outside the deployment directory in Production.");

    Directory.CreateDirectory(directory);
}

static void ValidateExternalDirectory(WebApplication app, string configurationKey)
{
    var configuredPath = app.Configuration[configurationKey];
    if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath))
        throw new InvalidOperationException($"Production requires {configurationKey} as an absolute path.");

    var fullPath = Path.GetFullPath(configuredPath);
    var contentRoot = Path.GetFullPath(app.Environment.ContentRootPath)
        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    if (fullPath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{configurationKey} must be outside the deployment directory in Production.");
}


