using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context
{
    public virtual DbSet<B2BPortalUser> B2BPortalUsers { get; set; }
    public virtual DbSet<B2BRequest> B2BRequests { get; set; }
    public virtual DbSet<B2BRequestItem> B2BRequestItems { get; set; }
    public virtual DbSet<B2BRequestAttachment> B2BRequestAttachments { get; set; }
    public virtual DbSet<ProductFactoryAlternative> ProductFactoryAlternatives { get; set; }
    public virtual DbSet<ProductFactoryAlternativeImage> ProductFactoryAlternativeImages { get; set; }

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
            entity.Property(e => e.RequestSource).HasMaxLength(50).HasDefaultValue("Portal");
            entity.Property(e => e.RequestedContactName).HasMaxLength(200);
            entity.Property(e => e.RequestedContactPhone).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.InternalNotes).HasMaxLength(2000);
            entity.Property(e => e.CustomerResponse).HasMaxLength(2000);
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

        modelBuilder.Entity<B2BRequestAttachment>(entity =>
        {
            entity.HasKey(e => e.AttachmentId);
            entity.ToTable("B2BRequestAttachments");

            entity.HasIndex(e => e.RequestId);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.RelativePath).HasMaxLength(500);
            entity.Property(e => e.ContentType).HasMaxLength(150);
            entity.Property(e => e.UploadedAt).HasColumnType("datetime");
            entity.Property(e => e.UploadedBy).HasMaxLength(100);

            entity.HasOne(e => e.Request)
                .WithMany(e => e.Attachments)
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_B2BRequestAttachments_B2BRequests");
        });

        modelBuilder.Entity<ProductFactoryAlternative>(entity =>
        {
            entity.HasKey(e => e.AlternativeId);
            entity.ToTable("ProductFactoryAlternatives");

            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.AlternativeId).HasColumnName("AlternativeID");
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.AlternativeName).HasMaxLength(150);
            entity.Property(e => e.SpecificationSummary).HasMaxLength(1000);
            entity.Property(e => e.ManufacturingDescription).HasMaxLength(2000);
            entity.Property(e => e.PurchasePriceCClass).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PurchasePricePremium).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PurchasePriceElite).HasColumnType("decimal(18,2)");
            entity.Property(e => e.SuggestedSalePriceCClass).HasColumnType("decimal(18,2)");
            entity.Property(e => e.SuggestedSalePricePremium).HasColumnType("decimal(18,2)");
            entity.Property(e => e.SuggestedSalePriceElite).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Status).HasMaxLength(30).HasDefaultValue("Proposed");
            entity.Property(e => e.IsPrimary).HasDefaultValue(false);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.ReviewedBy).HasMaxLength(100);
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime");

            entity.HasOne(e => e.Product)
                .WithMany(e => e.ProductFactoryAlternatives)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProductFactoryAlternatives_Products");
        });

        modelBuilder.Entity<ProductFactoryAlternativeImage>(entity =>
        {
            entity.HasKey(e => e.AlternativeImageId);
            entity.ToTable("ProductFactoryAlternativeImages");

            entity.HasIndex(e => e.AlternativeId);

            entity.Property(e => e.AlternativeImageId).HasColumnName("AlternativeImageID");
            entity.Property(e => e.AlternativeId).HasColumnName("AlternativeID");
            entity.Property(e => e.ImagePath).HasMaxLength(500);
            entity.Property(e => e.Caption).HasMaxLength(200);
            entity.Property(e => e.IsPrimary).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");

            entity.HasOne(e => e.Alternative)
                .WithMany(e => e.Images)
                .HasForeignKey(e => e.AlternativeId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProductFactoryAlternativeImages_Alternatives");
        });
    }
}
