using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_ReceiptAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TRN_ReceiptAllocations",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_ReceiptAllocations_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_ReceiptAllocations_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_ReceiptAllocations_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_ReceiptAllocations", x => x.Id);
                    table.CheckConstraint("CK_TRN_ReceiptAllocations_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_ReceiptAllocations_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_TRN_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalSchema: "trn",
                        principalTable: "TRN_Receipts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_ReceiptAllocations_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_ReceiptAllocations_InvoiceId",
                schema: "trn",
                table: "TRN_ReceiptAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_ReceiptAllocations_PublicId",
                schema: "trn",
                table: "TRN_ReceiptAllocations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_ReceiptAllocations_ReceiptId_InvoiceId",
                schema: "trn",
                table: "TRN_ReceiptAllocations",
                columns: new[] { "ReceiptId", "InvoiceId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // Revision 6 data: every existing receipt against one invoice becomes one allocation of its full amount (void receipts too, as the
            // record of what they had paid); the invoices' paid amounts are unchanged.
            migrationBuilder.Sql("""
                INSERT INTO [trn].[TRN_ReceiptAllocations] ([PublicId], [TenantId], [CompanyId], [ReceiptId], [InvoiceId], [Amount], [CreatedAt], [CreatedBy], [IsDeleted])
                SELECT NEWID(), r.[TenantId], r.[CompanyId], r.[Id], r.[InvoiceId], r.[Amount], r.[CreatedAt], r.[CreatedBy], 0
                FROM [trn].[TRN_Receipts] r
                WHERE r.[InvoiceId] IS NOT NULL AND r.[IsDeleted] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRN_ReceiptAllocations",
                schema: "trn");
        }
    }
}
