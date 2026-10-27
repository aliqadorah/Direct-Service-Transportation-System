// Revision 6 (owner findings 27 Oct 2026, DT-271): docs/design/Database_Design_Revision6.md §B13. A view — created by the migration
// Rev6_PartnerTripCoverage, never by EF.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class TripPartnerCoverageConfiguration : IEntityTypeConfiguration<TripPartnerCoverage>
{
    public void Configure(EntityTypeBuilder<TripPartnerCoverage> builder)
    {
        builder.ToView("TRN_TripPartnerCoverage", "trn");
        builder.HasKey(e => e.TripId);
        builder.Property(e => e.TripId).HasColumnName("TripId").HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint");
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint");
        builder.Property(e => e.PartnerId).HasColumnName("PartnerId").HasColumnType("bigint");
        builder.Property(e => e.PartnerCost).HasColumnName("PartnerCost").HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        builder.Property(e => e.Paid).HasColumnName("Paid").HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        builder.Property(e => e.OpenAmount).HasColumnName("OpenAmount").HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        builder.HasOne(e => e.Trip)
            .WithOne(t => t.PartnerCoverage)
            .HasForeignKey<TripPartnerCoverage>(e => e.TripId);
    }
}
