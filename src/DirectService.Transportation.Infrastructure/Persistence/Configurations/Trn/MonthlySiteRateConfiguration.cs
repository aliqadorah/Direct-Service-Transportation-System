// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class MonthlySiteRateConfiguration : IEntityTypeConfiguration<MonthlySiteRate>
{
    public void Configure(EntityTypeBuilder<MonthlySiteRate> builder)
    {
        builder.ToTable("TRN_MonthlySiteRates", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_MonthlySiteRates_OwnFleetRate", "[OwnFleetRate] > 0");
            t.HasCheckConstraint("CK_TRN_MonthlySiteRates_ExternalFleetRate", "[ExternalFleetRate] > 0");
            t.HasCheckConstraint("CK_TRN_MonthlySiteRates_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_MonthlySiteRates_RateMonth", "[RateMonth] LIKE '[12][0-9][0-9][0-9]-[01][0-9]'");
            t.HasCheckConstraint("CK_TRN_MonthlySiteRates_OwnFleetRate_ExternalFleetRate", "[OwnFleetRate] IS NOT NULL OR [ExternalFleetRate] IS NOT NULL");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_MonthlySiteRates");

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
        builder.Property(e => e.RateMonth)
            .HasColumnName("RateMonth")
            .HasColumnType("char(7)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(7)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.LoadingSiteId)
            .HasColumnName("LoadingSiteId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.OwnFleetRate)
            .HasColumnName("OwnFleetRate")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(6)
            .HasPrecision(18, 4)
            .IsRequired(false);
        builder.Property(e => e.ExternalFleetRate)
            .HasColumnName("ExternalFleetRate")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(7)
            .HasPrecision(18, 4)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(8)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(15)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(16)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_MonthlySiteRates_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "RateMonth", "LoadingSiteId" }, "UX_TRN_MonthlySiteRates_CompanyId_RateMonth_LoadingSiteId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "LoadingSiteId" }, "IX_TRN_MonthlySiteRates_LoadingSiteId");

        builder.HasOne(e => e.LoadingSite)
            .WithMany()
            .HasForeignKey(e => e.LoadingSiteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_MonthlySiteRates_TRN_LoadingSites_LoadingSiteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_MonthlySiteRates_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_MonthlySiteRates_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_MonthlySiteRates_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_MonthlySiteRates_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_MonthlySiteRates_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
