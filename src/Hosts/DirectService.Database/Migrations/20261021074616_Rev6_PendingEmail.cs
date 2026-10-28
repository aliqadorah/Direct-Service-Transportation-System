using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Database.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_PendingEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingEmail",
                schema: "core",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 32);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingEmailAt",
                schema: "core",
                table: "Users",
                type: "datetime2",
                nullable: true)
                .Annotation("Relational:ColumnOrder", 33);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PendingEmail_PendingEmailAt",
                schema: "core",
                table: "Users",
                sql: "([PendingEmail] IS NULL AND [PendingEmailAt] IS NULL) OR ([PendingEmail] IS NOT NULL AND [PendingEmailAt] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PendingEmail_PendingEmailAt",
                schema: "core",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PendingEmail",
                schema: "core",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PendingEmailAt",
                schema: "core",
                table: "Users");
        }
    }
}
