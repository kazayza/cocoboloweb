using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context
{
    public virtual DbSet<B2BPortalUser> B2BPortalUsers { get; set; }
    public virtual DbSet<B2BRequest> B2BRequests { get; set; }
    public virtual DbSet<B2BRequestItem> B2BRequestItems { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<B2BPortalUser>(entity =>
        {
            entity.HasKey(e => e.PortalUserId);
            entity.ToTable("B2BPortalUsers");

            entity.HasIndex(e => e.UserName).IsUnique();
            entity.HasIndex(e => e.PartyId);
            entity.HasIndex(e => e.ResponsibleEmployeeId);

            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.HashedPassword).HasMaxLength(500);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Mobile).HasMaxLength(50);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastLogin).HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CanViewPrices).HasDefaultValue(true);
            entity.Property(e => e.CanViewFinancials).HasDefaultValue(true);
            entity.Property(e => e.CanRequestQuotation).HasDefaultValue(true);
            entity.Property(e => e.CanUploadPaymentProof).HasDefaultValue(true);

            entity.HasOne(e => e.Party)
                .WithMany()
                .HasForeignKey(e => e.PartyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BPortalUsers_Parties");

            entity.HasOne(e => e.ResponsibleEmployee)
                .WithMany()
                .HasForeignKey(e => e.ResponsibleEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BPortalUsers_Employees");
        });

        modelBuilder.Entity<B2BRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);
            entity.ToTable("B2BRequests");

            entity.HasIndex(e => e.PartyId);
            entity.HasIndex(e => e.PortalUserId);
            entity.HasIndex(e => e.ResponsibleEmployeeId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RequestType);

            entity.Property(e => e.RequestType).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.HandledAt).HasColumnType("datetime");
            entity.Property(e => e.HandledBy).HasMaxLength(100);

            entity.HasOne(e => e.Party)
                .WithMany()
                .HasForeignKey(e => e.PartyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BRequests_Parties");

            entity.HasOne(e => e.PortalUser)
                .WithMany(e => e.B2BRequests)
                .HasForeignKey(e => e.PortalUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BRequests_B2BPortalUsers");

            entity.HasOne(e => e.ResponsibleEmployee)
                .WithMany()
                .HasForeignKey(e => e.ResponsibleEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BRequests_Employees");
        });

        modelBuilder.Entity<B2BRequestItem>(entity =>
        {
            entity.HasKey(e => e.RequestItemId);
            entity.ToTable("B2BRequestItems");

            entity.HasIndex(e => e.RequestId);
            entity.HasIndex(e => e.ProductId);

            entity.Property(e => e.Quantity).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasOne(e => e.Request)
                .WithMany(e => e.Items)
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_B2BRequestItems_B2BRequests");

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_B2BRequestItems_Products");
        });
    }
}
