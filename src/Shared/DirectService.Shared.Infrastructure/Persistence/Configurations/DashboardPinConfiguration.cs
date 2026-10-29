// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 5; the foreign keys to core.Tenants / Companies / Users are in Platform.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class DashboardPinConfiguration : IEntityTypeConfiguration<DashboardPin>
{
    public void Configure(EntityTypeBuilder<DashboardPin> builder)
    {
        builder.ToTable("DashboardPins", "cfg", t =>
        {
            t.HasCheckConstraint("CK_DashboardPins_WarnWhen", "[WarnWhen] IN ('Above','Below')");
            t.HasCheckConstraint("CK_DashboardPins_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_DashboardPins");

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
        builder.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.SavedReportId)
            .HasColumnName("SavedReportId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.Measure)
            .HasColumnName("Measure")
            .HasColumnType("varchar(60)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(60)
            .IsRequired(true);
        builder.Property(e => e.WarningThreshold)
            .HasColumnName("WarningThreshold")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(false);
        // Revision 6 (owner findings 27 Oct 2026, DT-275): §B14.
        builder.Property(e => e.WarnWhen)
            .HasColumnName("WarnWhen")
            .HasColumnType("varchar(10)")
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Above'");
        builder.Property(e => e.CriticalThreshold)
            .HasColumnName("CriticalThreshold")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(8)
            .HasPrecision(18, 3)
            .IsRequired(false);
        builder.Property(e => e.SortOrder)
            .HasColumnName("SortOrder")
            .HasColumnType("int")
            .HasColumnOrder(9)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
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

        builder.HasIndex(new[] { "PublicId" }, "UX_DashboardPins_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "UserId" }, "IX_DashboardPins_UserId");
        builder.HasIndex(new[] { "SavedReportId" }, "IX_DashboardPins_SavedReportId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_DashboardPins_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.SavedReport)
            .WithMany()
            .HasForeignKey(e => e.SavedReportId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_DashboardPins_SavedReports_SavedReportId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
