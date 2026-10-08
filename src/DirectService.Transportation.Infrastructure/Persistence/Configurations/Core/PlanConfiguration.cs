// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans", "core", t =>
        {
            t.HasCheckConstraint("CK_Plans_Price", "[Price] >= 0");
            t.HasCheckConstraint("CK_Plans_BillingCycle", "[BillingCycle] IN ('Monthly','Annual')");
            t.HasCheckConstraint("CK_Plans_MaxCompanies", "[MaxCompanies] > 0");
            t.HasCheckConstraint("CK_Plans_MaxActiveUsers", "[MaxActiveUsers] > 0");
            t.HasCheckConstraint("CK_Plans_MaxTripsPerMonth", "[MaxTripsPerMonth] > 0");
            t.HasCheckConstraint("CK_Plans_MaxStorageMb", "[MaxStorageMb] > 0");
            t.HasCheckConstraint("CK_Plans_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Plans");

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
        builder.Property(e => e.Code)
            .HasColumnName("Code")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(2)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.NameAr)
            .HasColumnName("NameAr")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(3)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(e => e.NameEn)
            .HasColumnName("NameEn")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(e => e.Price)
            .HasColumnName("Price")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(5)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.BillingCycle)
            .HasColumnName("BillingCycle")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Monthly'")
            .ValueGeneratedNever();
        builder.Property(e => e.MaxCompanies)
            .HasColumnName("MaxCompanies")
            .HasColumnType("int")
            .HasColumnOrder(7)
            .IsRequired(true);
        builder.Property(e => e.MaxActiveUsers)
            .HasColumnName("MaxActiveUsers")
            .HasColumnType("int")
            .HasColumnOrder(8)
            .IsRequired(true);
        builder.Property(e => e.MaxTripsPerMonth)
            .HasColumnName("MaxTripsPerMonth")
            .HasColumnType("int")
            .HasColumnOrder(9)
            .IsRequired(true);
        builder.Property(e => e.MaxStorageMb)
            .HasColumnName("MaxStorageMb")
            .HasColumnType("int")
            .HasColumnOrder(10)
            .IsRequired(true);
        builder.Property(e => e.IsActive)
            .HasColumnName("IsActive")
            .HasColumnType("bit")
            .HasColumnOrder(11)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(16)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(19)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(20)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_Plans_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "Code" }, "UX_Plans_Code")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Plans_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Plans_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Plans_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
