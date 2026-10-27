// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("TRN_Trips", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Trips_FleetClass", "[FleetClass] IN ('Own','External')");
            t.HasCheckConstraint("CK_TRN_Trips_LoadedWeight", "[LoadedWeight] > 0");
            t.HasCheckConstraint("CK_TRN_Trips_UnloadedWeight", "[UnloadedWeight] >= 0");
            t.HasCheckConstraint("CK_TRN_Trips_CustomerPrice", "[CustomerPrice] >= 0");
            t.HasCheckConstraint("CK_TRN_Trips_DriverRate", "[DriverRate] >= 0");
            t.HasCheckConstraint("CK_TRN_Trips_RateSource", "[RateSource] IN ('Monthly','Manual')");
            t.HasCheckConstraint("CK_TRN_Trips_Status", "[Status] IN ('Active','Void')");
            t.HasCheckConstraint("CK_TRN_Trips_CustomValues", "ISJSON([CustomValues]) = 1");
            t.HasCheckConstraint("CK_TRN_Trips_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Trips_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Trips_UnloadingDate", "[UnloadingDate] >= [LoadingDate]");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Trips");

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
        builder.Property(e => e.ReceiptNo)
            .HasColumnName("ReceiptNo")
            .HasColumnType("nvarchar(30)")
            .HasColumnOrder(4)
            .IsUnicode(true)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.ReceiptNoKey)
            .HasColumnName("ReceiptNoKey")
            .HasColumnType("nvarchar(30)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.TripDate)
            .HasColumnName("TripDate")
            .HasColumnType("date")
            .HasColumnOrder(6)
            .IsRequired(true);
        builder.Property(e => e.LoadingDate)
            .HasColumnName("LoadingDate")
            .HasColumnType("date")
            .HasColumnOrder(7)
            .IsRequired(true);
        builder.Property(e => e.UnloadingDate)
            .HasColumnName("UnloadingDate")
            .HasColumnType("date")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.DriverId)
            .HasColumnName("DriverId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(true);
        builder.Property(e => e.CustomerId)
            .HasColumnName("CustomerId")
            .HasColumnType("bigint")
            .HasColumnOrder(10)
            .IsRequired(true);
        builder.Property(e => e.LoadingSiteId)
            .HasColumnName("LoadingSiteId")
            .HasColumnType("bigint")
            .HasColumnOrder(11)
            .IsRequired(true);
        builder.Property(e => e.UnloadingSiteId)
            .HasColumnName("UnloadingSiteId")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(true);
        builder.Property(e => e.MaterialId)
            .HasColumnName("MaterialId")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(true);
        builder.Property(e => e.PartnerId)
            .HasColumnName("PartnerId")
            .HasColumnType("bigint")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.TruckPlateAtSave)
            .HasColumnName("TruckPlateAtSave")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(15)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(true);
        builder.Property(e => e.FleetClass)
            .HasColumnName("FleetClass")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(16)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.LoadedWeight)
            .HasColumnName("LoadedWeight")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(17)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.UnloadedWeight)
            .HasColumnName("UnloadedWeight")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(18)
            .HasPrecision(18, 3)
            .IsRequired(false);
        builder.Property(e => e.BillingQty)
            .HasColumnName("BillingQty")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(19)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.WageQty)
            .HasColumnName("WageQty")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(20)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.CustomerPrice)
            .HasColumnName("CustomerPrice")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(21)
            .HasPrecision(18, 4)
            .IsRequired(true);
        builder.Property(e => e.TariffPrice)
            .HasColumnName("TariffPrice")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(22)
            .HasPrecision(18, 4)
            .IsRequired(false);
        builder.Property(e => e.DriverRate)
            .HasColumnName("DriverRate")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(23)
            .HasPrecision(18, 4)
            .IsRequired(true);
        builder.Property(e => e.RateSource)
            .HasColumnName("RateSource")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(24)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.RateMonth)
            .HasColumnName("RateMonth")
            .HasColumnType("char(7)")
            .HasColumnOrder(25)
            .IsUnicode(false)
            .HasMaxLength(7)
            .IsFixedLength()
            .IsRequired(true);
        builder.Property(e => e.PartnerRate)
            .HasColumnName("PartnerRate")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(26)
            .HasPrecision(18, 4)
            .IsRequired(false);
        builder.Property(e => e.Revenue)
            .HasColumnName("Revenue")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(27)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.DriverWage)
            .HasColumnName("DriverWage")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(28)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.PartnerCost)
            .HasColumnName("PartnerCost")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(29)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ShortageWeight)
            .HasColumnName("ShortageWeight")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(30)
            .HasPrecision(18, 3)
            .IsRequired(false);
        builder.Property(e => e.ShortagePct)
            .HasColumnName("ShortagePct")
            .HasColumnType("decimal(9,4)")
            .HasColumnOrder(31)
            .HasPrecision(9, 4)
            .IsRequired(false);
        builder.Property(e => e.IsShortageFlagged)
            .HasColumnName("IsShortageFlagged")
            .HasColumnType("bit")
            .HasColumnOrder(32)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.IsTariffBreach)
            .HasColumnName("IsTariffBreach")
            .HasColumnType("bit")
            .HasColumnOrder(33)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.IsManualRate)
            .HasColumnName("IsManualRate")
            .HasColumnType("bit")
            .HasColumnOrder(34)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.NeedsReview)
            .HasColumnName("NeedsReview")
            .HasColumnType("bit")
            .HasColumnOrder(35)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ReviewNote)
            .HasColumnName("ReviewNote")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(36)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(37)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.InvoiceId)
            .HasColumnName("InvoiceId")
            .HasColumnType("bigint")
            .HasColumnOrder(38)
            .IsRequired(false);
        builder.Property(e => e.SettlementId)
            .HasColumnName("SettlementId")
            .HasColumnType("bigint")
            .HasColumnOrder(39)
            .IsRequired(false);
        builder.Property(e => e.JournalEntryId)
            .HasColumnName("JournalEntryId")
            .HasColumnType("bigint")
            .HasColumnOrder(40)
            .IsRequired(false);
        builder.Property(e => e.VoidReason)
            .HasColumnName("VoidReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(41)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.VoidedBy)
            .HasColumnName("VoidedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(42)
            .IsRequired(false);
        builder.Property(e => e.VoidedAt)
            .HasColumnName("VoidedAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(43)
            .IsRequired(false);
        builder.Property(e => e.ImportBatchId)
            .HasColumnName("ImportBatchId")
            .HasColumnType("bigint")
            .HasColumnOrder(44)
            .IsRequired(false);
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(45)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(false);
        builder.Property(e => e.CustomValues)
            .HasColumnName("CustomValues")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(46)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(47)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(48)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(49)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(50)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(51)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(52)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(53)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(54)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(55)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Trips_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "ReceiptNoKey" }, "UX_TRN_Trips_CompanyId_ReceiptNoKey")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        // DT-206: receipt-number search (LIKE '%…%') over a narrow binary-collation index per company.
        builder.Property(e => e.ReceiptNoSearch)
            .HasColumnName("ReceiptNoSearch")
            .HasColumnType("nvarchar(30)")
            .IsUnicode(true)
            .HasMaxLength(30)
            .HasComputedColumnSql("[ReceiptNoKey] COLLATE Latin1_General_100_BIN2", stored: true);
        builder.HasIndex(new[] { "CompanyId", "ReceiptNoSearch" }, "IX_TRN_Trips_ReceiptNoSearch")
            .IncludeProperties("Status")
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "TripDate" }, "IX_TRN_Trips_Date")
            .IncludeProperties("Status");
        builder.HasIndex(new[] { "CompanyId", "CustomerId", "Status" }, "IX_TRN_Trips_Unbilled")
            .HasFilter("[InvoiceId] IS NULL");
        builder.HasIndex(new[] { "CompanyId", "DriverId", "Status" }, "IX_TRN_Trips_Unsettled")
            .HasFilter("[SettlementId] IS NULL");
        // DT-271: partner trips in payment order (trip date, receipt number) for the running cost of trn.TRN_TripPartnerCoverage.
        builder.HasIndex(new[] { "TenantId", "CompanyId", "PartnerId", "TripDate", "ReceiptNoKey", "Id" }, "IX_TRN_Trips_PartnerOrder")
            .IncludeProperties("PartnerCost")
            .HasFilter("[PartnerId] IS NOT NULL AND [Status] = 'Active' AND [IsDeleted] = 0");
        builder.HasIndex(new[] { "DriverId" }, "IX_TRN_Trips_DriverId");
        builder.HasIndex(new[] { "CustomerId" }, "IX_TRN_Trips_CustomerId");
        builder.HasIndex(new[] { "LoadingSiteId" }, "IX_TRN_Trips_LoadingSiteId");
        builder.HasIndex(new[] { "UnloadingSiteId" }, "IX_TRN_Trips_UnloadingSiteId");
        builder.HasIndex(new[] { "MaterialId" }, "IX_TRN_Trips_MaterialId");
        builder.HasIndex(new[] { "PartnerId" }, "IX_TRN_Trips_PartnerId");
        builder.HasIndex(new[] { "InvoiceId" }, "IX_TRN_Trips_InvoiceId");
        builder.HasIndex(new[] { "SettlementId" }, "IX_TRN_Trips_SettlementId");
        builder.HasIndex(new[] { "JournalEntryId" }, "IX_TRN_Trips_JournalEntryId");
        builder.HasIndex(new[] { "VoidedBy" }, "IX_TRN_Trips_VoidedBy");
        builder.HasIndex(new[] { "ImportBatchId" }, "IX_TRN_Trips_ImportBatchId");

        builder.HasOne(e => e.Driver)
            .WithMany()
            .HasForeignKey(e => e.DriverId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Trips_TRN_Drivers_DriverId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Trips_TRN_Customers_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.LoadingSite)
            .WithMany()
            .HasForeignKey(e => e.LoadingSiteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Trips_TRN_LoadingSites_LoadingSiteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.UnloadingSite)
            .WithMany()
            .HasForeignKey(e => e.UnloadingSiteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Trips_TRN_UnloadingSites_UnloadingSiteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Material)
            .WithMany()
            .HasForeignKey(e => e.MaterialId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_TRN_Trips_TRN_Materials_MaterialId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Partner)
            .WithMany()
            .HasForeignKey(e => e.PartnerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_TRN_Partners_PartnerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Invoice)
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_TRN_Invoices_InvoiceId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Settlement)
            .WithMany()
            .HasForeignKey(e => e.SettlementId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_TRN_Settlements_SettlementId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.JournalEntry)
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_JournalEntries_JournalEntryId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.VoidedByUser)
            .WithMany()
            .HasForeignKey(e => e.VoidedBy)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_Users_VoidedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ImportBatch)
            .WithMany()
            .HasForeignKey(e => e.ImportBatchId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Trips_ImportBatches_ImportBatchId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Trips_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Trips_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Trips_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Trips_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Trips_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
