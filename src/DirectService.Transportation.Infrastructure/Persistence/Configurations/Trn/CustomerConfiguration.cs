// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("TRN_Customers", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Customers_PaymentTermsDays", "[PaymentTermsDays] >= 0");
            t.HasCheckConstraint("CK_TRN_Customers_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_TRN_Customers_CustomValues", "ISJSON([CustomValues]) = 1");
            t.HasCheckConstraint("CK_TRN_Customers_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Customers");

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
        builder.Property(e => e.RegistrationNo)
            .HasColumnName("RegistrationNo")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.Phone)
            .HasColumnName("Phone")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.Email)
            .HasColumnName("Email")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.Address)
            .HasColumnName("Address")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.City)
            .HasColumnName("City")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(10)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.ContactPerson)
            .HasColumnName("ContactPerson")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.PaymentTermsDays)
            .HasColumnName("PaymentTermsDays")
            .HasColumnType("int")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ApArPartyId)
            .HasColumnName("ApArPartyId")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(14)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(15)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(false);
        builder.Property(e => e.CustomValues)
            .HasColumnName("CustomValues")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(16)
            .IsUnicode(true)
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

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Customers_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Name" }, "UX_TRN_Customers_CompanyId_Name")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Active'");
        builder.HasIndex(new[] { "ApArPartyId" }, "IX_TRN_Customers_ApArPartyId");

        builder.HasOne(e => e.ApArParty)
            .WithMany()
            .HasForeignKey(e => e.ApArPartyId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Customers_ApArParties_ApArPartyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Customers_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Customers_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Customers_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Customers_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Customers_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
