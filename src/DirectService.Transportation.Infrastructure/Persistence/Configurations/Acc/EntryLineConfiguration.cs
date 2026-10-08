// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class EntryLineConfiguration : IEntityTypeConfiguration<EntryLine>
{
    public void Configure(EntityTypeBuilder<EntryLine> builder)
    {
        builder.ToTable("EntryLines", "acc", t =>
        {
            t.HasCheckConstraint("CK_EntryLines_Debit", "[Debit] >= 0");
            t.HasCheckConstraint("CK_EntryLines_Credit", "[Credit] >= 0");
            t.HasCheckConstraint("CK_EntryLines_PartyModule", "[PartyModule] IN ('TRN','ACC')");
            t.HasCheckConstraint("CK_EntryLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_EntryLines_Debit_Credit", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
        });

        builder.HasKey(e => e.Id).HasName("PK_EntryLines");

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
        builder.Property(e => e.JournalEntryId)
            .HasColumnName("JournalEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.LineNo)
            .HasColumnName("LineNo")
            .HasColumnType("int")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.AccountId)
            .HasColumnName("AccountId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.Debit)
            .HasColumnName("Debit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Credit)
            .HasColumnName("Credit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(8)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.CostCentreId)
            .HasColumnName("CostCentreId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.PartyModule)
            .HasColumnName("PartyModule")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(10)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(false);
        builder.Property(e => e.PartyType)
            .HasColumnName("PartyType")
            .HasColumnType("varchar(15)")
            .HasColumnOrder(11)
            .IsUnicode(false)
            .HasMaxLength(15)
            .IsRequired(false);
        builder.Property(e => e.PartyId)
            .HasColumnName("PartyId")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.Note)
            .HasColumnName("Note")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(13)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
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

        builder.HasIndex(new[] { "PublicId" }, "UX_EntryLines_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "AccountId" }, "IX_EntryLines_Account")
            .IncludeProperties("Debit", "Credit");
        builder.HasIndex(new[] { "CompanyId", "PartyModule", "PartyType", "PartyId" }, "IX_EntryLines_Party");
        builder.HasIndex(new[] { "JournalEntryId" }, "IX_EntryLines_JournalEntryId");
        builder.HasIndex(new[] { "CostCentreId" }, "IX_EntryLines_CostCentreId");

        builder.HasOne(e => e.JournalEntry)
            .WithMany(p => p.Lines)
            .HasForeignKey(e => e.JournalEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_EntryLines_JournalEntries_JournalEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_EntryLines_Accounts_AccountId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.CostCentre)
            .WithMany()
            .HasForeignKey(e => e.CostCentreId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_EntryLines_CostCentres_CostCentreId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_EntryLines_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_EntryLines_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_EntryLines_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_EntryLines_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_EntryLines_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
