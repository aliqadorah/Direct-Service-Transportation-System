// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", "core", t =>
        {
            t.HasCheckConstraint("CK_Tenants_Status", "[Status] IN ('Trial','Active','PastDue','Suspended','Cancelled','Deleted')");
            t.HasCheckConstraint("CK_Tenants_BillingCycle", "[BillingCycle] IN ('Monthly','Annual')");
            t.HasCheckConstraint("CK_Tenants_PasswordMinLength", "[PasswordMinLength] BETWEEN 8 AND 64");
            t.HasCheckConstraint("CK_Tenants_SessionIdleTimeoutMinutes", "[SessionIdleTimeoutMinutes] BETWEEN 5 AND 1440");
            t.HasCheckConstraint("CK_Tenants_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Tenants");

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
        builder.Property(e => e.Name)
            .HasColumnName("Name")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(2)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(15)")
            .HasColumnOrder(3)
            .IsUnicode(false)
            .HasMaxLength(15)
            .IsRequired(true)
            .HasDefaultValueSql("'Trial'")
            .ValueGeneratedNever();
        builder.Property(e => e.PlanId)
            .HasColumnName("PlanId")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.BillingCycle)
            .HasColumnName("BillingCycle")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Monthly'")
            .ValueGeneratedNever();
        builder.Property(e => e.TrialEndsAt)
            .HasColumnName("TrialEndsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.CurrentPeriodEnd)
            .HasColumnName("CurrentPeriodEnd")
            .HasColumnType("datetime2")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.GraceEndsAt)
            .HasColumnName("GraceEndsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.CancelledAt)
            .HasColumnName("CancelledAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.RetentionEndsAt)
            .HasColumnName("RetentionEndsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.PasswordMinLength)
            .HasColumnName("PasswordMinLength")
            .HasColumnType("tinyint")
            .HasColumnOrder(11)
            .IsRequired(true)
            .HasDefaultValueSql("8")
            .ValueGeneratedNever();
        builder.Property(e => e.PasswordRequireLetter)
            .HasColumnName("PasswordRequireLetter")
            .HasColumnType("bit")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.PasswordRequireDigit)
            .HasColumnName("PasswordRequireDigit")
            .HasColumnType("bit")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.SessionIdleTimeoutMinutes)
            .HasColumnName("SessionIdleTimeoutMinutes")
            .HasColumnType("smallint")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("30")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(15)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(19)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(22)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(23)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_Tenants_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "PlanId" }, "IX_Tenants_PlanId");

        builder.HasOne(e => e.Plan)
            .WithMany()
            .HasForeignKey(e => e.PlanId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_Tenants_Plans_PlanId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Tenants_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Tenants_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Tenants_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
