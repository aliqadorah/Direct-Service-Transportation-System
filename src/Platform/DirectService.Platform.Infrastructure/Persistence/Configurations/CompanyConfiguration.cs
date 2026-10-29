// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;


namespace DirectService.Platform.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies", "core", t =>
        {
            t.HasCheckConstraint("CK_Companies_CurrencyDecimals", "[CurrencyDecimals] BETWEEN 0 AND 3");
            t.HasCheckConstraint("CK_Companies_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_Companies_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_Companies_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Companies");

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
        builder.Property(e => e.Name)
            .HasColumnName("Name")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(3)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.NameEn)
            .HasColumnName("NameEn")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(false);
        builder.Property(e => e.TaxNumber)
            .HasColumnName("TaxNumber")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.CommercialRegNo)
            .HasColumnName("CommercialRegNo")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.Address)
            .HasColumnName("Address")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.Phone)
            .HasColumnName("Phone")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.Email)
            .HasColumnName("Email")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.LogoFile)
            .HasColumnName("LogoFile")
            .HasColumnType("varbinary(max)")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.CountryCode)
            .HasColumnName("CountryCode")
            .HasColumnType("char(2)")
            .HasColumnOrder(11)
            .IsUnicode(false)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired(true)
            .HasDefaultValueSql("'JO'")
            .ValueGeneratedNever();
        builder.Property(e => e.CurrencyCode)
            .HasColumnName("CurrencyCode")
            .HasColumnType("char(3)")
            .HasColumnOrder(12)
            .IsUnicode(false)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired(true)
            .HasDefaultValueSql("'JOD'")
            .ValueGeneratedNever();
        builder.Property(e => e.CurrencyDecimals)
            .HasColumnName("CurrencyDecimals")
            .HasColumnType("tinyint")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("3")
            .ValueGeneratedNever();
        builder.Property(e => e.TaxRate)
            .HasColumnName("TaxRate")
            .HasColumnType("decimal(5,2)")
            .HasColumnOrder(14)
            .HasPrecision(5, 2)
            .IsRequired(true)
            .HasDefaultValueSql("16.00")
            .ValueGeneratedNever();
        builder.Property(e => e.TaxInclusive)
            .HasColumnName("TaxInclusive")
            .HasColumnType("bit")
            .HasColumnOrder(15)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.TimeZone)
            .HasColumnName("TimeZone")
            .HasColumnType("varchar(50)")
            .HasColumnOrder(16)
            .IsUnicode(false)
            .HasMaxLength(50)
            .IsRequired(true)
            .HasDefaultValueSql("'Asia/Amman'")
            .ValueGeneratedNever();
        builder.Property(e => e.MigrationCompletedAt)
            .HasColumnName("MigrationCompletedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(18)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(19)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(22)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(23)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(24)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(25)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(26)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(27)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_Companies_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "TenantId", "Name" }, "UX_Companies_TenantId_Name")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_Companies_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Companies_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Companies_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Companies_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
