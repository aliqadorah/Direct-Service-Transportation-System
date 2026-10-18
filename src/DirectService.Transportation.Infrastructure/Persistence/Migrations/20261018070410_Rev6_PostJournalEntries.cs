using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_PostJournalEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 14L,
                columns: new[] { "DescriptionAr", "DescriptionEn" },
                values: new object[] { "إعداد القيود اليدوية: حفظ المسودات وتعديلها، واستخدام نماذج القيود وإدارتها (الترحيل يحتاج صلاحية «ترحيل القيود اليومية»)", "Prepare manual journal entries: save and edit drafts, use and manage entry templates (posting needs “Post journal entries”)" });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Permissions",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeleteReason", "DeletedAt", "DeletedBy", "DescriptionAr", "DescriptionEn", "GroupName", "IsDeleted", "PermissionKey", "PublicId", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 26L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "ترحيل القيود اليومية: ترحيل القيود اليدوية والمسودات، وتعديل القيود المرحّلة وإلغاؤها، واستيراد القيود", "Post journal entries: post manual entries and drafts, edit and void posted entries, import entries", "Accounting", false, "acc.acct.postentry", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a26"), null, null });

            // DT-201 data step: every role that holds acc.acct.post today (preset or custom) also gets acc.acct.postentry, so no one loses
            // the posting they have; owners then remove it from roles that should only prepare entries.
            migrationBuilder.Sql("""
                INSERT INTO [core].[RolePermissions] ([TenantId], [RoleId], [PermissionId])
                SELECT rp.[TenantId], rp.[RoleId], 26
                FROM [core].[RolePermissions] rp
                WHERE rp.[PermissionId] = 14 AND rp.[IsDeleted] = 0
                  AND NOT EXISTS (SELECT 1 FROM [core].[RolePermissions] x WHERE x.[RoleId] = rp.[RoleId] AND x.[PermissionId] = 26 AND x.[IsDeleted] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [core].[RolePermissions] WHERE [PermissionId] = 26;");
            migrationBuilder.DeleteData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 26L);

            migrationBuilder.UpdateData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 14L,
                columns: new[] { "DescriptionAr", "DescriptionEn" },
                values: new object[] { "إنشاء القيود اليدوية وتعديلها وإلغاؤها، واستيراد القيود", "Create, edit and void manual journal entries; import entries" });
        }
    }
}
