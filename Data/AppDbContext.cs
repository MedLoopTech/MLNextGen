using MedLoop.NextGen.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MedLoop.NextGen.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Pharmacy)
            .WithMany(p => p.Users)
            .HasForeignKey(u => u.PharmacyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
