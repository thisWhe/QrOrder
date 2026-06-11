global using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;



var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .WriteTo.Console();
});

builder.Services.AddControllers();

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
builder.Services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();


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
    opt.Password.RequireNonAlphanumeric = false; // MVP kolaylık
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
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
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

                if (user == null ||
                    user.TenantId != tenantId ||
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

// Swagger sadece dev'de
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

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
await DBSeeder.SeedAsync(app.Services, seedDemoData);


app.MapControllers();
app.MapHub<StaffOrdersHub>("/hubs/staff-orders");
app.MapHub<PublicOrdersHub>("/hubs/public-orders");


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

    var connectionString = app.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Production requires ConnectionStrings:Default.");

    if (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Production database connection string must not point to localhost or SQLEXPRESS.");

    var jwtKey = app.Configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 48)
        throw new InvalidOperationException("Production requires a strong Jwt:Key with at least 48 characters.");

    if (app.Configuration.GetValue<bool>("Seed:DemoData"))
        throw new InvalidOperationException("Seed:DemoData must be false in Production.");
}


