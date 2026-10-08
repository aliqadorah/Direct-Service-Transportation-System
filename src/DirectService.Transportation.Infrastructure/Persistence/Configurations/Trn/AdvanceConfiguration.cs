// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class AdvanceConfiguration : IEntityTypeConfiguration<Advance>
{
    public void Configure(EntityTypeBuilder<Advance> builder)
    {
        builder.ToTable("TRN_Advances", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Advances_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_TRN_Advances_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
            t.HasCheckConstraint("CK_TRN_Advances_Status", "[Status] IN ('Active','Void')");
            t.HasCheckConstraint("CK_TRN_Advances_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Advances_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
            t.HasCheckConstraint("CK_TRN_Advances_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Advances");

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
        builder.Property(e => e.DriverId)
            .HasColumnName("DriverId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.AdvanceDate)
            .HasColumnName("AdvanceDate")
            .HasColumnType("date")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.PaymentMethod)
            .HasColumnName("PaymentMethod")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.CashBoxId)
            .HasColumnName("CashBoxId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.BankAccountId)
            .HasColumnName("BankAccountId")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.Reference)
            .HasColumnName("Reference")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(12)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(13)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.SettlementId)
            .HasColumnName("SettlementId")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.JournalEntryId)
            .HasColumnName("JournalEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.VoidReason)
            .HasColumnName("VoidReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(16)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.VoidedBy)
            .HasColumnName("VoidedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.VoidedAt)
            .HasColumnName("VoidedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(19)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(22)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(23)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(24)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(25)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(26)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(27)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Advances_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Number" }, "UX_TRN_Advances_CompanyId_Number")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "DriverId" }, "IX_TRN_Advances_DriverId");
        builder.HasIndex(new[] { "CashBoxId" }, "IX_TRN_Advances_CashBoxId");
        builder.HasIndex(new[] { "BankAccountId" }, "IX_TRN_Advances_BankAccountId");
        builder.HasIndex(new[] { "SettlementId" }, "IX_TRN_Advances_SettlementId");
        builder.HasIndex(new[] { "JournalEntryId" }, "IX_TRN_Advances_JournalEntryId");
        builder.HasIndex(new[] { "VoidedBy" }, "IX_TRN_Advances_VoidedBy");

        builder.HasOne(e => e.Driver)
            .WithMany()
            .HasForeignKey(e => e.DriverId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Advances_TRN_Drivers_DriverId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.CashBox)
            .WithMany()
            .HasForeignKey(e => e.CashBoxId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Advances_CashBoxes_CashBoxId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.BankAccount)
            .WithMany()
            .HasForeignKey(e => e.BankAccountId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Advances_BankAccounts_BankAccountId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Settlement)
            .WithMany()
            .HasForeignKey(e => e.SettlementId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Advances_TRN_Settlements_SettlementId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.JournalEntry)
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Advances_JournalEntries_JournalEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.VoidedByUser)
            .WithMany()
            .HasForeignKey(e => e.VoidedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Advances_Users_VoidedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Advances_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Advances_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Advances_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Advances_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Advances_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
