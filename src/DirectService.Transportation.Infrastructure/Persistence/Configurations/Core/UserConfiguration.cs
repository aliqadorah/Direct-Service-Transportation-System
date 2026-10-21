// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "core", t =>
        {
            t.HasCheckConstraint("CK_Users_LanguageCode", "[LanguageCode] IN ('ar','en')");
            t.HasCheckConstraint("CK_Users_Status", "[Status] IN ('Active','Disabled','Archived')");
            t.HasCheckConstraint("CK_Users_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_Users_Email_UserName", "[Email] IS NOT NULL OR [UserName] IS NOT NULL");
            t.HasCheckConstraint("CK_Users_PendingEmail_PendingEmailAt", "([PendingEmail] IS NULL AND [PendingEmailAt] IS NULL) OR ([PendingEmail] IS NOT NULL AND [PendingEmailAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_Users_IsPlatformStaff_TenantId", "([IsPlatformStaff] = 1 AND [TenantId] IS NULL) OR ([IsPlatformStaff] = 0 AND [TenantId] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Users");

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
            .IsRequired(false);
        builder.Property(e => e.FullName)
            .HasColumnName("FullName")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(3)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(true);
        builder.Property(e => e.Email)
            .HasColumnName("Email")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.NormalizedEmail)
            .HasColumnName("NormalizedEmail")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.EmailConfirmedAt)
            .HasColumnName("EmailConfirmedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.PasswordHash)
            .HasColumnName("PasswordHash")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(true);
        builder.Property(e => e.SecurityStamp)
            .HasColumnName("SecurityStamp")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(e => e.IsPlatformStaff)
            .HasColumnName("IsPlatformStaff")
            .HasColumnType("bit")
            .HasColumnOrder(9)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.TwoFactorSecret)
            .HasColumnName("TwoFactorSecret")
            .HasColumnType("varbinary(256)")
            .HasColumnOrder(10)
            .HasMaxLength(256)
            .IsRequired(false);
        builder.Property(e => e.TwoFactorEnabledAt)
            .HasColumnName("TwoFactorEnabledAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.LanguageCode)
            .HasColumnName("LanguageCode")
            .HasColumnType("char(2)")
            .HasColumnOrder(12)
            .IsUnicode(false)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired(true)
            .HasDefaultValueSql("'ar'")
            .ValueGeneratedNever();
        builder.Property(e => e.DefaultCompanyId)
            .HasColumnName("DefaultCompanyId")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.DateFormat)
            .HasColumnName("DateFormat")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(14)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(true)
            .HasDefaultValueSql("'dd/MM/yyyy'")
            .ValueGeneratedNever();
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(15)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.FailedSignInCount)
            .HasColumnName("FailedSignInCount")
            .HasColumnType("int")
            .HasColumnOrder(16)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.LockedUntil)
            .HasColumnName("LockedUntil")
            .HasColumnType("datetime2")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.TwoFactorLastStep)   // DT-212
            .HasColumnName("TwoFactorLastStep")
            .HasColumnType("bigint")
            .HasColumnOrder(31)
            .IsRequired(false);
        builder.Property(e => e.LastSignInAt)
            .HasColumnName("LastSignInAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(18)
            .IsRequired(false);
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
        // Revision 6: username sign-in and forced password change (owner change request 15 Oct 2026).
        builder.Property(e => e.UserName)
            .HasColumnName("UserName")
            .HasColumnType("nvarchar(50)")
            .HasColumnOrder(28)
            .IsUnicode(true)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.NormalizedUserName)
            .HasColumnName("NormalizedUserName")
            .HasColumnType("nvarchar(50)")
            .HasColumnOrder(29)
            .IsUnicode(true)
            .HasMaxLength(50)
            .IsRequired(false);
        builder.Property(e => e.MustChangePassword)
            .HasColumnName("MustChangePassword")
            .HasColumnType("bit")
            .HasColumnOrder(30)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        // Revision 6 (DT-229): an email change waiting for confirmation.
        builder.Property(e => e.PendingEmail)
            .HasColumnName("PendingEmail")
            .HasColumnType("nvarchar(150)")
            .HasColumnOrder(32)
            .IsUnicode(true)
            .HasMaxLength(150)
            .IsRequired(false);
        builder.Property(e => e.PendingEmailAt)
            .HasColumnName("PendingEmailAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(33)
            .IsRequired(false);

        builder.HasIndex(new[] { "PublicId" }, "UX_Users_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "Email" }, "UX_Users_Email")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Email] IS NOT NULL");
        builder.HasIndex(new[] { "NormalizedEmail" }, "UX_Users_NormalizedEmail")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [NormalizedEmail] IS NOT NULL");
        builder.HasIndex(new[] { "NormalizedUserName" }, "UX_Users_NormalizedUserName")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [NormalizedUserName] IS NOT NULL");
        builder.HasIndex(new[] { "DefaultCompanyId" }, "IX_Users_DefaultCompanyId");

        builder.HasOne(e => e.DefaultCompany)
            .WithMany()
            .HasForeignKey(e => e.DefaultCompanyId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_Users_Companies_DefaultCompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_Users_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Users_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Users_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Users_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
