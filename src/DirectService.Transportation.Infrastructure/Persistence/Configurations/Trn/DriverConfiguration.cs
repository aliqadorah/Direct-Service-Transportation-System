// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("TRN_Drivers", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Drivers_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
            t.HasCheckConstraint("CK_TRN_Drivers_CapacityTons", "[CapacityTons] > 0");
            t.HasCheckConstraint("CK_TRN_Drivers_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_TRN_Drivers_CustomValues", "ISJSON([CustomValues]) = 1");
            t.HasCheckConstraint("CK_TRN_Drivers_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Drivers");

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
        builder.Property(e => e.FullName)
            .HasColumnName("FullName")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(true);
        builder.Property(e => e.NationalId)
            .HasColumnName("NationalId")
            .HasColumnType("char(10)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.Phone1)
            .HasColumnName("Phone1")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(true);
        builder.Property(e => e.Phone2)
            .HasColumnName("Phone2")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.Address)
            .HasColumnName("Address")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.LicenceNo)
            .HasColumnName("LicenceNo")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(9)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.LicenceType)
            .HasColumnName("LicenceType")
            .HasColumnType("nvarchar(30)")
            .HasColumnOrder(10)
            .IsUnicode(true)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.LicenceExpiry)
            .HasColumnName("LicenceExpiry")
            .HasColumnType("date")
            .HasColumnOrder(11)
            .IsRequired(true);
        builder.Property(e => e.PaymentMethod)
            .HasColumnName("PaymentMethod")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(12)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Cash'")
            .ValueGeneratedNever();
        builder.Property(e => e.BankName)
            .HasColumnName("BankName")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(13)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.Iban)
            .HasColumnName("Iban")
            .HasColumnType("varchar(34)")
            .HasColumnOrder(14)
            .IsUnicode(false)
            .HasMaxLength(34)
            .IsRequired(false);
        builder.Property(e => e.CliqAlias)
            .HasColumnName("CliqAlias")
            .HasColumnType("nvarchar(50)")
            .HasColumnOrder(15)
            .IsUnicode(true)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.PlateNumber)
            .HasColumnName("PlateNumber")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(16)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(true);
        builder.Property(e => e.TruckType)
            .HasColumnName("TruckType")
            .HasColumnType("nvarchar(50)")
            .HasColumnOrder(17)
            .IsUnicode(true)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.TrailerNo)
            .HasColumnName("TrailerNo")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(18)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.CapacityTons)
            .HasColumnName("CapacityTons")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(19)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.OwnershipTypeId)
            .HasColumnName("OwnershipTypeId")
            .HasColumnType("bigint")
            .HasColumnOrder(20)
            .IsRequired(true);
        builder.Property(e => e.PartnerId)
            .HasColumnName("PartnerId")
            .HasColumnType("bigint")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.RegistrationExpiry)
            .HasColumnName("RegistrationExpiry")
            .HasColumnType("date")
            .HasColumnOrder(22)
            .IsRequired(true);
        builder.Property(e => e.InsuranceExpiry)
            .HasColumnName("InsuranceExpiry")
            .HasColumnType("date")
            .HasColumnOrder(23)
            .IsRequired(true);
        builder.Property(e => e.InspectionExpiry)
            .HasColumnName("InspectionExpiry")
            .HasColumnType("date")
            .HasColumnOrder(24)
            .IsRequired(true);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(25)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(26)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(false);
        builder.Property(e => e.CustomValues)
            .HasColumnName("CustomValues")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(27)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(28)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(29)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(30)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(31)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(32)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(33)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(34)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(35)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(36)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Drivers_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "NationalId" }, "UX_TRN_Drivers_CompanyId_NationalId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "PlateNumber" }, "UX_TRN_Drivers_CompanyId_PlateNumber")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "LicenceExpiry", "RegistrationExpiry", "InsuranceExpiry", "InspectionExpiry" }, "IX_TRN_Drivers_Expiry")
            .HasFilter("[Status] = 'Active'");
        builder.HasIndex(new[] { "OwnershipTypeId" }, "IX_TRN_Drivers_OwnershipTypeId");
        builder.HasIndex(new[] { "PartnerId" }, "IX_TRN_Drivers_PartnerId");

        builder.HasOne(e => e.OwnershipType)
            .WithMany()
            .HasForeignKey(e => e.OwnershipTypeId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Drivers_TRN_OwnershipTypes_OwnershipTypeId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Partner)
            .WithMany()
            .HasForeignKey(e => e.PartnerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Drivers_TRN_Partners_PartnerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Drivers_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Drivers_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Drivers_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Drivers_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Drivers_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
