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
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidNegotiationRound> BidNegotiationRounds => Set<BidNegotiationRound>();
    public DbSet<Order> Orders => Set<Order>();

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

            // Postgres's system xmin column as an optimistic concurrency
            // token: any update to a Product (e.g. two bid approvals racing
            // to lock stock on the same listing) that's based on a stale
            // read now throws DbUpdateConcurrencyException instead of
            // silently overwriting. This is the fix for the legacy
            // BidApprovalController's read-LockQuantity-then-write race,
            // which had no protection against exactly that scenario.
            entity.UseXminAsConcurrencyToken();
        });

        builder.Entity<Bid>(entity =>
        {
            entity.Property(b => b.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(b => b.Product)
                .WithMany()
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(b => b.BuyerPharmacy)
                .WithMany()
                .HasForeignKey(b => b.BuyerPharmacyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.BuyerUser)
                .WithMany()
                .HasForeignKey(b => b.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(b => b.ProductId);
            entity.HasIndex(b => b.BuyerPharmacyId);
            entity.HasIndex(b => b.Status);
        });

        builder.Entity<BidNegotiationRound>(entity =>
        {
            entity.Property(r => r.ProposedBy).HasConversion<string>().HasMaxLength(16);

            entity.HasOne(r => r.Bid)
                .WithMany(b => b.NegotiationRounds)
                .HasForeignKey(r => r.BidId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.ProposedByUser)
                .WithMany()
                .HasForeignKey(r => r.ProposedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => r.BidId);
        });

        builder.Entity<Order>(entity =>
        {
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(o => o.Bid)
                .WithMany()
                .HasForeignKey(o => o.BidId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Product)
                .WithMany()
                .HasForeignKey(o => o.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.SellerPharmacy)
                .WithMany()
                .HasForeignKey(o => o.SellerPharmacyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.BuyerPharmacy)
                .WithMany()
                .HasForeignKey(o => o.BuyerPharmacyId)
                .OnDelete(DeleteBehavior.Restrict);

            // One order per bid, enforced at the database level — not just
            // an application-level check — so a retried/duplicated
            // checkout request can never create two paid orders for the
            // same bid even under a race.
            entity.HasIndex(o => o.BidId).IsUnique();
            entity.HasIndex(o => o.BuyerPharmacyId);
            entity.HasIndex(o => o.SellerPharmacyId);
        });
    }
}
