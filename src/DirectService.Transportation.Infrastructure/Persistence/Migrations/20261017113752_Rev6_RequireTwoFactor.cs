using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_RequireTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireTwoFactor",
                schema: "core",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 24)
                .Annotation("Relational:DefaultConstraintName", "DF_Tenants_RequireTwoFactor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireTwoFactor",
                schema: "core",
                table: "Tenants")
                .Annotation("Relational:DefaultConstraintName", "DF_Tenants_RequireTwoFactor");
        }
    }
}
