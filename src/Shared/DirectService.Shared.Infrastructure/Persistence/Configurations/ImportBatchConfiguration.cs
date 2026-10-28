// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 5; the foreign keys to core.Tenants / Companies / Users are in Transportation.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches", "cfg", t =>
        {
            t.HasCheckConstraint("CK_ImportBatches_Mode", "[Mode] IN ('Strict','Tolerant')");
            t.HasCheckConstraint("CK_ImportBatches_TotalRows", "[TotalRows] >= 0");
            t.HasCheckConstraint("CK_ImportBatches_ImportedRows", "[ImportedRows] >= 0");
            t.HasCheckConstraint("CK_ImportBatches_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_ImportBatches");

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
        builder.Property(e => e.EntityType)
            .HasColumnName("EntityType")
            .HasColumnType("varchar(40)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(40)
            .IsRequired(true);
        builder.Property(e => e.FileName)
            .HasColumnName("FileName")
            .HasColumnType("nvarchar(260)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(260)
            .IsRequired(true);
        builder.Property(e => e.Mode)
            .HasColumnName("Mode")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Strict'")
            .ValueGeneratedNever();
        builder.Property(e => e.TotalRows)
            .HasColumnName("TotalRows")
            .HasColumnType("int")
            .HasColumnOrder(7)
            .IsRequired(true);
        builder.Property(e => e.ImportedRows)
            .HasColumnName("ImportedRows")
            .HasColumnType("int")
            .HasColumnOrder(8)
            .IsRequired(true);
        builder.Property(e => e.RejectedRows)
            .HasColumnName("RejectedRows")
            .HasColumnType("int")
            .HasColumnOrder(9)
            .IsRequired(true);
        builder.Property(e => e.ImportedBy)
            .HasColumnName("ImportedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(true);
        builder.Property(e => e.ImportedAt)
            .HasColumnName("ImportedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(11)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(16)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(19)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(20)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_ImportBatches_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "ImportedBy" }, "IX_ImportBatches_ImportedBy");
        builder.HasIndex(new[] { "CompanyId" }, "IX_ImportBatches_CompanyId")
            .HasFilter("[IsDeleted] = 0");
    }
}
