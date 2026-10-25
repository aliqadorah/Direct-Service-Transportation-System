// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on. Names, types, defaults and constraints must match the design.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Acc;

namespace DirectService.Transportation.Infrastructure.Persistence.Configurations.Acc;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", "acc", t =>
        {
            t.HasCheckConstraint("CK_Accounts_AccountType", "[AccountType] IN ('Asset','Liability','Equity','Revenue','Expense')");
            t.HasCheckConstraint("CK_Accounts_OpeningDebit", "[OpeningDebit] >= 0");
            t.HasCheckConstraint("CK_Accounts_OpeningCredit", "[OpeningCredit] >= 0");
            t.HasCheckConstraint("CK_Accounts_ControlPartyType", "[ControlPartyType] IN ('Customer','Driver','Partner','Supplier')");
            t.HasCheckConstraint("CK_Accounts_Status", "[Status] IN ('Active','Archived')");
            t.HasCheckConstraint("CK_Accounts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_Accounts_OpeningDebit_OpeningCredit", "NOT ([OpeningDebit] > 0 AND [OpeningCredit] > 0)");
        });

        builder.HasKey(e => e.Id).HasName("PK_Accounts");

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
        builder.Property(e => e.Code)
            .HasColumnName("Code")
            .HasColumnType("varchar(30)")
            .HasColumnOrder(4)
            .IsUnicode(false)
            .HasMaxLength(30)
            .IsRequired(true);
        builder.Property(e => e.NameAr)
            .HasColumnName("NameAr")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(5)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(true);
        builder.Property(e => e.NameEn)
            .HasColumnName("NameEn")
            .HasColumnType("nvarchar(200)")
            .HasColumnOrder(6)
            .IsUnicode(true)
            .HasMaxLength(200)
            .IsRequired(false);
        builder.Property(e => e.AccountType)
            .HasColumnName("AccountType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(7)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true);
        builder.Property(e => e.ParentId)
            .HasColumnName("ParentId")
            .HasColumnType("bigint")
            .HasColumnOrder(8)
            .IsRequired(false);
        builder.Property(e => e.IsPostable)
            .HasColumnName("IsPostable")
            .HasColumnType("bit")
            .HasColumnOrder(9)
            .IsRequired(true)
            .HasDefaultValueSql("1")
            .ValueGeneratedNever();
        builder.Property(e => e.OpeningDebit)
            .HasColumnName("OpeningDebit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(10)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.OpeningCredit)
            .HasColumnName("OpeningCredit")
            .HasColumnType("decimal(18,3)")
            .HasColumnOrder(11)
            .HasPrecision(18, 3)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.IsControlAccount)
            .HasColumnName("IsControlAccount")
            .HasColumnType("bit")
            .HasColumnOrder(12)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.ControlPartyType)
            .HasColumnName("ControlPartyType")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(13)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasColumnName("Status")
            .HasColumnType("varchar(10)")
            .HasColumnOrder(14)
            .IsUnicode(false)
            .HasMaxLength(10)
            .IsRequired(true)
            .HasDefaultValueSql("'Active'")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedAt)
            .HasColumnName("CreatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(15)
            .IsRequired(true)
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedNever();
        builder.Property(e => e.CreatedBy)
            .HasColumnName("CreatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(16)
            .IsRequired(false);
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(17)
            .IsRequired(false);
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("UpdatedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(18)
            .IsRequired(false);
        builder.Property(e => e.IsDeleted)
            .HasColumnName("IsDeleted")
            .HasColumnType("bit")
            .HasColumnOrder(19)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.Property(e => e.DeletedAt)
            .HasColumnName("DeletedAt")
            .HasColumnType("datetime2(3)")
            .HasColumnOrder(20)
            .IsRequired(false);
        builder.Property(e => e.DeletedBy)
            .HasColumnName("DeletedBy")
            .HasColumnType("bigint")
            .HasColumnOrder(21)
            .IsRequired(false);
        builder.Property(e => e.DeleteReason)
            .HasColumnName("DeleteReason")
            .HasColumnType("nvarchar(500)")
            .HasColumnOrder(22)
            .IsUnicode(true)
            .HasMaxLength(500)
            .IsRequired(false);
        builder.Property(e => e.RowVersion)
            .HasColumnName("RowVersion")
            .HasColumnType("rowversion")
            .HasColumnOrder(23)
            .IsRequired(true)
            .IsRowVersion();
        // DT-256 (owner request 25 Oct 2026): tax accounts offered on journal lines; never a control account.
        builder.Property(e => e.IsTaxAccount)
            .HasColumnName("IsTaxAccount")
            .HasColumnType("bit")
            .HasColumnOrder(24)
            .IsRequired(true)
            .HasDefaultValueSql("0")
            .ValueGeneratedNever();
        builder.ToTable(t => t.HasCheckConstraint("CK_Accounts_IsTaxAccount", "[IsTaxAccount] = 0 OR [IsControlAccount] = 0"));

        builder.HasIndex(new[] { "PublicId" }, "UX_Accounts_PublicId")
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(new[] { "CompanyId", "Code" }, "UX_Accounts_CompanyId_Code")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(new[] { "ParentId" }, "IX_Accounts_ParentId");

        builder.HasOne(e => e.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(e => e.ParentId)
            .HasPrincipalKey(p => p.Id)
            .IsRequired(false)
            .HasConstraintName("FK_Accounts_Accounts_ParentId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_Accounts_Tenants_TenantId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(e => e.CompanyId)
            .HasConstraintName("FK_Accounts_Companies_CompanyId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .HasConstraintName("FK_Accounts_Users_CreatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UpdatedBy)
            .HasConstraintName("FK_Accounts_Users_UpdatedBy")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.DeletedBy)
            .HasConstraintName("FK_Accounts_Users_DeletedBy")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
