using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Auth;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Seed
{
    public static class DBSeeder
    {
        public static async Task SeedAsync(IServiceProvider sp, bool seedDemoData)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            await db.Database.MigrateAsync();

            foreach (var role in new[] { "SuperAdmin", "Admin", "Kitchen", "Service" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }

            if (!seedDemoData)
                return;

            var platformTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "platform");
            if (platformTenant == null)
            {
                platformTenant = new Tenant
                {
                    Name = "Platform",
                    Slug = "platform",
                    IsActive = true,
                    IsOrderingEnabled = false,
                    TableSessionHours = 12
                };

                db.Tenants.Add(platformTenant);
                await db.SaveChangesAsync();
            }

            tenantContext.TenantId = platformTenant.Id;
            await EnsureStaffUserAsync(userManager, platformTenant.Id, "superadmin@demo.com", "SuperAdmin123!", "SuperAdmin");

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "demo-cafe");
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    Name = "Demo Cafe",
                    Slug = "demo-cafe",
                    IsActive = true,
                    IsOrderingEnabled = true,
                    TableSessionHours = 12
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
            }
            else if (!tenant.IsActive || tenant.TableSessionHours <= 0)
            {
                tenant.IsActive = true;
                if (tenant.TableSessionHours <= 0)
                    tenant.TableSessionHours = 12;

                await db.SaveChangesAsync();
            }

            tenantContext.TenantId = tenant.Id;

            var adminEmail = "admin@demo.com";
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    TenantId = tenant.Id,
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(admin, "Admin123!");
                if (!result.Succeeded)
                    throw new Exception(string.Join("; ", result.Errors.Select(e => e.Description)));

                await userManager.AddToRoleAsync(admin, "Admin");
            }

            await EnsureStaffUserAsync(userManager, tenant.Id, "kitchen@demo.com", "Kitchen123!", "Kitchen");
            await EnsureStaffUserAsync(userManager, tenant.Id, "service@demo.com", "Service123!", "Service");

            var category = await db.Categories
                .FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.Name == "Drinks");

            if (category == null)
            {
                category = new Category
                {
                    TenantId = tenant.Id,
                    Name = "Drinks",
                    SortOrder = 0,
                    IsActive = true
                };

                db.Categories.Add(category);
                await db.SaveChangesAsync();
            }

            var hasAnyProduct = await db.Products.AnyAsync(p => p.TenantId == tenant.Id);
            if (!hasAnyProduct)
            {
                db.Products.AddRange(
                    new Product
                    {
                        TenantId = tenant.Id,
                        CategoryId = category.Id,
                        Name = "Water",
                        Description = "500ml",
                        Price = 20m,
                        IsActive = true
                    },
                    new Product
                    {
                        TenantId = tenant.Id,
                        CategoryId = category.Id,
                        Name = "Coffee",
                        Description = "Filter coffee",
                        Price = 60m,
                        IsActive = true
                    },
                    new Product
                    {
                        TenantId = tenant.Id,
                        CategoryId = category.Id,
                        Name = "Tea",
                        Description = "Black tea",
                        Price = 30m,
                        IsActive = true
                    });

                await db.SaveChangesAsync();
            }
        }

        private static async Task EnsureStaffUserAsync(
            UserManager<ApplicationUser> userManager,
            Guid tenantId,
            string email,
            string password,
            string role)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    TenantId = tenantId,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                    throw new Exception(string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            if (!await userManager.IsInRoleAsync(user, role))
                await userManager.AddToRoleAsync(user, role);
        }
    }
}
