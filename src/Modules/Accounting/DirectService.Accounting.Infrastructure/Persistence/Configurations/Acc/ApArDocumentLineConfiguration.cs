// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Acc;

namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Acc;

internal sealed class ApArDocumentLineConfiguration : IEntityTypeConfiguration<ApArDocumentLine>
{
    public void Configure(EntityTypeBuilder<ApArDocumentLine> builder)
    {
        builder.ToTable("ApArDocumentLines", "acc", t =>
        {
            t.HasCheckConstraint("CK_ApArDocumentLines_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_ApArDocumentLines_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_ApArDocumentLines_Deduction", "[Deduction] >= 0 AND [Deduction] <= [Amount]");
            t.HasCheckConstraint("CK_ApArDocumentLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_ApArDocumentLines");

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
        builder.Property(e => e.DocumentId)
            .HasColumnName("DocumentId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.Description)
            .HasColumnName("Description")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(true);
        builder.Property(e => e.AccountId)
            .HasColumnName("AccountId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.TaxRate)
            .HasColumnName("TaxRate")
            .HasColumnType("decimal(5,2)")
            .HasColumnOrder(8)
            .HasPrecision(5, 2)
            .IsRequired(true);
        builder.Property(e => e.CostCentreId)
            .HasColumnName("CostCentreId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(17)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(18)
            .IsRequired(true)
            .IsRowVersion();
        // Revision 6: line deduction of supplier bills (owner change request 15 Oct 2026).
        builder.Property(e => e.Deduction)
            .HasColumnName("Deduction")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(19)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();

        builder.HasIndex(new[] { "PublicId" }, "UX_ApArDocumentLines_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "DocumentId" }, "IX_ApArDocumentLines_DocumentId");
        builder.HasIndex(new[] { "AccountId" }, "IX_ApArDocumentLines_AccountId");
        builder.HasIndex(new[] { "CostCentreId" }, "IX_ApArDocumentLines_CostCentreId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_ApArDocumentLines_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.Document)
            .WithMany(p => p.Lines)
            .HasForeignKey(e => e.DocumentId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_ApArDocumentLines_ApArDocuments_DocumentId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_ApArDocumentLines_Accounts_AccountId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.CostCentre)
            .WithMany()
            .HasForeignKey(e => e.CostCentreId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_ApArDocumentLines_CostCentres_CostCentreId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_ApArDocumentLines_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_ApArDocumentLines_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_ApArDocumentLines_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_ApArDocumentLines_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_ApArDocumentLines_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
