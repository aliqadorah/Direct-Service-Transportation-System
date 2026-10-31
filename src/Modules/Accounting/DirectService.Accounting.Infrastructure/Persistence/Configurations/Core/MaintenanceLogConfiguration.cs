// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Core;


namespace DirectService.Accounting.Infrastructure.Persistence.Configurations.Core;

internal sealed class MaintenanceLogConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> builder)
    {
        builder.ToTable("MaintenanceLogs", "core", t =>
        {
            t.HasCheckConstraint("CK_MaintenanceLogs_RecordCounts", "ISJSON([RecordCounts]) = 1");
        });

        builder.HasKey(e => e.Id).HasName("PK_MaintenanceLogs");

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
            .IsRequired(true);
        builder.Property(e => e.CompanyId)
            .HasColumnName("CompanyId")
            .HasColumnType("bigint")
            .HasColumnOrder(2)
            .IsRequired(true);
        builder.Property(e => e.UserId)
            .HasColumnName("UserId")
            .HasColumnType("bigint")
            .HasColumnOrder(3)
            .IsRequired(true);
        builder.Property(e => e.Action)
            .HasColumnName("Action")
            .HasColumnType("varchar(40)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(40)
            .IsRequired(true);
        builder.Property(e => e.RecordCounts)
            .HasColumnName("RecordCounts")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .IsRequired(true);
        builder.Property(e => e.OccurredAt)
            .HasColumnName("OccurredAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(6)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();

        builder.HasIndex(new[] { "TenantId" }, "IX_MaintenanceLogs_TenantId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_MaintenanceLogs_CompanyId");
        builder.HasIndex(new[] { "UserId" }, "IX_MaintenanceLogs_UserId");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_MaintenanceLogs_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_MaintenanceLogs_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_MaintenanceLogs_Users_UserId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
