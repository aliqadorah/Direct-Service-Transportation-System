// Revision 6 part B (owner change request 15 Oct 2026): journal entry templates. Hand-written in the style of the generated configurations.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class JournalTemplateConfiguration : IEntityTypeConfiguration<JournalTemplate>
{
    public void Configure(EntityTypeBuilder<JournalTemplate> builder)
    {
        builder.ToTable("JournalTemplates", "acc", t =>
        {
            t.HasCheckConstraint("CK_JournalTemplates_Lines", "ISJSON([Lines]) = 1");
            t.HasCheckConstraint("CK_JournalTemplates_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id).HasName("PK_JournalTemplates");

        builder.Property(e => e.Id).HasColumnName("Id").HasColumnType("bigint").HasColumnOrder(0).IsRequired(true).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnName("PublicId").HasColumnType("uniqueidentifier").HasColumnOrder(1).IsRequired(true)
            .HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnName("TenantId").HasColumnType("bigint").HasColumnOrder(2).IsRequired(true);
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId").HasColumnType("bigint").HasColumnOrder(3).IsRequired(true);
        builder.Property(e => e.Name).HasColumnName("Name").HasColumnType("nvarchar(100)").HasColumnOrder(4).IsUnicode(true).HasMaxLength(100).IsRequired(true);
        builder.Property(e => e.Description).HasColumnName("Description").HasColumnType("nvarchar(500)").HasColumnOrder(5).IsUnicode(true)
            .HasMaxLength(500).IsRequired(false);
        builder.Property(e => e.Reference).HasColumnName("Reference").HasColumnType("nvarchar(100)").HasColumnOrder(6).IsUnicode(true)
            .HasMaxLength(100).IsRequired(false);
        builder.Property(e => e.Lines).HasColumnName("Lines").HasColumnType("nvarchar(max)").HasColumnOrder(7).IsUnicode(true).IsRequired(true);
        builder.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime2(3)").HasColumnOrder(8).IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasColumnName("CreatedBy").HasColumnType("bigint").HasColumnOrder(9).IsRequired(false);
        builder.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime2(3)").HasColumnOrder(10).IsRequired(false);
        builder.Property(e => e.UpdatedBy).HasColumnName("UpdatedBy").HasColumnType("bigint").HasColumnOrder(11).IsRequired(false);
        builder.Property(e => e.IsDeleted).HasColumnName("IsDeleted").HasColumnType("bit").HasColumnOrder(12).IsRequired(true)
            .HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.DeletedAt).HasColumnName("DeletedAt").HasColumnType("datetime2(3)").HasColumnOrder(13).IsRequired(false);
        builder.Property(e => e.DeletedBy).HasColumnName("DeletedBy").HasColumnType("bigint").HasColumnOrder(14).IsRequired(false);
        builder.Property(e => e.DeleteReason).HasColumnName("DeleteReason").HasColumnType("nvarchar(500)").HasColumnOrder(15).IsUnicode(true)
            .HasMaxLength(500).IsRequired(false);
        builder.Property(e => e.RowVersion).HasColumnName("RowVersion").HasColumnType("rowversion").HasColumnOrder(16).IsRequired(true).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_JournalTemplates_PublicId").IsUnique().HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Name" }, "UX_JournalTemplates_CompanyId_Name").IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).HasConstraintName("FK_JournalTemplates_Tenants_TenantId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("FK_JournalTemplates_Companies_CompanyId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedBy).HasConstraintName("FK_JournalTemplates_Users_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedBy).HasConstraintName("FK_JournalTemplates_Users_UpdatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedBy).HasConstraintName("FK_JournalTemplates_Users_DeletedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
