// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
// Shared since Stage 2 session 4; the foreign keys to core.Tenants / Companies / Users are in Transportation.Infrastructure
// Persistence/CrossModuleRelationships.cs (design a.2), with the same names.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Shared.Infrastructure.Persistence.Configurations;

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("CustomFieldDefinitions", "cfg", t =>
        {
            t.HasCheckConstraint("CK_CustomFieldDefinitions_FieldType", "[FieldType] IN ('Text','Number','Date','List','Checkbox','LongText')");
            t.HasCheckConstraint("CK_CustomFieldDefinitions_Options", "ISJSON([Options]) = 1");
            t.HasCheckConstraint("CK_CustomFieldDefinitions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_CustomFieldDefinitions");

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
        builder.Property(e => e.EntityType)
            .HasColumnName("EntityType")
            .HasColumnType("varchar(40)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(40)
            .IsRequired(true);
        builder.Property(e => e.FieldKey)
            .HasColumnName("FieldKey")
            .HasColumnType("varchar(50)")
            .HasColumnOrder(5)
            .IsUnicode(false)
            .HasMaxLength(50)
            .IsRequired(true);
        builder.Property(e => e.LabelAr)
            .HasColumnName("LabelAr")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(6)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(e => e.LabelEn)
            .HasColumnName("LabelEn")
            .HasColumnType("nvarchar(100)")
            .HasColumnOrder(7)
            .IsUnicode(true)
            .HasMaxLength(100)
            .IsRequired(false);
        builder.Property(e => e.FieldType)
            .HasColumnName("FieldType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(8)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.Options)
            .HasColumnName("Options")
            .HasColumnType("nvarchar(max)")
            .HasColumnOrder(9)
            .IsUnicode(true)
            .IsRequired(false);
        builder.Property(e => e.DefaultValue)
            .HasColumnName("DefaultValue")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(10)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(false);
        builder.Property(e => e.HelpText)
            .HasColumnName("HelpText")
            .HasColumnType("nvarchar(300)")
            .HasColumnOrder(11)
            .IsUnicode(true)
            .HasMaxLength(300)
            .IsRequired(false);
        builder.Property(e => e.IsRequired)
            .HasColumnName("IsRequired")
            .HasColumnType("bit")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ShowAsColumn)
            .HasColumnName("ShowAsColumn")
            .HasColumnType("bit")
            .HasColumnOrder(13)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.SortOrder)
            .HasColumnName("SortOrder")
            .HasColumnType("int")
            .HasColumnOrder(14)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.IsHidden)
            .HasColumnName("IsHidden")
            .HasColumnType("bit")
            .HasColumnOrder(15)
            .IsRequired(true)
            .HasDefaultValueSql("0")
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

        builder.HasIndex(new[] { "PublicId" }, "UX_CustomFieldDefinitions_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "EntityType", "FieldKey" }, "UX_CustomFieldDefinitions_CompanyId_EntityType_FieldKey")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
