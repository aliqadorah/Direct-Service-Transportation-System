// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;


namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements", "core", t =>
        {
            t.HasCheckConstraint("CK_Announcements_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_Announcements_EndsAt", "[EndsAt] > [StartsAt]");
        });

        builder.HasKey(e => e.Id).HasName("PK_Announcements");

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
        builder.Property(e => e.Title)
            .HasColumnName("Title")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(2)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.Message)
            .HasColumnName("Message")
            .HasColumnType("nvarchar(1000)")
            .HasColumnOrder(3)
            .IsUnicode(true)
            .HasMaxLength(1000)
            .IsRequired(true);
        builder.Property(e => e.StartsAt)
            .HasColumnName("StartsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(4)
            .IsRequired(true);
        builder.Property(e => e.EndsAt)
            .HasColumnName("EndsAt")
            .HasColumnType("datetime2")
            .HasColumnOrder(5)
            .IsRequired(true);
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(6)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(7)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(9)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(10)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(11)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(12)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(13)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(14)
            .IsRequired(true)
            .IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_Announcements_PublicId")
            .IsUnique()
            .HasFilter(null);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Announcements_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Announcements_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Announcements_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
