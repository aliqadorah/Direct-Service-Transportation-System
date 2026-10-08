// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class UserCompanyRoleConfiguration : IEntityTypeConfiguration<UserCompanyRole>
{
    public void Configure(EntityTypeBuilder<UserCompanyRole> builder)
    {
        builder.ToTable("UserCompanyRoles", "core", t =>
        {
            t.HasCheckConstraint("CK_UserCompanyRoles_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_UserCompanyRoles");

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
        builder.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasColumnType("bigint")
            .HasColumnOrder(3)
            .IsRequired(true);
        builder.Property(e => e.CompanyId)
            .HasColumnName("CompanyId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.RoleId)
            .HasColumnName("RoleId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(6)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(13)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(14)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_UserCompanyRoles_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "UserId", "CompanyId" }, "UX_UserCompanyRoles_UserId_CompanyId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId" }, "IX_UserCompanyRoles_CompanyId");
        builder.HasIndex(new[] { "RoleId" }, "IX_UserCompanyRoles_RoleId");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_UserCompanyRoles_Users_UserId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_UserCompanyRoles_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_UserCompanyRoles_Roles_RoleId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_UserCompanyRoles_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_UserCompanyRoles_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_UserCompanyRoles_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_UserCompanyRoles_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
