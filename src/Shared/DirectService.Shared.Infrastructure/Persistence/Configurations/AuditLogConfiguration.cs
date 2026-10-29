// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 8; the foreign keys to core.Tenants / Companies / Users are in Transportation.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", "core", t =>
        {
            t.HasCheckConstraint("CK_AuditLogs_OldValues", "ISJSON([OldValues]) = 1");
            t.HasCheckConstraint("CK_AuditLogs_NewValues", "ISJSON([NewValues]) = 1");
        });

        builder.HasKey(e => e.Id).HasName("PK_AuditLogs");

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("bigint")
            .HasColumnOrder(0)
            .IsRequired(true)
            .UseIdentityColumn();
        builder.Property(e => e.TenantId)
            .HasColumnName("TenantId")
            .HasColumnType("bigint")
            .HasColumnOrder(1)
            .IsRequired(false);
        builder.Property(e => e.CompanyId)
            .HasColumnName("CompanyId")
            .HasColumnType("bigint")
            .HasColumnOrder(2)
            .IsRequired(false);
        builder.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasColumnType("bigint")
            .HasColumnOrder(3)
            .IsRequired(false);
        builder.Property(e => e.OccurredAt)
            .HasColumnName("OccurredAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(4)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.Action)
            .HasColumnName("Action")
            .HasColumnType("varchar(50)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(50)
            .IsRequired(true);
        builder.Property(e => e.EntityType)
            .HasColumnName("EntityType")
            .HasColumnType("varchar(60)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(60)
            .IsRequired(false);
        builder.Property(e => e.EntityId)
            .HasColumnName("EntityId")
            .HasColumnType("bigint")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.OldValues)
            .HasColumnName("OldValues")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.NewValues)
            .HasColumnName("NewValues")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.IpAddress)
            .HasColumnName("IpAddress")
            .HasColumnType("varchar(45)")
            .HasColumnOrder(10)
            .IsUnicode(false)
            .HasMaxLength(45)
            .IsRequired(false);

        builder.HasIndex(new[] { "CompanyId", "EntityType", "EntityId", "OccurredAt" }, "IX_AuditLogs_Entity");
        builder.HasIndex(new[] { "UserId", "OccurredAt" }, "IX_AuditLogs_User");
        builder.HasIndex(new[] { "TenantId" }, "IX_AuditLogs_TenantId");
    }
}
