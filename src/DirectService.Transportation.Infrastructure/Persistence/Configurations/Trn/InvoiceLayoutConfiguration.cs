// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class InvoiceLayoutConfiguration : IEntityTypeConfiguration<InvoiceLayout>
{
    public void Configure(EntityTypeBuilder<InvoiceLayout> builder)
    {
        builder.ToTable("TRN_InvoiceLayouts", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_LayoutType", "[LayoutType] IN ('Claim','TaxInvoice')");
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_Grouping", "[Grouping] IN ('LoadingSite','Route','Material')");
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_AnnexColumns", "ISJSON([AnnexColumns]) = 1");
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_Labels", "ISJSON([Labels]) = 1");
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_Signatures", "ISJSON([Signatures]) = 1");
            t.HasCheckConstraint("CK_TRN_InvoiceLayouts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_InvoiceLayouts");

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
        builder.Property(e => e.LayoutType)
            .HasColumnName("LayoutType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.Title)
            .HasColumnName("Title")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.BandText)
            .HasColumnName("BandText")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(6)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.Grouping)
            .HasColumnName("Grouping")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(true)
            .HasDefaultValueSql("'LoadingSite'")
            .ValueGeneratedNever();
        builder.Property(e => e.AnnexColumns)
            .HasColumnName("AnnexColumns")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .IsRequired(true);
        builder.Property(e => e.Labels)
            .HasColumnName("Labels")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.FooterText)
            .HasColumnName("FooterText")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(10)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.Signatures)
            .HasColumnName("Signatures")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.ShowLogo)
            .HasColumnName("ShowLogo")
            .HasColumnType("bit")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.ShowQr)
            .HasColumnName("ShowQr")
            .HasColumnType("bit")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(18)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(19)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(21)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(22)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_InvoiceLayouts_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "LayoutType" }, "UX_TRN_InvoiceLayouts_CompanyId_LayoutType")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_InvoiceLayouts_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_InvoiceLayouts_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_InvoiceLayouts_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_InvoiceLayouts_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_InvoiceLayouts_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
