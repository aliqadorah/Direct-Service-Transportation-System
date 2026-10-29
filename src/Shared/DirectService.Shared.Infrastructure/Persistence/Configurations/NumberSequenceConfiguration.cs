// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 4; the foreign keys to core.Tenants / Companies / Users are in Platform.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("NumberSequences", "cfg", t =>
        {
            t.HasCheckConstraint("CK_NumberSequences_Digits", "[Digits] BETWEEN 3 AND 10");
            t.HasCheckConstraint("CK_NumberSequences_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_NumberSequences");

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
        builder.Property(e => e.DocumentType)
            .HasColumnName("DocumentType")
            .HasColumnType("varchar(40)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(40)
            .IsRequired(true);
        builder.Property(e => e.Prefix)
            .HasColumnName("Prefix")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.Digits)
            .HasColumnName("Digits")
            .HasColumnType("tinyint")
            .HasColumnOrder(6)
            .IsRequired(true)
            .HasDefaultValueSql("5")
            .ValueGeneratedNever();
        builder.Property(e => e.NextNumber)
            .HasColumnName("NextNumber")
            .HasColumnType("bigint")
            .HasColumnOrder(7)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
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

        builder.HasIndex(new[] { "PublicId" }, "UX_NumberSequences_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "DocumentType" }, "UX_NumberSequences_CompanyId_DocumentType")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
