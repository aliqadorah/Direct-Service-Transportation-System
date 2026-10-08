// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class ApArPartyConfiguration : IEntityTypeConfiguration<ApArParty>
{
    public void Configure(EntityTypeBuilder<ApArParty> builder)
    {
        builder.ToTable("ApArParties", "acc", t =>
        {
            t.HasCheckConstraint("CK_ApArParties_PartyRole", "[PartyRole] IN ('Supplier','Customer')");
            t.HasCheckConstraint("CK_ApArParties_PaymentTermsDays", "[PaymentTermsDays] >= 0");
            t.HasCheckConstraint("CK_ApArParties_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_ApArParties_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_ApArParties");

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
        builder.Property(e => e.PartyRole)
            .HasColumnName("PartyRole")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.Name)
            .HasColumnName("Name")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.TaxNumber)
            .HasColumnName("TaxNumber")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(6)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(e => e.Phone)
            .HasColumnName("Phone")
            .HasColumnType("varchar(20)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(e => e.Address)
            .HasColumnName("Address")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(8)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.ControlAccountId)
            .HasColumnName("ControlAccountId")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(true);
        builder.Property(e => e.PaymentTermsDays)
            .HasColumnName("PaymentTermsDays")
            .HasColumnType("int")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(11)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(13)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(14)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(15)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(16)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(19)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(20)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_ApArParties_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "ControlAccountId" }, "IX_ApArParties_ControlAccountId");
        builder.HasIndex(new[] { "CompanyId" }, "IX_ApArParties_CompanyId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(e => e.ControlAccount)
            .WithMany()
            .HasForeignKey(e => e.ControlAccountId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(true)
            .HasConstraintName("FK_ApArParties_Accounts_ControlAccountId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_ApArParties_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_ApArParties_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_ApArParties_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_ApArParties_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_ApArParties_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
