// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 5; the foreign keys to core.Tenants / Companies / Users are in Platform.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> builder)
    {
        builder.ToTable("SavedReports", "cfg", t =>
        {
            t.HasCheckConstraint("CK_SavedReports_Definition", "ISJSON([Definition]) = 1");
            t.HasCheckConstraint("CK_SavedReports_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_SavedReports");

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
        builder.Property(e => e.OwnerUserId)
            .HasColumnName("OwnerUserId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.Name)
            .HasColumnName("Name")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(true);
        builder.Property(e => e.Source)
            .HasColumnName("Source")
            .HasColumnType("varchar(60)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(60)
            .IsRequired(true);
        builder.Property(e => e.Definition)
            .HasColumnName("Definition")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .IsRequired(true);
        builder.Property(e => e.IsShared)
            .HasColumnName("IsShared")
            .HasColumnType("bit")
            .HasColumnOrder(8)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(9)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(16)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(17)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_SavedReports_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "OwnerUserId" }, "IX_SavedReports_OwnerUserId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_SavedReports_CompanyId")
            .HasFilter("[IsDeleted] = 0");
    }
}
