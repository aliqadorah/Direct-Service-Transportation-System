// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Acc;

namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Acc;

internal sealed class ApArDocumentConfiguration : IEntityTypeConfiguration<ApArDocument>
{
    public void Configure(EntityTypeBuilder<ApArDocument> builder)
    {
        builder.ToTable("ApArDocuments", "acc", t =>
        {
            t.HasCheckConstraint("CK_ApArDocuments_DocumentKind", "[DocumentKind] IN ('SupplierBill','Receivable')");
            t.HasCheckConstraint("CK_ApArDocuments_Subtotal", "[Subtotal] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_Tax", "[Tax] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_Deduction", "[Deduction] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_Total", "[Total] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_PaidAmount", "[PaidAmount] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_ReturnedAmount", "[ReturnedAmount] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_RemainingAmount", "[RemainingAmount] >= 0");
            t.HasCheckConstraint("CK_ApArDocuments_Status", "[Status] IN ('Posted','Void')");
            t.HasCheckConstraint("CK_ApArDocuments_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_ApArDocuments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_ApArDocuments_DueDate", "[DueDate] >= [DocumentDate]");
        });

        builder.HasKey(e => e.Id).HasName("PK_ApArDocuments");

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
        builder.Property(e => e.DocumentKind)
            .HasColumnName("DocumentKind")
            .HasColumnType("varchar(15)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(15)
            .IsRequired(true);
        builder.Property(e => e.PartyId)
            .HasColumnName("PartyId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.DocumentDate)
            .HasColumnName("DocumentDate")
            .HasColumnType("date")
            .HasColumnOrder(7)
            .IsRequired(true);
        builder.Property(e => e.DueDate)
            .HasColumnName("DueDate")
            .HasColumnType("date")
            .HasColumnOrder(8)
            .IsRequired(true);
        builder.Property(e => e.Reference)
            .HasColumnName("Reference")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.Subtotal)
            .HasColumnName("Subtotal")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(10)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Tax)
            .HasColumnName("Tax")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(11)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Total)
            .HasColumnName("Total")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(12)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.PaidAmount)
            .HasColumnName("PaidAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(13)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ReturnedAmount)
            .HasColumnName("ReturnedAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(14)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.RemainingAmount)
            .HasColumnName("RemainingAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(15)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(16)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Posted'")
            .ValueGeneratedNever();
        builder.Property(e => e.JournalEntryId)
            .HasColumnName("JournalEntryId")
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
        // Revision 6: Σ line deductions of a supplier bill (Total = Subtotal + Tax − Deduction).
        builder.Property(e => e.Deduction)
            .HasColumnName("Deduction")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(30)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();

        builder.HasIndex(new[] { "PublicId" }, "UX_ApArDocuments_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "DocumentKind", "Number" }, "UX_ApArDocuments_CompanyId_DocumentKind_Number")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "PartyId" }, "IX_ApArDocuments_PartyId");
        builder.HasIndex(new[] { "JournalEntryId" }, "IX_ApArDocuments_JournalEntryId");
        builder.HasIndex(new[] { "VoidedBy" }, "IX_ApArDocuments_VoidedBy");

        builder.HasOne(e => e.Party)
            .WithMany()
            .HasForeignKey(e => e.PartyId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_ApArDocuments_ApArParties_PartyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.JournalEntry)
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_ApArDocuments_JournalEntries_JournalEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.VoidedByUser)
            .WithMany()
            .HasForeignKey(e => e.VoidedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_ApArDocuments_Users_VoidedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_ApArDocuments_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_ApArDocuments_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_ApArDocuments_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_ApArDocuments_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_ApArDocuments_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
