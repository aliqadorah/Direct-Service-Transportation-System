// Revision 6 (owner findings 27 Oct 2026, DT-271): docs/design/Database_Design_Revision6.md §B13. A view — created by the migration
// Rev6_PartnerTripCoverage, never by EF.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class PartnerPaymentBoundaryConfiguration : IEntityTypeConfiguration<PartnerPaymentBoundary>
{
    public void Configure(EntityTypeBuilder<PartnerPaymentBoundary> builder)
    {
        builder.ToView("TRN_PartnerPaymentBoundary", "trn");
        builder.HasKey(e => e.PartnerId);
        builder.Property(e => e.PartnerId).HasColumnName("PartnerId").HasColumnType("bigint").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint");
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint");
        builder.Property(e => e.Paid).HasColumnName("Paid").HasColumnType("decimal(18,3)").HasPrecision(18, 3);
        builder.Property(e => e.BoundaryDate).HasColumnName("BoundaryDate").HasColumnType("date");
        builder.Property(e => e.CostBefore).HasColumnName("CostBefore").HasColumnType("decimal(18,3)").HasPrecision(18, 3);
    }
}
