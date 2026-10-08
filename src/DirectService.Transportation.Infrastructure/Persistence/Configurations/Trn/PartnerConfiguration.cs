// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("TRN_Partners", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Partners_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_TRN_Partners_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Partners");

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
        builder.Property(e => e.Name)
            .HasColumnName("Name")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.TaxNumber)
            .HasColumnName("TaxNumber")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.Phone)
            .HasColumnName("Phone")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.Address)
            .HasColumnName("Address")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.ContactPerson)
            .HasColumnName("ContactPerson")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.BankName)
            .HasColumnName("BankName")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.Iban)
            .HasColumnName("Iban")
            .HasColumnType("varchar(34)")
            .HasColumnOrder(10)
            .IsUnicode(false)
            .HasMaxLength(34)
            .IsRequired(false);
        builder.Property(e => e.CliqAlias)
            .HasColumnName("CliqAlias")
            .HasColumnType("nvarchar(50)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(12)
            .IsUnicode(true)
            .HasMaxLength(1000)
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

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Partners_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Name" }, "UX_TRN_Partners_CompanyId_Name")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Active'");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Partners_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Partners_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Partners_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Partners_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Partners_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
