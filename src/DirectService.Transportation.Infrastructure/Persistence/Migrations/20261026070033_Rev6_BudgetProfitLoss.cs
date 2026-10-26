using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_BudgetProfitLoss : Migration
    {
        /// <summary>Data step of Up (the API tests also run it on rows they make).</summary>
        public const string CostOfSalesSql =
            "UPDATE [acc].[Accounts] SET [ProfitLossGroup] = N'CostOfSales' WHERE [Code] = N'51' AND [AccountType] = N'Expense'" +
            " AND [ProfitLossGroup] IS NULL AND [IsDeleted] = 0 AND ([NameAr] = N'تكاليف النقل المباشرة' OR [NameEn] = N'Direct transport costs');";

        /// <summary>Data step of Up (the API tests also run it on rows they make).</summary>
        public const string TaxCodeNamesSql =
            "UPDATE t SET t.[NameAr] = d.[NewName] FROM [acc].[TaxCodes] t INNER JOIN (VALUES (N'ضريبة المبيعات 0%', N'ضريبة المبيعات العامة 0%'), (N'ضريبة المبيعات 1%', N'ضريبة المبيعات العامة 1%'), (N'ضريبة المبيعات 2%', N'ضريبة المبيعات العامة 2%'), (N'ضريبة المبيعات 4%', N'ضريبة المبيعات العامة 4%'), (N'ضريبة المبيعات 5%', N'ضريبة المبيعات العامة 5%'), (N'ضريبة المبيعات 7%', N'ضريبة المبيعات العامة 7%'), (N'ضريبة المبيعات 8%', N'ضريبة المبيعات العامة 8%'), (N'ضريبة المبيعات 10%', N'ضريبة المبيعات العامة 10%'), (N'ضريبة المبيعات 16%', N'ضريبة المبيعات العامة 16%'))" +
            " AS d([OldName], [NewName]) ON t.[NameAr] = d.[OldName] WHERE t.[IsDeleted] = 0;";

        /// <summary>Data step of Up (the API tests also run it on rows they make).</summary>
        public const string VatExpensesNameSql =
            "UPDATE [acc].[Accounts] SET [NameAr] = N'ضريبة مبيعات غير قابلة للخصم' WHERE [NameAr] = N'مصروف ضريبة المبيعات غير القابلة للاسترداد'" +
            " AND [AccountType] = N'Expense' AND [IsDeleted] = 0;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfitLossGroup",
                schema: "acc",
                table: "Accounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 25);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_ProfitLossGroup",
                schema: "acc",
                table: "Accounts",
                sql: "[ProfitLossGroup] IS NULL OR ([AccountType] = N'Revenue' AND [ProfitLossGroup] IN (N'Sales', N'OtherIncome')) OR ([AccountType] = N'Expense' AND [ProfitLossGroup] IN (N'CostOfSales', N'Operating', N'OtherExpense'))");

            // DT-264: the template's direct transport costs (51) are the cost of sales — only where the account is still the template's own
            // (same code, type and one of its names) and has no section yet; every other account keeps the default of its type.
            migrationBuilder.Sql(CostOfSalesSql);

            // DT-263 (owner decision 26 Oct 2026): new Arabic names — only texts that are still the old default (a name the user changed stays).
            migrationBuilder.Sql(TaxCodeNamesSql);
            migrationBuilder.Sql(VatExpensesNameSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the earlier default Arabic names where the new defaults are still in place.
            migrationBuilder.Sql(
                "UPDATE t SET t.[NameAr] = d.[OldName] FROM [acc].[TaxCodes] t INNER JOIN (VALUES (N'ضريبة المبيعات 0%', N'ضريبة المبيعات العامة 0%'), (N'ضريبة المبيعات 1%', N'ضريبة المبيعات العامة 1%'), (N'ضريبة المبيعات 2%', N'ضريبة المبيعات العامة 2%'), (N'ضريبة المبيعات 4%', N'ضريبة المبيعات العامة 4%'), (N'ضريبة المبيعات 5%', N'ضريبة المبيعات العامة 5%'), (N'ضريبة المبيعات 7%', N'ضريبة المبيعات العامة 7%'), (N'ضريبة المبيعات 8%', N'ضريبة المبيعات العامة 8%'), (N'ضريبة المبيعات 10%', N'ضريبة المبيعات العامة 10%'), (N'ضريبة المبيعات 16%', N'ضريبة المبيعات العامة 16%'))" +
                " AS d([OldName], [NewName]) ON t.[NameAr] = d.[NewName] WHERE t.[IsDeleted] = 0;");
            migrationBuilder.Sql(
                "UPDATE [acc].[Accounts] SET [NameAr] = N'مصروف ضريبة المبيعات غير القابلة للاسترداد' WHERE [NameAr] = N'ضريبة مبيعات غير قابلة للخصم'" +
                " AND [AccountType] = N'Expense' AND [IsDeleted] = 0;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_ProfitLossGroup",
                schema: "acc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ProfitLossGroup",
                schema: "acc",
                table: "Accounts");
        }
    }
}
