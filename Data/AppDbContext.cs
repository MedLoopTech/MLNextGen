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
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Pharmacy)
            .WithMany(p => p.Users)
            .HasForeignKey(u => u.PharmacyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Branch>(entity =>
        {
            entity.HasOne(b => b.Pharmacy)
                .WithMany(p => p.Branches)
                .HasForeignKey(b => b.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(b => b.PharmacyId);
        });

        builder.Entity<Warehouse>(entity =>
        {
            entity.HasOne(w => w.Pharmacy)
                .WithMany(p => p.Warehouses)
                .HasForeignKey(w => w.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(w => w.Branch)
                .WithMany(b => b.Warehouses)
                .HasForeignKey(w => w.BranchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(w => w.PharmacyId);
            entity.HasIndex(w => w.BranchId);
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(p => p.Pharmacy)
                .WithMany(ph => ph.Products)
                .HasForeignKey(p => p.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Branch)
                .WithMany(b => b.Products)
                .HasForeignKey(p => p.BranchId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(p => p.Warehouse)
                .WithMany(w => w.Products)
                .HasForeignKey(p => p.WarehouseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(p => p.PharmacyId);
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.ExpiryDate);
        });
    }
}
