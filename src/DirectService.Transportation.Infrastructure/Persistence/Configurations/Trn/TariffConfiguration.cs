// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Trn;

internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.ToTable("TRN_Tariffs", "trn", t =>
        {
            t.HasCheckConstraint("CK_TRN_Tariffs_PartyType", "[PartyType] IN ('Customer','Partner')");
            t.HasCheckConstraint("CK_TRN_Tariffs_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_TRN_Tariffs_Specificity", "[Specificity] BETWEEN 0 AND 15");
            t.HasCheckConstraint("CK_TRN_Tariffs_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_TRN_Tariffs_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_TRN_Tariffs_CustomerId_PartnerId", "([PartyType] = 'Customer' AND [PartnerId] IS NULL) OR ([PartyType] = 'Partner' AND [CustomerId] IS NULL)");
            t.HasCheckConstraint("CK_TRN_Tariffs_ValidTo", "[ValidTo] >= [ValidFrom]");
        });

        builder.HasKey(e => e.Id).HasName("PK_TRN_Tariffs");

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
        builder.Property(e => e.PartyType)
            .HasColumnName("PartyType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.CustomerId)
            .HasColumnName("CustomerId")
            .HasColumnType("bigint")
            .HasColumnOrder(5)
            .IsRequired(false);
        builder.Property(e => e.PartnerId)
            .HasColumnName("PartnerId")
            .HasColumnType("bigint")
            .HasColumnOrder(6)
            .IsRequired(false);
        builder.Property(e => e.MaterialId)
            .HasColumnName("MaterialId")
            .HasColumnType("bigint")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.LoadingSiteId)
            .HasColumnName("LoadingSiteId")
            .HasColumnType("bigint")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.UnloadingSiteId)
            .HasColumnName("UnloadingSiteId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.Price)
            .HasColumnName("Price")
            .HasColumnType("decimal(18,4)")
            .HasColumnOrder(10)
            .HasPrecision(18, 4)
            .IsRequired(true);
        builder.Property(e => e.ValidFrom)
            .HasColumnName("ValidFrom")
            .HasColumnType("date")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.ValidTo)
            .HasColumnName("ValidTo")
            .HasColumnType("date")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.Specificity)
            .HasColumnName("Specificity")
            .HasColumnType("tinyint")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasComputedColumnSql("ISNULL(CONVERT(tinyint, CASE WHEN [CustomerId] IS NOT NULL OR [PartnerId] IS NOT NULL THEN 8 ELSE 0 END + CASE WHEN [MaterialId] IS NOT NULL THEN 4 ELSE 0 END + CASE WHEN [LoadingSiteId] IS NOT NULL THEN 2 ELSE 0 END + CASE WHEN [UnloadingSiteId] IS NOT NULL THEN 1 ELSE 0 END), 0)", stored: true);
        builder.Property(e => e.Notes)
            .HasColumnName("Notes")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(14)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(15)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(16)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(19)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(20)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(22)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(23)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(24)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_TRN_Tariffs_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "PartyType", "CustomerId", "PartnerId", "MaterialId", "LoadingSiteId", "UnloadingSiteId", "ValidFrom" }, "UX_TRN_Tariffs_CompanyId_PartyType_CustomerId_PartnerId_MaterialId_LoadingSiteId_UnloadingSiteId_ValidFrom")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "CompanyId", "PartyType", "Status", "Specificity", "ValidFrom" }, "IX_TRN_Tariffs_Lookup")
            .IsDescending(false, false, false, true, true);
        builder.HasIndex(new[] { "CustomerId" }, "IX_TRN_Tariffs_CustomerId");
        builder.HasIndex(new[] { "PartnerId" }, "IX_TRN_Tariffs_PartnerId");
        builder.HasIndex(new[] { "MaterialId" }, "IX_TRN_Tariffs_MaterialId");
        builder.HasIndex(new[] { "LoadingSiteId" }, "IX_TRN_Tariffs_LoadingSiteId");
        builder.HasIndex(new[] { "UnloadingSiteId" }, "IX_TRN_Tariffs_UnloadingSiteId");

        builder.HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Tariffs_TRN_Customers_CustomerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Partner)
            .WithMany()
            .HasForeignKey(e => e.PartnerId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Tariffs_TRN_Partners_PartnerId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Material)
            .WithMany()
            .HasForeignKey(e => e.MaterialId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Tariffs_TRN_Materials_MaterialId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.LoadingSite)
            .WithMany()
            .HasForeignKey(e => e.LoadingSiteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Tariffs_TRN_LoadingSites_LoadingSiteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.UnloadingSite)
            .WithMany()
            .HasForeignKey(e => e.UnloadingSiteId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_TRN_Tariffs_TRN_UnloadingSites_UnloadingSiteId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_TRN_Tariffs_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_TRN_Tariffs_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_TRN_Tariffs_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_TRN_Tariffs_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_TRN_Tariffs_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
