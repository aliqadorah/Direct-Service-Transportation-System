// Revision 6 part B (owner change request 15 Oct 2026): budgets. Hand-written in the style of the generated configurations.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("Budgets", "acc", t =>
        {
            t.HasCheckConstraint("CK_Budgets_FiscalYear", "[FiscalYear] BETWEEN 2000 AND 2100");
            t.HasCheckConstraint("CK_Budgets_Status", "[Status] IN (N'Draft', N'Approved')");
            t.HasCheckConstraint("CK_Budgets_Status_ApprovedAt_ApprovedBy", "[Status] <> N'Approved' OR ([ApprovedAt] IS NOT NULL AND [ApprovedBy] IS NOT NULL)");
            t.HasCheckConstraint("CK_Budgets_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Budgets");

        builder.Property(e => e.Id).HasColumnName("Id").HasColumnType("bigint").HasColumnOrder(0).IsRequired(true).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnName("PublicId").HasColumnType("uniqueidentifier").HasColumnOrder(1).IsRequired(true)
            .HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint").HasColumnOrder(2).IsRequired(true);
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint").HasColumnOrder(3).IsRequired(true);
        builder.Property(e => e.FiscalYear).HasColumnName("FiscalYear").HasColumnType("int").HasColumnOrder(4).IsRequired(true);
        builder.Property(e => e.Name).HasColumnName("Name").HasColumnType("nvarchar(200)").HasColumnOrder(5).IsUnicode(true).HasMaxLength(200).IsRequired(true);
        builder.Property(e => e.Status).HasColumnName("Status").HasColumnType("nvarchar(20)").HasColumnOrder(6).IsUnicode(true).HasMaxLength(20).IsRequired(true)
            .HasDefaultValueSql("N'Draft'").ValueGeneratedNever();
        builder.Property(e => e.ApprovedAt).HasColumnName("ApprovedAt").HasColumnType("datetime2(3)").HasColumnOrder(7).IsRequired(false);
        builder.Property(e => e.ApprovedBy).HasColumnName("ApprovedBy").HasColumnType("bigint").HasColumnOrder(8).IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime2(3)").HasColumnOrder(9).IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasColumnName("CreatedBy").HasColumnType("bigint").HasColumnOrder(10).IsRequired(false);
        builder.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime2(3)").HasColumnOrder(11).IsRequired(false);
        builder.Property(e => e.UpdatedBy).HasColumnName("UpdatedBy").HasColumnType("bigint").HasColumnOrder(12).IsRequired(false);
        builder.Property(e => e.IsDeleted).HasColumnName("IsDeleted").HasColumnType("bit").HasColumnOrder(13).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.DeletedAt).HasColumnName("DeletedAt").HasColumnType("datetime2(3)").HasColumnOrder(14).IsRequired(false);
        builder.Property(e => e.DeletedBy).HasColumnName("DeletedBy").HasColumnType("bigint").HasColumnOrder(15).IsRequired(false);
        builder.Property(e => e.DeleteReason).HasColumnName("DeleteReason").HasColumnType("nvarchar(500)").HasColumnOrder(16).IsUnicode(true)
            .HasMaxLength(500).IsRequired(false);
        builder.Property(e => e.RowVersion).HasColumnName("RowVersion").HasColumnType("rowversion").HasColumnOrder(17).IsRequired(true).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_Budgets_PublicId").IsUnique().HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "FiscalYear", "Name" }, "UX_Budgets_CompanyId_FiscalYear_Name").IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "FiscalYear" }, "UX_Budgets_CompanyId_FiscalYear_Approved").IsUnique()
            .HasFilter("[Status] = N'Approved' AND [IsDeleted] = 0");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).HasConstraintName("FK_Budgets_Tenants_TenantId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("FK_Budgets_Companies_CompanyId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ApprovedBy).HasConstraintName("FK_Budgets_Users_ApprovedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedBy).HasConstraintName("FK_Budgets_Users_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedBy).HasConstraintName("FK_Budgets_Users_UpdatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedBy).HasConstraintName("FK_Budgets_Users_DeletedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
