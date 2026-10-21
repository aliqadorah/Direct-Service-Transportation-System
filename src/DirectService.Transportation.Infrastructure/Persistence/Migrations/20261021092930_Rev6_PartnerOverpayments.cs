using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_PartnerOverpayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeductedAmount",
                schema: "trn",
                table: "TRN_PartnerPayments",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValueSql: "0")
                .Annotation("Relational:ColumnOrder", 28)
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_DeductedAmount");

            migrationBuilder.CreateTable(
                name: "TRN_PartnerOverpayments",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerOverpayments_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    PartnerId = table.Column<long>(type: "bigint", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: false),
                    PartnerPaymentId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Handling = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerOverpayments_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerOverpayments_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_PartnerOverpayments", x => x.Id);
                    table.CheckConstraint("CK_TRN_PartnerOverpayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_PartnerOverpayments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_PartnerOverpayments_Handling", "[Handling] IN ('Return','Deduct','NotPaid')");
                    table.CheckConstraint("CK_TRN_PartnerOverpayments_PartnerPaymentId", "([Handling] = 'NotPaid' AND [PartnerPaymentId] IS NOT NULL) OR ([Handling] <> 'NotPaid' AND [PartnerPaymentId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_TRN_PartnerPayments_PartnerPaymentId",
                        column: x => x.PartnerPaymentId,
                        principalSchema: "trn",
                        principalTable: "TRN_PartnerPayments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_TRN_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Partners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_TRN_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "trn",
                        principalTable: "TRN_Trips",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerOverpayments_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRN_PartnerPayments_DeductedAmount",
                schema: "trn",
                table: "TRN_PartnerPayments",
                sql: "[DeductedAmount] >= 0 AND ([Kind] = 'Payment' OR [DeductedAmount] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerOverpayments_CompanyId",
                schema: "trn",
                table: "TRN_PartnerOverpayments",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerOverpayments_PartnerId",
                schema: "trn",
                table: "TRN_PartnerOverpayments",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerOverpayments_PartnerPaymentId",
                schema: "trn",
                table: "TRN_PartnerOverpayments",
                column: "PartnerPaymentId",
                filter: "[PartnerPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerOverpayments_TripId",
                schema: "trn",
                table: "TRN_PartnerOverpayments",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_PartnerOverpayments_PublicId",
                schema: "trn",
                table: "TRN_PartnerOverpayments",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRN_PartnerOverpayments",
                schema: "trn");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRN_PartnerPayments_DeductedAmount",
                schema: "trn",
                table: "TRN_PartnerPayments");

            migrationBuilder.DropColumn(
                name: "DeductedAmount",
                schema: "trn",
                table: "TRN_PartnerPayments")
                .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_DeductedAmount");
        }
    }
}
