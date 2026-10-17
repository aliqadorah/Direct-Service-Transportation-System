using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_SupplierBillDeductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Deduction",
                schema: "acc",
                table: "ApArDocuments",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 30)
                .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_Deduction");

            migrationBuilder.AddColumn<decimal>(
                name: "Deduction",
                schema: "acc",
                table: "ApArDocumentLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 19)
                .Annotation("Relational:DefaultConstraintName", "DF_ApArDocumentLines_Deduction");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApArDocuments_Deduction",
                schema: "acc",
                table: "ApArDocuments",
                sql: "[Deduction] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApArDocumentLines_Deduction",
                schema: "acc",
                table: "ApArDocumentLines",
                sql: "[Deduction] >= 0 AND [Deduction] <= [Amount]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ApArDocuments_Deduction",
                schema: "acc",
                table: "ApArDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApArDocumentLines_Deduction",
                schema: "acc",
                table: "ApArDocumentLines");

            migrationBuilder.DropColumn(
                name: "Deduction",
                schema: "acc",
                table: "ApArDocuments")
                .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_Deduction");

            migrationBuilder.DropColumn(
                name: "Deduction",
                schema: "acc",
                table: "ApArDocumentLines")
                .Annotation("Relational:DefaultConstraintName", "DF_ApArDocumentLines_Deduction");
        }
    }
}
