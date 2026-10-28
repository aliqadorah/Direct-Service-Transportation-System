using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_JournalLineTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                schema: "acc",
                table: "EntryLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 23);

            migrationBuilder.AddColumn<long>(
                name: "TaxAccountId",
                schema: "acc",
                table: "EntryLines",
                type: "bigint",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 27);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "acc",
                table: "EntryLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 26);

            migrationBuilder.AddColumn<long>(
                name: "TaxCodeId",
                schema: "acc",
                table: "EntryLines",
                type: "bigint",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 24);

            migrationBuilder.AddColumn<long>(
                name: "TaxForLineId",
                schema: "acc",
                table: "EntryLines",
                type: "bigint",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 28);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "acc",
                table: "EntryLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 25);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxAccount",
                schema: "acc",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 24)
                .Annotation("Relational:DefaultConstraintName", "DF_Accounts_IsTaxAccount");

            migrationBuilder.CreateTable(
                name: "TaxCodes",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TaxCodes_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TaxCodes_Status"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TaxCodes_SortOrder"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TaxCodes_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TaxCodes_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodes", x => x.Id);
                    table.CheckConstraint("CK_TaxCodes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TaxCodes_Rate", "[Rate] >= 0 AND [Rate] <= 100");
                    table.CheckConstraint("CK_TaxCodes_SortOrder", "[SortOrder] >= 0");
                    table.CheckConstraint("CK_TaxCodes_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TaxCodes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxCodes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxCodes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxCodes_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaxCodes_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_CompanyId_Reference",
                schema: "acc",
                table: "EntryLines",
                columns: new[] { "CompanyId", "Reference" },
                filter: "[Reference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_TaxAccountId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxAccountId",
                filter: "[TaxAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_TaxCodeId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxCodeId",
                filter: "[TaxCodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxForLineId",
                filter: "[TaxForLineId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EntryLines_TaxAmount",
                schema: "acc",
                table: "EntryLines",
                sql: "[TaxAmount] IS NULL OR [TaxAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EntryLines_TaxCodeId_TaxRate",
                schema: "acc",
                table: "EntryLines",
                sql: "([TaxCodeId] IS NULL AND [TaxRate] IS NULL AND [TaxAmount] IS NULL AND [TaxAccountId] IS NULL AND [TaxForLineId] IS NULL) OR ([TaxCodeId] IS NOT NULL AND [TaxRate] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines",
                sql: "[TaxForLineId] IS NULL OR ([TaxAmount] IS NULL AND [TaxAccountId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EntryLines_TaxRate",
                schema: "acc",
                table: "EntryLines",
                sql: "[TaxRate] IS NULL OR ([TaxRate] >= 0 AND [TaxRate] <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_IsTaxAccount",
                schema: "acc",
                table: "Accounts",
                sql: "[IsTaxAccount] = 0 OR [IsControlAccount] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TaxCodes_CompanyId_Code",
                schema: "acc",
                table: "TaxCodes",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TaxCodes_PublicId",
                schema: "acc",
                table: "TaxCodes",
                column: "PublicId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_Accounts_TaxAccountId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxAccountId",
                principalSchema: "acc",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxForLineId",
                principalSchema: "acc",
                principalTable: "EntryLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_TaxCodes_TaxCodeId",
                schema: "acc",
                table: "EntryLines",
                column: "TaxCodeId",
                principalSchema: "acc",
                principalTable: "TaxCodes",
                principalColumn: "Id");

            // DT-254: the lines of existing entries take the entry's Reference (what a new line starts with), so statements, the entry view and
            // the search show old and new entries alike. Entries without a Reference (all automatic entries) keep empty line references.
            migrationBuilder.Sql(
                "UPDATE l SET l.[Reference] = e.[Reference] FROM [acc].[EntryLines] l INNER JOIN [acc].[JournalEntries] e ON e.[Id] = l.[JournalEntryId]" +
                " WHERE e.[Reference] IS NOT NULL AND l.[Reference] IS NULL;");

            // DT-257: the editable default tax codes (Jordanian VAT rates) for every existing company; new companies get them when created
            // (CompanyProvisioningService). Existing entries keep no tax code; no account is added or marked as a tax account (owner rule).
            migrationBuilder.Sql(
                "INSERT INTO [acc].[TaxCodes] ([TenantId], [CompanyId], [Code], [Rate], [NameAr], [NameEn], [Status], [SortOrder])" +
                " SELECT c.[TenantId], c.[Id], d.[Code], d.[Rate], d.[NameAr], d.[NameEn], 'Active', d.[SortOrder] FROM [core].[Companies] c" +
                " CROSS JOIN (VALUES" +
                " ('VAT_JO_0', 0, N'ضريبة المبيعات 0%', N'VAT 0%', 10), ('VAT_JO_1', 1, N'ضريبة المبيعات 1%', N'VAT 1%', 20)," +
                " ('VAT_JO_2', 2, N'ضريبة المبيعات 2%', N'VAT 2%', 30), ('VAT_JO_4', 4, N'ضريبة المبيعات 4%', N'VAT 4%', 40)," +
                " ('VAT_JO_5', 5, N'ضريبة المبيعات 5%', N'VAT 5%', 50), ('VAT_JO_7', 7, N'ضريبة المبيعات 7%', N'VAT 7%', 60)," +
                " ('VAT_JO_8', 8, N'ضريبة المبيعات 8%', N'VAT 8%', 70), ('VAT_JO_10', 10, N'ضريبة المبيعات 10%', N'VAT 10%', 80)," +
                " ('VAT_JO_16', 16, N'ضريبة المبيعات 16%', N'VAT 16%', 90)) AS d([Code], [Rate], [NameAr], [NameEn], [SortOrder])" +
                " WHERE c.[IsDeleted] = 0 AND NOT EXISTS (SELECT 1 FROM [acc].[TaxCodes] x WHERE x.[CompanyId] = c.[Id] AND x.[Code] = d.[Code] AND x.[IsDeleted] = 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DT-259 (same style as DT-231): once a journal line carries a tax code (taxed lines and their automatic tax lines), rolling back would
            // drop the tax lines' link and the codes they were posted with, so it is refused first, in the same transaction, with the reason.
            // Without tax in use the rollback drops the tax codes, the tax-account flags and the line references (the entry Reference stays).
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [acc].[EntryLines] WHERE [TaxCodeId] IS NOT NULL OR [TaxForLineId] IS NOT NULL)" +
                " THROW 50259, N'Rev6_JournalLineTax cannot be rolled back: journal lines with tax codes or automatic tax lines exist.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_EntryLines_Accounts_TaxAccountId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_EntryLines_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_EntryLines_TaxCodes_TaxCodeId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropTable(
                name: "TaxCodes",
                schema: "acc");

            migrationBuilder.DropIndex(
                name: "IX_EntryLines_CompanyId_Reference",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropIndex(
                name: "IX_EntryLines_TaxAccountId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropIndex(
                name: "IX_EntryLines_TaxCodeId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropIndex(
                name: "IX_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EntryLines_TaxAmount",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EntryLines_TaxCodeId_TaxRate",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EntryLines_TaxForLineId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EntryLines_TaxRate",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_IsTaxAccount",
                schema: "acc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Reference",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "TaxAccountId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "TaxCodeId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "TaxForLineId",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "acc",
                table: "EntryLines");

            migrationBuilder.DropColumn(
                name: "IsTaxAccount",
                schema: "acc",
                table: "Accounts")
                .Annotation("Relational:DefaultConstraintName", "DF_Accounts_IsTaxAccount");
        }
    }
}
