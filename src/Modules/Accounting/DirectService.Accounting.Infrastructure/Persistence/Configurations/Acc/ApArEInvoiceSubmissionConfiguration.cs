// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Acc;

namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Acc;

internal sealed class ApArEInvoiceSubmissionConfiguration : IEntityTypeConfiguration<ApArEInvoiceSubmission>
{
    public void Configure(EntityTypeBuilder<ApArEInvoiceSubmission> builder)
    {
        builder.ToTable("ApArEInvoiceSubmissions", "acc", t =>
        {
            t.HasCheckConstraint("CK_ApArEInvoiceSubmissions_DocumentType", "[DocumentType] IN ('Invoice','CreditNote')");
            t.HasCheckConstraint("CK_ApArEInvoiceSubmissions_Status", "[Status] IN ('Queued','Submitted','Rejected','NotRequired')");
            t.HasCheckConstraint("CK_ApArEInvoiceSubmissions_Attempts", "[Attempts] >= 0");
            t.HasCheckConstraint("CK_ApArEInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_ApArEInvoiceSubmissions");

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
        builder.Property(e => e.ApArDocumentId)
            .HasColumnName("ApArDocumentId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(false);
        builder.Property(e => e.ApArReturnId)
            .HasColumnName("ApArReturnId")
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

        builder.HasIndex(new[] { "PublicId" }, "UX_ApArEInvoiceSubmissions_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Icv" }, "UX_ApArEInvoiceSubmissions_CompanyId_Icv")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Icv] IS NOT NULL");
        builder.HasIndex(new[] { "CompanyId", "Status" }, "IX_ApArEInvoiceSubmissions_Queue")
            .HasFilter("[Status] IN ('Queued','Rejected')");
        builder.HasIndex(new[] { "ApArDocumentId" }, "IX_ApArEInvoiceSubmissions_ApArDocumentId");
        builder.HasIndex(new[] { "ApArReturnId" }, "IX_ApArEInvoiceSubmissions_ApArReturnId");

        builder.HasOne(e => e.ApArDocument)
            .WithMany()
            .HasForeignKey(e => e.ApArDocumentId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_ApArDocuments_ApArDocumentId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ApArReturn)
            .WithMany()
            .HasForeignKey(e => e.ApArReturnId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_ApArReturns_ApArReturnId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_ApArEInvoiceSubmissions_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
