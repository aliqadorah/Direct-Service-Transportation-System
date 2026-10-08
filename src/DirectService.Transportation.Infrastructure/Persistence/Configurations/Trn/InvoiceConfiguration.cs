// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("TRN_Invoices", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Invoices_InvoiceType", "[InvoiceType] IN ('Trip','Manual')");
            t.HasCheckConstraint("CK_TRN_Invoices_Subtotal", "[Subtotal] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_ExtraFees", "[ExtraFees] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_TRN_Invoices_Tax", "[Tax] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_PaidAmount", "[PaidAmount] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_ReturnedAmount", "[ReturnedAmount] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_RemainingAmount", "[RemainingAmount] >= 0");
            t.HasCheckConstraint("CK_TRN_Invoices_Status", "[Status] IN ('Draft','Approved','Closed','Void')");
            t.HasCheckConstraint("CK_TRN_Invoices_LayoutType", "[LayoutType] IN ('Claim','TaxInvoice')");
            t.HasCheckConstraint("CK_TRN_Invoices_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Invoices_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Invoices_DueDate", "[DueDate] >= [InvoiceDate]");
            t.HasCheckConstraint("CK_TRN_Invoices_Discount", "[Discount] >= 0 AND [Discount] <= [Subtotal]");
            t.HasCheckConstraint("CK_TRN_Invoices_BaseAmount", "[BaseAmount] = [Subtotal] - [Discount] + [ExtraFees]");
            t.HasCheckConstraint("CK_TRN_Invoices_Total", "[Total] = [BaseAmount] + [Tax]");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Invoices");

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
            .IsRequired(false);
        builder.Property(e => e.InvoiceType)
            .HasColumnName("InvoiceType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.CustomerId)
            .HasColumnName("CustomerId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.InvoiceDate)
            .HasColumnName("InvoiceDate")
            .HasColumnType("date")
            .HasColumnOrder(7)
            .IsRequired(true);
        builder.Property(e => e.DueDate)
            .HasColumnName("DueDate")
            .HasColumnType("date")
            .HasColumnOrder(8)
            .IsRequired(true);
        builder.Property(e => e.Subtotal)
            .HasColumnName("Subtotal")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(9)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Discount)
            .HasColumnName("Discount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(10)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ExtraFees)
            .HasColumnName("ExtraFees")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(11)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ExtraFeesDescription)
            .HasColumnName("ExtraFeesDescription")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(12)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(false);
        builder.Property(e => e.BaseAmount)
            .HasColumnName("BaseAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(13)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.TaxRate)
            .HasColumnName("TaxRate")
            .HasColumnType("decimal(5,2)")
            .HasColumnOrder(14)
            .HasPrecision(5, 2)
            .IsRequired(true);
        builder.Property(e => e.Tax)
            .HasColumnName("Tax")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(15)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Total)
            .HasColumnName("Total")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(16)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.PaidAmount)
            .HasColumnName("PaidAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(17)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ReturnedAmount)
            .HasColumnName("ReturnedAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(18)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.RemainingAmount)
            .HasColumnName("RemainingAmount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(19)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(20)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Draft'")
            .ValueGeneratedNever();
        builder.Property(e => e.LayoutType)
            .HasColumnName("LayoutType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(21)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Claim'")
            .ValueGeneratedNever();
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(22)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(false);
        builder.Property(e => e.ApprovedBy)
            .HasColumnName("ApprovedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(23)
            .IsRequired(false);
        builder.Property(e => e.ApprovedAt)
            .HasColumnName("ApprovedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(24)
            .IsRequired(false);
        builder.Property(e => e.JournalEntryId)
            .HasColumnName("JournalEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(25)
            .IsRequired(false);
        builder.Property(e => e.VoidReason)
            .HasColumnName("VoidReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(26)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.VoidedBy)
            .HasColumnName("VoidedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(27)
            .IsRequired(false);
        builder.Property(e => e.VoidedAt)
            .HasColumnName("VoidedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(28)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(29)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(30)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(31)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(32)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(33)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(34)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(35)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(36)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(37)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Invoices_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Number" }, "UX_TRN_Invoices_CompanyId_Number")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Number] IS NOT NULL");
        builder.HasIndex(new[] { "CompanyId", "CustomerId", "Status" }, "IX_TRN_Invoices_Customer");
        builder.HasIndex(new[] { "CompanyId", "InvoiceDate" }, "IX_TRN_Invoices_Date");
        builder.HasIndex(new[] { "CustomerId" }, "IX_TRN_Invoices_CustomerId");
        builder.HasIndex(new[] { "ApprovedBy" }, "IX_TRN_Invoices_ApprovedBy");
        builder.HasIndex(new[] { "JournalEntryId" }, "IX_TRN_Invoices_JournalEntryId");
        builder.HasIndex(new[] { "VoidedBy" }, "IX_TRN_Invoices_VoidedBy");

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Invoices_TRN_Customers_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ApprovedByUser)
            .WithMany()
            .HasForeignKey(e => e.ApprovedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Invoices_Users_ApprovedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.JournalEntry)
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Invoices_JournalEntries_JournalEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.VoidedByUser)
            .WithMany()
            .HasForeignKey(e => e.VoidedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Invoices_Users_VoidedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Invoices_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Invoices_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Invoices_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Invoices_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Invoices_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
