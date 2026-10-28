using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_ReceiptNoSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptNoSearch",
                schema: "trn",
                table: "TRN_Trips",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                computedColumnSql: "[ReceiptNoKey] COLLATE Latin1_General_100_BIN2",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_ReceiptNoSearch",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "CompanyId", "ReceiptNoSearch" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRN_Trips_ReceiptNoSearch",
                schema: "trn",
                table: "TRN_Trips");

            migrationBuilder.DropColumn(
                name: "ReceiptNoSearch",
                schema: "trn",
                table: "TRN_Trips");
        }
    }
}
