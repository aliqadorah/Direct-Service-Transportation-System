// Revision 6 part B (owner change request 15 Oct 2026): budget lines. Hand-written in the style of the generated configurations.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    private static readonly string[] MonthColumns = Enumerable.Range(1, 12).Select(m => $"Month{m:00}").ToArray();

    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        builder.ToTable("BudgetLines", "acc", t =>
        {
            t.HasCheckConstraint("CK_BudgetLines_Total", "[Total] = " + string.Join(" + ", MonthColumns.Select(c => $"[{c}]")));
            t.HasCheckConstraint("CK_BudgetLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_BudgetLines");

        builder.Property(e => e.Id).HasColumnName("Id").HasColumnType("bigint").HasColumnOrder(0).IsRequired(true).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnName("PublicId").HasColumnType("uniqueidentifier").HasColumnOrder(1).IsRequired(true)
            .HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint").HasColumnOrder(2).IsRequired(true);
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint").HasColumnOrder(3).IsRequired(true);
        builder.Property(e => e.BudgetId).HasColumnName("BudgetId").HasColumnType("bigint").HasColumnOrder(4).IsRequired(true);
        builder.Property(e => e.AccountId).HasColumnName("AccountId").HasColumnType("bigint").HasColumnOrder(5).IsRequired(true);
        builder.Property(e => e.CostCentreId).HasColumnName("CostCentreId").HasColumnType("bigint").HasColumnOrder(6).IsRequired(false);
        for (var m = 0; m < 12; m++)
        {
            builder.Property<decimal>(MonthColumns[m]).HasColumnName(MonthColumns[m]).HasColumnType("decimal(18,3)").HasColumnOrder(7 + m).HasPrecision(18, 3)
                .IsRequired(true).HasDefaultValueSql("0").ValueGeneratedNever();
        }
        builder.Property(e => e.Total).HasColumnName("Total").HasColumnType("decimal(18,3)").HasColumnOrder(19).HasPrecision(18, 3).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime2(3)").HasColumnOrder(20).IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasColumnName("CreatedBy").HasColumnType("bigint").HasColumnOrder(21).IsRequired(false);
        builder.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime2(3)").HasColumnOrder(22).IsRequired(false);
        builder.Property(e => e.UpdatedBy).HasColumnName("UpdatedBy").HasColumnType("bigint").HasColumnOrder(23).IsRequired(false);
        builder.Property(e => e.IsDeleted).HasColumnName("IsDeleted").HasColumnType("bit").HasColumnOrder(24).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.DeletedAt).HasColumnName("DeletedAt").HasColumnType("datetime2(3)").HasColumnOrder(25).IsRequired(false);
        builder.Property(e => e.DeletedBy).HasColumnName("DeletedBy").HasColumnType("bigint").HasColumnOrder(26).IsRequired(false);
        builder.Property(e => e.DeleteReason).HasColumnName("DeleteReason").HasColumnType("nvarchar(500)").HasColumnOrder(27).IsUnicode(true)
            .HasMaxLength(500).IsRequired(false);
        builder.Property(e => e.RowVersion).HasColumnName("RowVersion").HasColumnType("rowversion").HasColumnOrder(28).IsRequired(true).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_BudgetLines_PublicId").IsUnique().HasFilter(null);
        builder.HasIndex(new[] { "BudgetId", "AccountId", "CostCentreId" }, "UX_BudgetLines_BudgetId_AccountId_CostCentreId").IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.Budget).WithMany(b => b.Lines).HasForeignKey(e => e.BudgetId).HasPrincipalKey(p => p.Id).IsRequired(true)
            .HasConstraintName("FK_BudgetLines_Budgets_BudgetId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Account).WithMany().HasForeignKey(e => e.AccountId).HasPrincipalKey(p => p.Id).IsRequired(true)
            .HasConstraintName("FK_BudgetLines_Accounts_AccountId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCentre>().WithMany().HasForeignKey(e => e.CostCentreId).HasConstraintName("FK_BudgetLines_CostCentres_CostCentreId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).HasConstraintName("FK_BudgetLines_Tenants_TenantId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("FK_BudgetLines_Companies_CompanyId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedBy).HasConstraintName("FK_BudgetLines_Users_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedBy).HasConstraintName("FK_BudgetLines_Users_UpdatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedBy).HasConstraintName("FK_BudgetLines_Users_DeletedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
