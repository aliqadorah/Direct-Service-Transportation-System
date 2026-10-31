// Owner request 25 Oct 2026 (DT-257): tax codes of journal lines. Hand-written in the style of the generated configurations.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Acc;

namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Acc;

internal sealed class TaxCodeConfiguration : IEntityTypeConfiguration<TaxCode>
{
    public void Configure(EntityTypeBuilder<TaxCode> builder)
    {
        builder.ToTable("TaxCodes", "acc", t =>
        {
            t.HasCheckConstraint("CK_TaxCodes_Rate", "[Rate] >= 0 AND [Rate] <= 100");
            t.HasCheckConstraint("CK_TaxCodes_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_TaxCodes_SortOrder", "[SortOrder] >= 0");
            t.HasCheckConstraint("CK_TaxCodes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TaxCodes");

        builder.Property(e => e.Id).HasColumnName("Id").HasColumnType("bigint").HasColumnOrder(0).IsRequired(true).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnName("PublicId").HasColumnType("uniqueidentifier").HasColumnOrder(1).IsRequired(true)
            .HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint").HasColumnOrder(2).IsRequired(true);
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint").HasColumnOrder(3).IsRequired(true);
        builder.Property(e => e.Code).HasColumnName("Code").HasColumnType("varchar(30)").HasColumnOrder(4).IsUnicode(false).HasMaxLength(30).IsRequired(true);
        builder.Property(e => e.Rate).HasColumnName("Rate").HasColumnType("decimal(5,2)").HasColumnOrder(5).HasPrecision(5, 2).IsRequired(true);
        builder.Property(e => e.NameAr).HasColumnName("NameAr").HasColumnType("nvarchar(100)").HasColumnOrder(6).IsUnicode(true).HasMaxLength(100).IsRequired(true);
        builder.Property(e => e.NameEn).HasColumnName("NameEn").HasColumnType("nvarchar(100)").HasColumnOrder(7).IsUnicode(true).HasMaxLength(100).IsRequired(false);
        builder.Property(e => e.Status).HasColumnName("Status").HasColumnType("varchar(10)").HasColumnOrder(8).IsUnicode(false).HasMaxLength(10).IsRequired(true)
            .HasDefaultValueSql("'Active'").ValueGeneratedNever();
        builder.Property(e => e.SortOrder).HasColumnName("SortOrder").HasColumnType("int").HasColumnOrder(9).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime2(3)").HasColumnOrder(10).IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasColumnName("CreatedBy").HasColumnType("bigint").HasColumnOrder(11).IsRequired(false);
        builder.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime2(3)").HasColumnOrder(12).IsRequired(false);
        builder.Property(e => e.UpdatedBy).HasColumnName("UpdatedBy").HasColumnType("bigint").HasColumnOrder(13).IsRequired(false);
        builder.Property(e => e.IsDeleted).HasColumnName("IsDeleted").HasColumnType("bit").HasColumnOrder(14).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.DeletedAt).HasColumnName("DeletedAt").HasColumnType("datetime2(3)").HasColumnOrder(15).IsRequired(false);
        builder.Property(e => e.DeletedBy).HasColumnName("DeletedBy").HasColumnType("bigint").HasColumnOrder(16).IsRequired(false);
        builder.Property(e => e.DeleteReason).HasColumnName("DeleteReason").HasColumnType("nvarchar(500)").HasColumnOrder(17).IsUnicode(true)
            .HasMaxLength(500).IsRequired(false);
        builder.Property(e => e.RowVersion).HasColumnName("RowVersion").HasColumnType("rowversion").HasColumnOrder(18).IsRequired(true).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TaxCodes_PublicId").IsUnique().HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Code" }, "UX_TaxCodes_CompanyId_Code").IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).HasConstraintName("FK_TaxCodes_Tenants_TenantId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("FK_TaxCodes_Companies_CompanyId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedBy).HasConstraintName("FK_TaxCodes_Users_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedBy).HasConstraintName("FK_TaxCodes_Users_UpdatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedBy).HasConstraintName("FK_TaxCodes_Users_DeletedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
