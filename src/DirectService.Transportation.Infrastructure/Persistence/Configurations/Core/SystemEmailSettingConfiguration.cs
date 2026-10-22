using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

/// <summary>core.SystemEmailSettings (revision 6, system email, DT-250; Database_Design_Revision6.md §B10).</summary>
internal sealed class SystemEmailSettingConfiguration : IEntityTypeConfiguration<SystemEmailSetting>
{
    public void Configure(EntityTypeBuilder<SystemEmailSetting> builder)
    {
        builder.ToTable("SystemEmailSettings", "core", t =>
        {
            t.HasCheckConstraint("CK_SystemEmailSettings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_SystemEmailSettings_State", "[State] IN ('Active','Pending','Retired')");
            t.HasCheckConstraint("CK_SystemEmailSettings_Mode", "[Mode] IN ('Development','Microsoft365')");
            t.HasCheckConstraint("CK_SystemEmailSettings_Microsoft365",
                "[Mode] <> 'Microsoft365' OR ([EntraTenantId] IS NOT NULL AND [EntraClientId] IS NOT NULL AND [ClientSecretEncrypted] IS NOT NULL AND [SenderMailbox] IS NOT NULL)");
            t.HasCheckConstraint("CK_SystemEmailSettings_ExpiryWarnedDays", "[ExpiryWarnedDays] IS NULL OR [ExpiryWarnedDays] > 0");
        });

        builder.HasKey(e => e.Id).HasName("PK_SystemEmailSettings");
        builder.Property(e => e.Id).HasColumnType("bigint").HasColumnOrder(0).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnType("uniqueidentifier").HasColumnOrder(1).HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.State).HasColumnType("varchar(10)").HasColumnOrder(2).IsUnicode(false).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Mode).HasColumnType("varchar(20)").HasColumnOrder(3).IsUnicode(false).HasMaxLength(20).IsRequired();
        builder.Property(e => e.EntraTenantId).HasColumnType("nvarchar(100)").HasColumnOrder(4).HasMaxLength(100);
        builder.Property(e => e.EntraClientId).HasColumnType("nvarchar(100)").HasColumnOrder(5).HasMaxLength(100);
        builder.Property(e => e.ClientSecretEncrypted).HasColumnType("varbinary(max)").HasColumnOrder(6);
        builder.Property(e => e.SecretSavedAt).HasColumnType("datetime2(3)").HasColumnOrder(7);
        builder.Property(e => e.SecretExpiresOn).HasColumnType("date").HasColumnOrder(8);
        builder.Property(e => e.SenderMailbox).HasColumnType("nvarchar(254)").HasColumnOrder(9).HasMaxLength(254);
        builder.Property(e => e.DisplayName).HasColumnType("nvarchar(100)").HasColumnOrder(10).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ReplyTo).HasColumnType("nvarchar(254)").HasColumnOrder(11).HasMaxLength(254);
        builder.Property(e => e.ActivatedAt).HasColumnType("datetime2(3)").HasColumnOrder(12);
        builder.Property(e => e.LastTestAt).HasColumnType("datetime2(3)").HasColumnOrder(13);
        builder.Property(e => e.LastTestOk).HasColumnType("bit").HasColumnOrder(14);
        builder.Property(e => e.LastTestError).HasColumnType("nvarchar(400)").HasColumnOrder(15).HasMaxLength(400);
        builder.Property(e => e.ExpiryWarnedDays).HasColumnType("int").HasColumnOrder(16);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasColumnOrder(17).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasColumnType("bigint").HasColumnOrder(18);
        builder.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)").HasColumnOrder(19);
        builder.Property(e => e.UpdatedBy).HasColumnType("bigint").HasColumnOrder(20);
        builder.Property(e => e.IsDeleted).HasColumnType("bit").HasColumnOrder(21).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.DeletedAt).HasColumnType("datetime2(3)").HasColumnOrder(22);
        builder.Property(e => e.DeletedBy).HasColumnType("bigint").HasColumnOrder(23);
        builder.Property(e => e.DeleteReason).HasColumnType("nvarchar(500)").HasColumnOrder(24).HasMaxLength(500);
        builder.Property(e => e.RowVersion).HasColumnType("rowversion").HasColumnOrder(25).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_SystemEmailSettings_PublicId").IsUnique().HasFilter(null);
        // One Active and one Pending row at most.
        builder.HasIndex(new[] { "State" }, "UX_SystemEmailSettings_Active").IsUnique().HasFilter("[State] = 'Active' AND [IsDeleted] = 0");
        builder.HasIndex(new[] { "State" }, "UX_SystemEmailSettings_Pending").IsUnique().HasFilter("[State] = 'Pending' AND [IsDeleted] = 0");

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedBy).HasConstraintName("FK_SystemEmailSettings_Users_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedBy).HasConstraintName("FK_SystemEmailSettings_Users_UpdatedBy").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DeletedBy).HasConstraintName("FK_SystemEmailSettings_Users_DeletedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
