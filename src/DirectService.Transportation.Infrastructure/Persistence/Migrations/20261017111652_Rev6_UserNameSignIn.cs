using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_UserNameSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Users_Email",
                schema: "core",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Users_NormalizedEmail",
                schema: "core",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                schema: "core",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "core",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                schema: "core",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 30)
                .Annotation("Relational:DefaultConstraintName", "DF_Users_MustChangePassword");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                schema: "core",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 29);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                schema: "core",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("Relational:ColumnOrder", 28);

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                schema: "core",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0 AND [Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_NormalizedEmail",
                schema: "core",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[IsDeleted] = 0 AND [NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_NormalizedUserName",
                schema: "core",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[IsDeleted] = 0 AND [NormalizedUserName] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Email_UserName",
                schema: "core",
                table: "Users",
                sql: "[Email] IS NOT NULL OR [UserName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Users_Email",
                schema: "core",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Users_NormalizedEmail",
                schema: "core",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Users_NormalizedUserName",
                schema: "core",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Email_UserName",
                schema: "core",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                schema: "core",
                table: "Users")
                .Annotation("Relational:DefaultConstraintName", "DF_Users_MustChangePassword");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                schema: "core",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserName",
                schema: "core",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                schema: "core",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "core",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                schema: "core",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Users_NormalizedEmail",
                schema: "core",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
