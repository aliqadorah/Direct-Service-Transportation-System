// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("TRN_InvoiceLines", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_InvoiceLines_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_TRN_InvoiceLines_UnitPrice", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_TRN_InvoiceLines_Amount", "[Amount] >= 0");
            t.HasCheckConstraint("CK_TRN_InvoiceLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_InvoiceLines");

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
        builder.Property(e => e.InvoiceId)
            .HasColumnName("InvoiceId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.TripId)
            .HasColumnName("TripId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(false);
        builder.Property(e => e.Description)
            .HasColumnName("Description")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(6)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.Quantity)
            .HasColumnName("Quantity")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.UnitPrice)
            .HasColumnName("UnitPrice")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(8)
            .HasPrecision(18, 4)
            .IsRequired(true);
        builder.Property(e => e.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(9)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.SortOrder)
            .HasColumnName("SortOrder")
            .HasColumnType("int")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(11)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(15)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(18)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(19)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_InvoiceLines_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "InvoiceId" }, "IX_TRN_InvoiceLines_InvoiceId");
        builder.HasIndex(new[] { "TripId" }, "IX_TRN_InvoiceLines_TripId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_TRN_InvoiceLines_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.Invoice)
            .WithMany(p => p.Lines)
            .HasForeignKey(e => e.InvoiceId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_InvoiceLines_TRN_Invoices_InvoiceId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Trip)
            .WithMany()
            .HasForeignKey(e => e.TripId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_InvoiceLines_TRN_Trips_TripId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_InvoiceLines_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_InvoiceLines_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_InvoiceLines_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_InvoiceLines_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_InvoiceLines_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
