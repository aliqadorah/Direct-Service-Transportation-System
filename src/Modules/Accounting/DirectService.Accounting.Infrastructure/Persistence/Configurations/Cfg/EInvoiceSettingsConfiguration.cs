// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Cfg;

namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Cfg;

internal sealed class EInvoiceSettingsConfiguration : IEntityTypeConfiguration<EInvoiceSettings>
{
    public void Configure(EntityTypeBuilder<EInvoiceSettings> builder)
    {
        builder.ToTable("EInvoiceSettings", "cfg", t =>
        {
            t.HasCheckConstraint("CK_EInvoiceSettings_Environment", "[Environment] IN ('Test','Production')");
            t.HasCheckConstraint("CK_EInvoiceSettings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_EInvoiceSettings");

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
        builder.Property(e => e.IsEnabled)
            .HasColumnName("IsEnabled")
            .HasColumnType("bit")
            .HasColumnOrder(4)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Connector)
            .HasColumnName("Connector")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true)
            .HasDefaultValueSql("'JoFotara'")
            .ValueGeneratedNever();
        builder.Property(e => e.SellerTaxNumber)
            .HasColumnName("SellerTaxNumber")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.RegistrationType)
            .HasColumnName("RegistrationType")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.IncomeSourceSequence)
            .HasColumnName("IncomeSourceSequence")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.ClientIdEncrypted)
            .HasColumnName("ClientIdEncrypted")
            .HasColumnType("varbinary(512)")
            .HasColumnOrder(9)
            .HasMaxLength(512)
            .IsRequired(false);
        builder.Property(e => e.SecretKeyEncrypted)
            .HasColumnName("SecretKeyEncrypted")
            .HasColumnType("varbinary(512)")
            .HasColumnOrder(10)
            .HasMaxLength(512)
            .IsRequired(false);
        builder.Property(e => e.Environment)
            .HasColumnName("Environment")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(11)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Test'")
            .ValueGeneratedNever();
        builder.Property(e => e.IcvCounter)
            .HasColumnName("IcvCounter")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("0")
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

        builder.HasIndex(new[] { "PublicId" }, "UX_EInvoiceSettings_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId" }, "UX_EInvoiceSettings_CompanyId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_EInvoiceSettings_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_EInvoiceSettings_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_EInvoiceSettings_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_EInvoiceSettings_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_EInvoiceSettings_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
