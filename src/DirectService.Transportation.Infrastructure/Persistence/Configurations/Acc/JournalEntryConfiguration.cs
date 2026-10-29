// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries", "acc", t =>
        {
            t.HasCheckConstraint("CK_JournalEntries_SourceModule", "[SourceModule] IN ('TRN','ACC')");
            t.HasCheckConstraint("CK_JournalEntries_Status", "[Status] IN ('Posted','Reversed','Void')");
            t.HasCheckConstraint("CK_JournalEntries_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_JournalEntries_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_JournalEntries_TotalDebit_TotalCredit", "[TotalDebit] = [TotalCredit]");
        });

        builder.HasKey(e => e.Id).HasName("PK_JournalEntries");

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
        builder.Property(e => e.Number)
            .HasColumnName("Number")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.EntryDate)
            .HasColumnName("EntryDate")
            .HasColumnType("date")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.PeriodMonth)
            .HasColumnName("PeriodMonth")
            .HasColumnType("char(7)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(7)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.Description)
            .HasColumnName("Description")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(true);
        builder.Property(e => e.SourceModule)
            .HasColumnName("SourceModule")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.SourceType)
            .HasColumnName("SourceType")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(9)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.SourceId)
            .HasColumnName("SourceId")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.IsManual)
            .HasColumnName("IsManual")
            .HasColumnType("bit")
            .HasColumnOrder(11)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(12)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Posted'")
            .ValueGeneratedNever();
        builder.Property(e => e.ReversesEntryId)
            .HasColumnName("ReversesEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.ReversedByEntryId)
            .HasColumnName("ReversedByEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.TotalDebit)
            .HasColumnName("TotalDebit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(15)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.TotalCredit)
            .HasColumnName("TotalCredit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(16)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.ExportBatchId)
            .HasColumnName("ExportBatchId")
            .HasColumnType("bigint")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.VoidReason)
            .HasColumnName("VoidReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(18)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.VoidedBy)
            .HasColumnName("VoidedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(19)
            .IsRequired(false);
        builder.Property(e => e.VoidedAt)
            .HasColumnName("VoidedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(21)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(22)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(23)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(24)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(25)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(26)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(27)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(28)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(29)
            .IsRequired(true)
            .IsRowVersion();
        // Revision 6: Reference / المرجع (owner change request 15 Oct 2026).
        builder.Property(e => e.Reference)
            .HasColumnName("Reference")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(30)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.HasIndex(new[] { "PublicId" }, "UX_JournalEntries_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Number" }, "UX_JournalEntries_CompanyId_Number")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "PeriodMonth" }, "IX_JournalEntries_Period");
        builder.HasIndex(new[] { "CompanyId", "SourceModule", "SourceType", "SourceId" }, "IX_JournalEntries_Source");
        builder.HasIndex(new[] { "ReversesEntryId" }, "IX_JournalEntries_ReversesEntryId");
        builder.HasIndex(new[] { "ReversedByEntryId" }, "IX_JournalEntries_ReversedByEntryId");
        builder.HasIndex(new[] { "ExportBatchId" }, "IX_JournalEntries_ExportBatchId");
        builder.HasIndex(new[] { "VoidedBy" }, "IX_JournalEntries_VoidedBy");

        builder.HasOne(e => e.ReversesEntry)
            .WithMany()
            .HasForeignKey(e => e.ReversesEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_JournalEntries_JournalEntries_ReversesEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ReversedByEntry)
            .WithMany()
            .HasForeignKey(e => e.ReversedByEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_JournalEntries_JournalEntries_ReversedByEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ExportBatch)
            .WithMany()
            .HasForeignKey(e => e.ExportBatchId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_JournalEntries_JournalExportBatches_ExportBatchId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.VoidedByUser)
            .WithMany()
            .HasForeignKey(e => e.VoidedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_JournalEntries_Users_VoidedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_JournalEntries_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_JournalEntries_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_JournalEntries_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_JournalEntries_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_JournalEntries_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
