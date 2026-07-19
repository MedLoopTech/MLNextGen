using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Data;

// Dev-only convenience: creates a ready-to-use seller account, buyer account,
// admin account, and a couple of marketplace listings, so a fresh
// `docker compose up` isn't a blank slate that needs manual registration
// before the B2B flow can be exercised. Only called in Development (see
// Program.cs) and idempotent — safe to run on every startup, since each step
// checks for the record it would create before creating it.
public static class DbSeeder
{
    public const string DemoPassword = "MedLoopDemo123!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var db = services.GetRequiredService<AppDbContext>();

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        var sellerPharmacy = await GetOrCreatePharmacyAsync(db, "Demo Seller Pharmacy");
        var buyerPharmacy = await GetOrCreatePharmacyAsync(db, "Demo Buyer Pharmacy");

        var seller = await GetOrCreateUserAsync(userManager, "seller@medloop.test", "Demo Seller", sellerPharmacy.Id);
        await GetOrCreateUserAsync(userManager, "buyer@medloop.test", "Demo Buyer", buyerPharmacy.Id);
        var admin = await GetOrCreateUserAsync(userManager, "admin@medloop.test", "Demo Admin", pharmacyId: null);

        if (!await userManager.IsInRoleAsync(admin, "Admin"))
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }

        if (!await db.Products.AnyAsync(p => p.PharmacyId == sellerPharmacy.Id))
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Title == "Pain Relief");
            if (category is null)
            {
                category = new Category { Title = "Pain Relief" };
                db.Categories.Add(category);
            }

            db.Products.AddRange(
                new Product
                {
                    Name = "Paracetamol 500mg",
                    ProductCode = "PARA500",
                    PharmacyId = sellerPharmacy.Id,
                    CategoryId = category.Id,
                    Price = 2.5,
                    Quantity = 100,
                    Status = ProductListingStatus.ForRedistribution,
                    ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(18)),
                    CreatedById = seller.Id
                },
                new Product
                {
                    Name = "Amoxicillin 250mg",
                    ProductCode = "AMOX250",
                    PharmacyId = sellerPharmacy.Id,
                    CategoryId = category.Id,
                    Price = 4.0,
                    Quantity = 60,
                    Status = ProductListingStatus.ForRedistribution,
                    ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
                    CreatedById = seller.Id
                });

            await db.SaveChangesAsync();
        }
    }

    private static async Task<Pharmacy> GetOrCreatePharmacyAsync(AppDbContext db, string name)
    {
        var pharmacy = await db.Pharmacies.FirstOrDefaultAsync(p => p.Name == name);
        if (pharmacy is not null)
        {
            return pharmacy;
        }

        pharmacy = new Pharmacy { Name = name };
        db.Pharmacies.Add(pharmacy);
        await db.SaveChangesAsync();
        return pharmacy;
    }

    private static async Task<ApplicationUser> GetOrCreateUserAsync(
        UserManager<ApplicationUser> userManager, string email, string displayName, string? pharmacyId)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            PharmacyId = pharmacyId
        };

        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed demo user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        return user;
    }
}
