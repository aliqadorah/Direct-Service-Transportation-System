using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Revision 6 §B15 (owner findings 27 Oct 2026, DT-276 / DT-277): a driver settlement and a partner payment / repayment can settle the party's
    /// balance from manual / opening journal entries (PreviousBalance on the document, per account in trn.TRN_PreviousBalanceItems). A partner
    /// payment may be 0 when it only sets off what the partner owes against its trips. The partner's payments cover its trips (DT-271) with their
    /// trip part only: Amount − PreviousBalance (payment), Amount + PreviousBalance (repayment) — trn.TRN_PartnerPaymentBoundary changed.
    /// </summary>
    public partial class Rev6_PreviousBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_PartnerPayments_Amount",
                schema: "trn",
                table: "TRN_PartnerPayments");

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousBalance",
                schema: "trn",
                table: "TRN_Settlements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 31)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_PreviousBalance");

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousBalance",
                schema: "trn",
                table: "TRN_PartnerPayments",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 29)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_PreviousBalance");

            migrationBuilder.CreateTable(
                name: "TRN_PreviousBalanceItems",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PreviousBalanceItems_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    SettlementId = table.Column<long>(type: "bigint", nullable: true),
                    PartnerPaymentId = table.Column<long>(type: "bigint", nullable: true),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PreviousBalanceItems_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PreviousBalanceItems_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_PreviousBalanceItems", x => x.Id);
                    table.CheckConstraint("CK_TRN_PreviousBalanceItems_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_TRN_PreviousBalanceItems_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_PreviousBalanceItems_Document", "([SettlementId] IS NOT NULL AND [PartnerPaymentId] IS NULL) OR ([SettlementId] IS NULL AND [PartnerPaymentId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_TRN_PartnerPayments_PartnerPaymentId",
                        column: x => x.PartnerPaymentId,
                        principalSchema: "trn",
                        principalTable: "TRN_PartnerPayments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_TRN_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalSchema: "trn",
                        principalTable: "TRN_Settlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PreviousBalanceItems_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements",
                sql: "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal] + [PreviousBalance] + [RepaidAmount] + [CarriedForward]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_PartnerPayments_Amount",
                schema: "trn",
                table: "TRN_PartnerPayments",
                sql: "[Amount] > 0 OR ([Amount] = 0 AND [Kind] = 'Payment' AND [PreviousBalance] < 0)");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PreviousBalanceItems_AccountId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PreviousBalanceItems_CompanyId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PreviousBalanceItems_JournalEntryId_AccountId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                columns: new[] { "JournalEntryId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PreviousBalanceItems_PartnerPaymentId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                column: "PartnerPaymentId",
                filter: "[PartnerPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PreviousBalanceItems_SettlementId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                column: "SettlementId",
                filter: "[SettlementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_PreviousBalanceItems_PublicId",
                schema: "trn",
                table: "TRN_PreviousBalanceItems",
                column: "PublicId",
                unique: true);

            // DT-277: only the trip part of a payment / repayment covers trips (an offset of a manual balance is not a payment for trips).
            migrationBuilder.Sql("""
                ALTER VIEW trn.TRN_PartnerPaymentBoundary AS
                WITH pay AS (
                    SELECT PartnerId, SUM(CASE WHEN Kind = 'Repayment' THEN -(Amount + PreviousBalance) ELSE Amount - PreviousBalance END) AS Paid
                    FROM trn.TRN_PartnerPayments WHERE Status = 'Active' AND IsDeleted = 0 GROUP BY PartnerId),
                day AS (
                    SELECT TenantId, CompanyId, PartnerId, TripDate, SUM(PartnerCost) AS DayCost
                    FROM trn.TRN_Trips WHERE PartnerId IS NOT NULL AND Status = 'Active' AND IsDeleted = 0
                    GROUP BY TenantId, CompanyId, PartnerId, TripDate),
                run AS (
                    SELECT d.TenantId, d.CompanyId, d.PartnerId, d.TripDate, ISNULL(p.Paid, 0) AS Paid,
                        SUM(d.DayCost) OVER (PARTITION BY d.TenantId, d.CompanyId, d.PartnerId ORDER BY d.TripDate ROWS UNBOUNDED PRECEDING) AS CostToDay
                    FROM day d LEFT JOIN pay p ON p.PartnerId = d.PartnerId)
                SELECT TenantId, CompanyId, PartnerId, CAST(MAX(Paid) AS decimal(18,3)) AS Paid,
                    MIN(CASE WHEN CostToDay > Paid THEN TripDate END) AS BoundaryDate,
                    CAST(ISNULL(MAX(CASE WHEN CostToDay <= Paid THEN CostToDay END), 0) AS decimal(18,3)) AS CostBefore
                FROM run GROUP BY TenantId, CompanyId, PartnerId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback guard (as DT-231): documents that settled a previous balance cannot be held by the older schema.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM trn.TRN_PreviousBalanceItems) OR EXISTS (SELECT 1 FROM trn.TRN_Settlements WHERE PreviousBalance <> 0)
                   OR EXISTS (SELECT 1 FROM trn.TRN_PartnerPayments WHERE PreviousBalance <> 0)
                    THROW 50276, 'Rev6_PreviousBalances cannot be rolled back: settlements or partner payments settled manual / opening balances.', 1;
                """);
            migrationBuilder.Sql("""
                ALTER VIEW trn.TRN_PartnerPaymentBoundary AS
                WITH pay AS (
                    SELECT PartnerId, SUM(CASE WHEN Kind = 'Repayment' THEN -Amount ELSE Amount END) AS Paid
                    FROM trn.TRN_PartnerPayments WHERE Status = 'Active' AND IsDeleted = 0 GROUP BY PartnerId),
                day AS (
                    SELECT TenantId, CompanyId, PartnerId, TripDate, SUM(PartnerCost) AS DayCost
                    FROM trn.TRN_Trips WHERE PartnerId IS NOT NULL AND Status = 'Active' AND IsDeleted = 0
                    GROUP BY TenantId, CompanyId, PartnerId, TripDate),
                run AS (
                    SELECT d.TenantId, d.CompanyId, d.PartnerId, d.TripDate, ISNULL(p.Paid, 0) AS Paid,
                        SUM(d.DayCost) OVER (PARTITION BY d.TenantId, d.CompanyId, d.PartnerId ORDER BY d.TripDate ROWS UNBOUNDED PRECEDING) AS CostToDay
                    FROM day d LEFT JOIN pay p ON p.PartnerId = d.PartnerId)
                SELECT TenantId, CompanyId, PartnerId, CAST(MAX(Paid) AS decimal(18,3)) AS Paid,
                    MIN(CASE WHEN CostToDay > Paid THEN TripDate END) AS BoundaryDate,
                    CAST(ISNULL(MAX(CASE WHEN CostToDay <= Paid THEN CostToDay END), 0) AS decimal(18,3)) AS CostBefore
                FROM run GROUP BY TenantId, CompanyId, PartnerId;
                """);

            migrationBuilder.DropTable(
                name: "TRN_PreviousBalanceItems",
                schema: "trn");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_PartnerPayments_Amount",
                schema: "trn",
                table: "TRN_PartnerPayments");

            migrationBuilder.DropColumn(
                name: "PreviousBalance",
                schema: "trn",
                table: "TRN_Settlements")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_PreviousBalance");

            migrationBuilder.DropColumn(
                name: "PreviousBalance",
                schema: "trn",
                table: "TRN_PartnerPayments")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_PreviousBalance");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements",
                sql: "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal] + [RepaidAmount] + [CarriedForward]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_PartnerPayments_Amount",
                schema: "trn",
                table: "TRN_PartnerPayments",
                sql: "[Amount] > 0");
        }
    }
}
