// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class JournalExportBatchConfiguration : IEntityTypeConfiguration<JournalExportBatch>
{
    public void Configure(EntityTypeBuilder<JournalExportBatch> builder)
    {
        builder.ToTable("JournalExportBatches", "acc", t =>
        {
            t.HasCheckConstraint("CK_JournalExportBatches_Format", "[Format] IN ('JSON','CSV')");
            t.HasCheckConstraint("CK_JournalExportBatches_EntryCount", "[EntryCount] >= 0");
            t.HasCheckConstraint("CK_JournalExportBatches_TotalDebit", "[TotalDebit] >= 0");
            t.HasCheckConstraint("CK_JournalExportBatches_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_JournalExportBatches");

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
        builder.Property(e => e.BatchNo)
            .HasColumnName("BatchNo")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.PeriodFrom)
            .HasColumnName("PeriodFrom")
            .HasColumnType("char(7)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(7)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.PeriodTo)
            .HasColumnName("PeriodTo")
            .HasColumnType("char(7)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(7)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.Format)
            .HasColumnName("Format")
            .HasColumnType("varchar(5)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(5)
            .IsRequired(true);
        builder.Property(e => e.EntryCount)
            .HasColumnName("EntryCount")
            .HasColumnType("int")
            .HasColumnOrder(8)
            .IsRequired(true);
        builder.Property(e => e.TotalDebit)
            .HasColumnName("TotalDebit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(9)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.TotalCredit)
            .HasColumnName("TotalCredit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(10)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.ExportedBy)
            .HasColumnName("ExportedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(11)
            .IsRequired(true);
        builder.Property(e => e.ExportedAt)
            .HasColumnName("ExportedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(17)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(19)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(20)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(21)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_JournalExportBatches_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "ExportedBy" }, "IX_JournalExportBatches_ExportedBy");
        builder.HasIndex(new[] { "CompanyId" }, "IX_JournalExportBatches_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.ExportedByUser)
            .WithMany()
            .HasForeignKey(e => e.ExportedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_JournalExportBatches_Users_ExportedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_JournalExportBatches_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_JournalExportBatches_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_JournalExportBatches_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_JournalExportBatches_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_JournalExportBatches_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
