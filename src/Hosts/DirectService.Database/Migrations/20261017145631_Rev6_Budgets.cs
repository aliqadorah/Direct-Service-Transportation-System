using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_Budgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Budgets_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValueSql: "N'Draft'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Budgets_Status"),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ApprovedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Budgets_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Budgets_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.CheckConstraint("CK_Budgets_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Budgets_FiscalYear", "[FiscalYear] BETWEEN 2000 AND 2100");
                    table.CheckConstraint("CK_Budgets_Status", "[Status] IN (N'Draft', N'Approved')");
                    table.CheckConstraint("CK_Budgets_Status_ApprovedAt_ApprovedBy", "[Status] <> N'Approved' OR ([ApprovedAt] IS NOT NULL AND [ApprovedBy] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Budgets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Budgets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Budgets_Users_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Budgets_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Budgets_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Budgets_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BudgetLines",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    BudgetId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CostCentreId = table.Column<long>(type: "bigint", nullable: true),
                    Month01 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month01"),
                    Month02 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month02"),
                    Month03 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month03"),
                    Month04 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month04"),
                    Month05 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month05"),
                    Month06 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month06"),
                    Month07 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month07"),
                    Month08 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month08"),
                    Month09 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month09"),
                    Month10 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month10"),
                    Month11 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month11"),
                    Month12 = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Month12"),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_Total"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BudgetLines_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                    table.CheckConstraint("CK_BudgetLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_BudgetLines_Total", "[Total] = [Month01] + [Month02] + [Month03] + [Month04] + [Month05] + [Month06] + [Month07] + [Month08] + [Month09] + [Month10] + [Month11] + [Month12]");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalSchema: "acc",
                        principalTable: "Budgets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_CostCentres_CostCentreId",
                        column: x => x.CostCentreId,
                        principalSchema: "acc",
                        principalTable: "CostCentres",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BudgetLines_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Permissions",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeleteReason", "DeletedAt", "DeletedBy", "DescriptionAr", "DescriptionEn", "GroupName", "IsDeleted", "PermissionKey", "PublicId", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 23L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض الموازنات وتقرير الموازنة مقابل الفعلي", "View budgets and the budget vs actual report", "Accounting", false, "acc.budget.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a23"), null, null },
                    { 24L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إنشاء الموازنات وتعديلها واستيرادها ونسخها من فعلي السنة السابقة وحذف المسودات", "Create, edit and import budgets, copy them from last year's actuals, delete drafts", "Accounting", false, "acc.budget.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a24"), null, null },
                    { 25L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "اعتماد الموازنات وإعادتها إلى مسودة", "Approve budgets and return them to draft", "Accounting", false, "acc.budget.approve", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a25"), null, null }
                });

            migrationBuilder.CreateIndex(
                name: "UX_BudgetLines_BudgetId_AccountId_CostCentreId",
                schema: "acc",
                table: "BudgetLines",
                columns: new[] { "BudgetId", "AccountId", "CostCentreId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_BudgetLines_PublicId",
                schema: "acc",
                table: "BudgetLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Budgets_CompanyId_FiscalYear_Approved",
                schema: "acc",
                table: "Budgets",
                columns: new[] { "CompanyId", "FiscalYear" },
                unique: true,
                filter: "[Status] = N'Approved' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Budgets_CompanyId_FiscalYear_Name",
                schema: "acc",
                table: "Budgets",
                columns: new[] { "CompanyId", "FiscalYear", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Budgets_PublicId",
                schema: "acc",
                table: "Budgets",
                column: "PublicId",
                unique: true);

            // Revision 6 part B data step: existing preset roles get the budget keys of their preset (PresetRoles.Permissions) — Owner and
            // Administrator all three, Accountant view + manage, Viewer view. Custom roles are left as they are (their owners add the keys).
            migrationBuilder.Sql("""
                INSERT INTO [core].[RolePermissions] ([TenantId], [RoleId], [PermissionId])
                SELECT r.[TenantId], r.[Id], p.[Id]
                FROM [core].[Roles] r
                JOIN [core].[Permissions] p ON
                       (r.[Name] IN (N'Owner', N'Administrator') AND p.[PermissionKey] IN (N'acc.budget.view', N'acc.budget.manage', N'acc.budget.approve'))
                    OR (r.[Name] = N'Accountant' AND p.[PermissionKey] IN (N'acc.budget.view', N'acc.budget.manage'))
                    OR (r.[Name] = N'Viewer' AND p.[PermissionKey] = N'acc.budget.view')
                WHERE r.[IsPreset] = 1 AND r.[IsDeleted] = 0
                  AND NOT EXISTS (SELECT 1 FROM [core].[RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id] AND x.[IsDeleted] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [core].[RolePermissions] WHERE [PermissionId] IN (23, 24, 25);");
            migrationBuilder.DropTable(
                name: "BudgetLines",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "Budgets",
                schema: "acc");

            migrationBuilder.DeleteData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 23L);

            migrationBuilder.DeleteData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 24L);

            migrationBuilder.DeleteData(
                schema: "core",
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 25L);
        }
    }
}
