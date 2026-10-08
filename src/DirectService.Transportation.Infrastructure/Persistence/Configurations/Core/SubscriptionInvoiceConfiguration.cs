// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("SubscriptionInvoices", "core", t =>
        {
            t.HasCheckConstraint("CK_SubscriptionInvoices_Amount", "[Amount] >= 0");
            t.HasCheckConstraint("CK_SubscriptionInvoices_Tax", "[Tax] >= 0");
            t.HasCheckConstraint("CK_SubscriptionInvoices_Status", "[Status] IN ('Due','Paid','Failed','Credited')");
            t.HasCheckConstraint("CK_SubscriptionInvoices_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_SubscriptionInvoices_PeriodEnd", "[PeriodEnd] >= [PeriodStart]");
            t.HasCheckConstraint("CK_SubscriptionInvoices_Total", "[Total] = [Amount] + [Tax]");
        });

        builder.HasKey(e => e.Id).HasName("PK_SubscriptionInvoices");

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
        builder.Property(e => e.Number)
            .HasColumnName("Number")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(3)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.PeriodStart)
            .HasColumnName("PeriodStart")
            .HasColumnType("date")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.PeriodEnd)
            .HasColumnName("PeriodEnd")
            .HasColumnType("date")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.Amount)
            .HasColumnName("Amount")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(6)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Tax)
            .HasColumnName("Tax")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(7)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.Total)
            .HasColumnName("Total")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(8)
            .HasPrecision(18, 3)
            .IsRequired(true);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(15)")
            .HasColumnOrder(9)
            .IsUnicode(false)
            .HasMaxLength(15)
            .IsRequired(true)
            .HasDefaultValueSql("'Due'")
            .ValueGeneratedNever();
        builder.Property(e => e.PaidAt)
            .HasColumnName("PaidAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(10)
            .IsRequired(false);
        builder.Property(e => e.GatewayReference)
            .HasColumnName("GatewayReference")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
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

        builder.HasIndex(new[] { "PublicId" }, "UX_SubscriptionInvoices_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "Number" }, "UX_SubscriptionInvoices_Number")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_SubscriptionInvoices_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_SubscriptionInvoices_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_SubscriptionInvoices_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_SubscriptionInvoices_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
