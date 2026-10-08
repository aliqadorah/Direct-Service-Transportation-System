// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class SupportAccessGrantConfiguration : IEntityTypeConfiguration<SupportAccessGrant>
{
    public void Configure(EntityTypeBuilder<SupportAccessGrant> builder)
    {
        builder.ToTable("SupportAccessGrants", "core", t =>
        {
            t.HasCheckConstraint("CK_SupportAccessGrants_Status", "[Status] IN ('Requested','Approved','Rejected','Expired')");
            t.HasCheckConstraint("CK_SupportAccessGrants_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_SupportAccessGrants_EndsAt", "[EndsAt] > [StartsAt]");
        });

        builder.HasKey(e => e.Id).HasName("PK_SupportAccessGrants");

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
        builder.Property(e => e.RequestedBy)
            .HasColumnName("RequestedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(3)
            .IsRequired(true);
        builder.Property(e => e.ApprovedBy)
            .HasColumnName("ApprovedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(4)
            .IsRequired(false);
        builder.Property(e => e.Reason)
            .HasColumnName("Reason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(true);
        builder.Property(e => e.StartsAt)
            .HasColumnName("StartsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.EndsAt)
            .HasColumnName("EndsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Requested'")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(9)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(16)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(17)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_SupportAccessGrants_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "RequestedBy" }, "IX_SupportAccessGrants_RequestedBy");
        builder.HasIndex(new[] { "ApprovedBy" }, "IX_SupportAccessGrants_ApprovedBy");

        builder.HasOne(e => e.RequestedByUser)
            .WithMany()
            .HasForeignKey(e => e.RequestedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_SupportAccessGrants_Users_RequestedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ApprovedByUser)
            .WithMany()
            .HasForeignKey(e => e.ApprovedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_SupportAccessGrants_Users_ApprovedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_SupportAccessGrants_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_SupportAccessGrants_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_SupportAccessGrants_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_SupportAccessGrants_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
