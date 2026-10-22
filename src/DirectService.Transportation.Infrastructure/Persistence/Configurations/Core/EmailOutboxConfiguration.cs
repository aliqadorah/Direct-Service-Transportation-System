using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Core;

/// <summary>core.EmailOutbox (revision 6, system email, DT-251; Database_Design_Revision6.md §B10).</summary>
internal sealed class EmailOutboxConfiguration : IEntityTypeConfiguration<EmailOutbox>
{
    public void Configure(EntityTypeBuilder<EmailOutbox> builder)
    {
        builder.ToTable("EmailOutbox", "core", t =>
        {
            t.HasCheckConstraint("CK_EmailOutbox_Status", "[Status] IN ('Pending','Sending','Sent','Failed','Suppressed')");
            t.HasCheckConstraint("CK_EmailOutbox_LanguageCode", "[LanguageCode] IN ('ar','en')");
            t.HasCheckConstraint("CK_EmailOutbox_Attempts", "[Attempts] >= 0");
            t.HasCheckConstraint("CK_EmailOutbox_SentAt", "[Status] <> 'Sent' OR [SentAt] IS NOT NULL");
            t.HasCheckConstraint("CK_EmailOutbox_FailedAt", "[Status] <> 'Failed' OR [FailedAt] IS NOT NULL");
            // The message is never kept once sent, nor after a failure when it holds a single-use link.
            t.HasCheckConstraint("CK_EmailOutbox_PayloadEncrypted",
                "[PayloadEncrypted] IS NULL OR [Status] IN ('Pending','Sending') OR ([Status] = 'Failed' AND [HasSecretLink] = 0)");
        });

        builder.HasKey(e => e.Id).HasName("PK_EmailOutbox");
        builder.Property(e => e.Id).HasColumnType("bigint").HasColumnOrder(0).UseIdentityColumn();
        builder.Property(e => e.PublicId).HasColumnType("uniqueidentifier").HasColumnOrder(1).HasDefaultValueSql("NEWID()").ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasColumnType("bigint").HasColumnOrder(2);
        builder.Property(e => e.Template).HasColumnType("varchar(40)").HasColumnOrder(3).IsUnicode(false).HasMaxLength(40).IsRequired();
        builder.Property(e => e.LanguageCode).HasColumnType("varchar(5)").HasColumnOrder(4).IsUnicode(false).HasMaxLength(5).IsRequired();
        builder.Property(e => e.Recipient).HasColumnType("nvarchar(254)").HasColumnOrder(5).HasMaxLength(254).IsRequired();
        builder.Property(e => e.Status).HasColumnType("varchar(10)").HasColumnOrder(6).IsUnicode(false).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Attempts).HasColumnType("int").HasColumnOrder(7).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.NextAttemptAt).HasColumnType("datetime2(3)").HasColumnOrder(8);
        builder.Property(e => e.ClaimedUntil).HasColumnType("datetime2(3)").HasColumnOrder(9);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasColumnOrder(10).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedNever();
        builder.Property(e => e.LastAttemptAt).HasColumnType("datetime2(3)").HasColumnOrder(11);
        builder.Property(e => e.SentAt).HasColumnType("datetime2(3)").HasColumnOrder(12);
        builder.Property(e => e.FailedAt).HasColumnType("datetime2(3)").HasColumnOrder(13);
        builder.Property(e => e.ErrorKind).HasColumnType("varchar(40)").HasColumnOrder(14).IsUnicode(false).HasMaxLength(40);
        builder.Property(e => e.LastError).HasColumnType("nvarchar(400)").HasColumnOrder(15).HasMaxLength(400);
        builder.Property(e => e.Mode).HasColumnType("varchar(20)").HasColumnOrder(16).IsUnicode(false).HasMaxLength(20);
        builder.Property(e => e.HasSecretLink).HasColumnType("bit").HasColumnOrder(17).HasDefaultValueSql("0").ValueGeneratedNever();
        builder.Property(e => e.PayloadEncrypted).HasColumnType("varbinary(max)").HasColumnOrder(18);
        builder.Property(e => e.ResentFromId).HasColumnType("bigint").HasColumnOrder(19);
        builder.Property(e => e.ResentAt).HasColumnType("datetime2(3)").HasColumnOrder(20);
        builder.Property(e => e.RowVersion).HasColumnType("rowversion").HasColumnOrder(21).IsRowVersion();

        builder.HasIndex(new[] { "PublicId" }, "UX_EmailOutbox_PublicId").IsUnique().HasFilter(null);
        // The worker's queue: due rows in order.
        builder.HasIndex(new[] { "Status", "NextAttemptAt" }, "IX_EmailOutbox_Status_NextAttemptAt");
        // The throttle (sent in the last minute) and the console's status (last success).
        builder.HasIndex(new[] { "SentAt" }, "IX_EmailOutbox_SentAt").HasFilter("[SentAt] IS NOT NULL");
        // The delivery log (newest first) and the per-recipient limit.
        builder.HasIndex(new[] { "CreatedAt" }, "IX_EmailOutbox_CreatedAt");
        builder.HasIndex(new[] { "Recipient", "Template", "CreatedAt" }, "IX_EmailOutbox_Recipient_Template_CreatedAt");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).HasConstraintName("FK_EmailOutbox_Tenants_TenantId").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmailOutbox>().WithMany().HasForeignKey(e => e.ResentFromId).HasConstraintName("FK_EmailOutbox_EmailOutbox_ResentFromId").OnDelete(DeleteBehavior.NoAction);
    }
}
