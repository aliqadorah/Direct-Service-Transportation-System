// Revision 6 (owner decision 21 Oct 2026 on DT-228, DT-230): docs/design/Database_Design_Revision6.md §B9.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class PartnerOverpaymentConfiguration : IEntityTypeConfiguration<PartnerOverpayment>
{
    public void Configure(EntityTypeBuilder<PartnerOverpayment> builder)
    {
        builder.ToTable("TRN_PartnerOverpayments", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_PartnerOverpayments_Handling", "[Handling] IN ('Return','Deduct','NotPaid')");
            t.HasCheckConstraint("CK_TRN_PartnerOverpayments_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_TRN_PartnerOverpayments_PartnerPaymentId", "([Handling] = 'NotPaid' AND [PartnerPaymentId] IS NOT NULL) OR ([Handling] <> 'NotPaid' AND [PartnerPaymentId] IS NULL)");
            t.HasCheckConstraint("CK_TRN_PartnerOverpayments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_PartnerOverpayments");

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("bigint")
            .HasColumnOrder(0)
            .IsRequired(true)
            .UseIdentityColumn();
        builder.Property(e => e.PublicId)
            .HasColumnName("PublicId")
            .HasColumnType("uniqueidentifier")
            .HasColumnOrder(1)
            .IsRequired(true)
            .HasDefaultValueSql("NEWID()")
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasColumnName("TenantId")
            .HasColumnType("bigint")
            .HasColumnOrder(2)
            .IsRequired(true);
        builder.Property(e => e.CompanyId)
            .HasColumnName("CompanyId")
            .HasColumnType("bigint")
            .HasColumnOrder(3)
            .IsRequired(true);
        builder.Property(e => e.PartnerId)
            .HasColumnName("PartnerId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.TripId)
            .HasColumnName("TripId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.PartnerPaymentId)
            .HasColumnName("PartnerPaymentId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Handling)
            .HasColumnName("Handling")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.Reason)
            .HasColumnName("Reason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(true);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(17)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(18)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_PartnerOverpayments_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "PartnerId" }, "IX_TRN_PartnerOverpayments_PartnerId");
        builder.HasIndex(new[] { "TripId" }, "IX_TRN_PartnerOverpayments_TripId");
        builder.HasIndex(new[] { "PartnerPaymentId" }, "IX_TRN_PartnerOverpayments_PartnerPaymentId").HasFilter("[PartnerPaymentId] IS NOT NULL");
        builder.HasIndex(new[] { "CompanyId" }, "IX_TRN_PartnerOverpayments_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.Partner)
            .WithMany()
            .HasForeignKey(e => e.PartnerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_PartnerOverpayments_TRN_Partners_PartnerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Trip)
            .WithMany()
            .HasForeignKey(e => e.TripId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_PartnerOverpayments_TRN_Trips_TripId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.PartnerPayment)
            .WithMany()
            .HasForeignKey(e => e.PartnerPaymentId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_PartnerOverpayments_TRN_PartnerPayments_PartnerPaymentId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_PartnerOverpayments_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_PartnerOverpayments_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_PartnerOverpayments_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_PartnerOverpayments_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_PartnerOverpayments_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
