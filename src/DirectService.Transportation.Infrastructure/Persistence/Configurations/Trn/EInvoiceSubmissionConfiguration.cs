// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class EInvoiceSubmissionConfiguration : IEntityTypeConfiguration<EInvoiceSubmission>
{
    public void Configure(EntityTypeBuilder<EInvoiceSubmission> builder)
    {
        builder.ToTable("TRN_EInvoiceSubmissions", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_EInvoiceSubmissions_DocumentType", "[DocumentType] IN ('Invoice','CreditNote')");
            t.HasCheckConstraint("CK_TRN_EInvoiceSubmissions_Status", "[Status] IN ('Queued','Submitted','Rejected','NotRequired')");
            t.HasCheckConstraint("CK_TRN_EInvoiceSubmissions_Attempts", "[Attempts] >= 0");
            t.HasCheckConstraint("CK_TRN_EInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_EInvoiceSubmissions_InvoiceId_CreditNoteId", "([DocumentType] = 'Invoice' AND [InvoiceId] IS NOT NULL AND [CreditNoteId] IS NULL) OR ([DocumentType] = 'CreditNote' AND [CreditNoteId] IS NOT NULL AND [InvoiceId] IS NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_EInvoiceSubmissions");

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
        builder.Property(e => e.DocumentType)
            .HasColumnName("DocumentType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.InvoiceId)
            .HasColumnName("InvoiceId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(false);
        builder.Property(e => e.CreditNoteId)
            .HasColumnName("CreditNoteId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(12)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(12)
            .IsRequired(true)
            .HasDefaultValueSql("'Queued'")
            .ValueGeneratedNever();
        builder.Property(e => e.Icv)
            .HasColumnName("Icv")
            .HasColumnType("bigint")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.UniqueId)
            .HasColumnName("UniqueId")
            .HasColumnType("uniqueidentifier")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.SystemNumber)
            .HasColumnName("SystemNumber")
            .HasColumnType("varchar(50)")
            .HasColumnOrder(10)
            .IsUnicode(false)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.QrCode)
            .HasColumnName("QrCode")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.RequestPayload)
            .HasColumnName("RequestPayload")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(12)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.ResponsePayload)
            .HasColumnName("ResponsePayload")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(13)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.Attempts)
            .HasColumnName("Attempts")
            .HasColumnType("int")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.LastError)
            .HasColumnName("LastError")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(15)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(false);
        builder.Property(e => e.SubmittedAt)
            .HasColumnName("SubmittedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(19)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(21)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(22)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(23)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(24)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(25)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_EInvoiceSubmissions_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Icv" }, "UX_TRN_EInvoiceSubmissions_CompanyId_Icv")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Icv] IS NOT NULL");
        builder.HasIndex(new[] { "CompanyId", "Status" }, "IX_TRN_EInvoiceSubmissions_Queue")
            .HasFilter("[Status] IN ('Queued','Rejected')");
        builder.HasIndex(new[] { "InvoiceId" }, "IX_TRN_EInvoiceSubmissions_InvoiceId");
        builder.HasIndex(new[] { "CreditNoteId" }, "IX_TRN_EInvoiceSubmissions_CreditNoteId");

        builder.HasOne(e => e.Invoice)
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_TRN_Invoices_InvoiceId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.CreditNote)
            .WithMany()
            .HasForeignKey(e => e.CreditNoteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_TRN_CreditNotes_CreditNoteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_EInvoiceSubmissions_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
