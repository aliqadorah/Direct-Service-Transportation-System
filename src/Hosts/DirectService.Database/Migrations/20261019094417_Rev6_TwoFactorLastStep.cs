using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_TwoFactorLastStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TwoFactorLastStep",
                schema: "core",
                table: "Users",
                type: "bigint",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 31);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorLastStep",
                schema: "core",
                table: "Users");
        }
    }
}
