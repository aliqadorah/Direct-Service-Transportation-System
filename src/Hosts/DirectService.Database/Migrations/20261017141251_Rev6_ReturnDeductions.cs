using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_ReturnDeductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Deduction",
                schema: "acc",
                table: "ApArReturns",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 25)
                .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_Deduction");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApArReturns_Deduction",
                schema: "acc",
                table: "ApArReturns",
                sql: "[Deduction] >= 0 AND [Deduction] <= [Amount]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ApArReturns_Deduction",
                schema: "acc",
                table: "ApArReturns");

            migrationBuilder.DropColumn(
                name: "Deduction",
                schema: "acc",
                table: "ApArReturns")
                .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_Deduction");
        }
    }
}
