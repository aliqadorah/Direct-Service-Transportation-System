using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_SettlementRepayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Advances_CashBoxId_BankAccountId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Advances_PaymentMethod",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.AddColumn<decimal>(
                name: "CarriedForward",
                schema: "trn",
                table: "TRN_Settlements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 30)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_CarriedForward");

            migrationBuilder.AddColumn<decimal>(
                name: "RepaidAmount",
                schema: "trn",
                table: "TRN_Settlements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 29)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_RepaidAmount");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "trn",
                table: "TRN_PartnerPayments",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValueSql: "'Payment'")
                .Annotation("Relational:ColumnOrder", 27)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_Kind");

            migrationBuilder.AddColumn<long>(
                name: "CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances",
                type: "bigint",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 28);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements",
                sql: "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal] + [RepaidAmount] + [CarriedForward]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Settlements_RepaidAmount_CarriedForward",
                schema: "trn",
                table: "TRN_Settlements",
                sql: "[RepaidAmount] >= 0 AND [CarriedForward] >= 0 AND ([NetPayable] = 0 OR ([RepaidAmount] = 0 AND [CarriedForward] = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_PartnerPayments_Kind",
                schema: "trn",
                table: "TRN_PartnerPayments",
                sql: "[Kind] IN ('Payment','Repayment')");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances",
                column: "CarriedFromSettlementId",
                filter: "[CarriedFromSettlementId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Advances_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances",
                sql: "([PaymentMethod] = 'Carried' AND [CarriedFromSettlementId] IS NOT NULL) OR ([PaymentMethod] <> 'Carried' AND [CarriedFromSettlementId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Advances_CashBoxId_BankAccountId",
                schema: "trn",
                table: "TRN_Advances",
                sql: "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL) OR ([PaymentMethod] = 'Carried' AND [CashBoxId] IS NULL AND [BankAccountId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Advances_PaymentMethod",
                schema: "trn",
                table: "TRN_Advances",
                sql: "[PaymentMethod] IN ('Cash','Bank','CliQ','Carried')");

            migrationBuilder.AddForeignKey(
                name: "FK_TRN_Advances_TRN_Settlements_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances",
                column: "CarriedFromSettlementId",
                principalSchema: "trn",
                principalTable: "TRN_Settlements",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TRN_Advances_TRN_Settlements_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Settlements_RepaidAmount_CarriedForward",
                schema: "trn",
                table: "TRN_Settlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_PartnerPayments_Kind",
                schema: "trn",
                table: "TRN_PartnerPayments");

            migrationBuilder.DropIndex(
                name: "IX_TRN_Advances_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Advances_CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Advances_CashBoxId_BankAccountId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_Advances_PaymentMethod",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.DropColumn(
                name: "CarriedForward",
                schema: "trn",
                table: "TRN_Settlements")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_CarriedForward");

            migrationBuilder.DropColumn(
                name: "RepaidAmount",
                schema: "trn",
                table: "TRN_Settlements")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_RepaidAmount");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "trn",
                table: "TRN_PartnerPayments")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_Kind");

            migrationBuilder.DropColumn(
                name: "CarriedFromSettlementId",
                schema: "trn",
                table: "TRN_Advances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Settlements_NetPayable",
                schema: "trn",
                table: "TRN_Settlements",
                sql: "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Advances_CashBoxId_BankAccountId",
                schema: "trn",
                table: "TRN_Advances",
                sql: "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_Advances_PaymentMethod",
                schema: "trn",
                table: "TRN_Advances",
                sql: "[PaymentMethod] IN ('Cash','Bank','CliQ')");
        }
    }
}
