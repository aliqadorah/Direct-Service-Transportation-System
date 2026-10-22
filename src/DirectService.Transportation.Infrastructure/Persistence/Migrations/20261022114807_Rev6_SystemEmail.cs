using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rev6_SystemEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EmailOutbox_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    Template = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    LanguageCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EmailOutbox_Attempts"),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ClaimedUntil = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EmailOutbox_CreatedAt"),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ErrorKind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Mode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    HasSecretLink = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EmailOutbox_HasSecretLink"),
                    PayloadEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ResentFromId = table.Column<long>(type: "bigint", nullable: true),
                    ResentAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                    table.CheckConstraint("CK_EmailOutbox_Attempts", "[Attempts] >= 0");
                    table.CheckConstraint("CK_EmailOutbox_FailedAt", "[Status] <> 'Failed' OR [FailedAt] IS NOT NULL");
                    table.CheckConstraint("CK_EmailOutbox_LanguageCode", "[LanguageCode] IN ('ar','en')");
                    table.CheckConstraint("CK_EmailOutbox_PayloadEncrypted", "[PayloadEncrypted] IS NULL OR [Status] IN ('Pending','Sending') OR ([Status] = 'Failed' AND [HasSecretLink] = 0)");
                    table.CheckConstraint("CK_EmailOutbox_SentAt", "[Status] <> 'Sent' OR [SentAt] IS NOT NULL");
                    table.CheckConstraint("CK_EmailOutbox_Status", "[Status] IN ('Pending','Sending','Sent','Failed','Suppressed')");
                    table.ForeignKey(
                        name: "FK_EmailOutbox_EmailOutbox_ResentFromId",
                        column: x => x.ResentFromId,
                        principalSchema: "core",
                        principalTable: "EmailOutbox",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmailOutbox_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SystemEmailSettings",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SystemEmailSettings_PublicId"),
                    State = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Mode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    EntraTenantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntraClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientSecretEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SecretSavedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    SecretExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SenderMailbox = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReplyTo = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastTestAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastTestOk = table.Column<bool>(type: "bit", nullable: true),
                    LastTestError = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ExpiryWarnedDays = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SystemEmailSettings_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SystemEmailSettings_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEmailSettings", x => x.Id);
                    table.CheckConstraint("CK_SystemEmailSettings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_SystemEmailSettings_ExpiryWarnedDays", "[ExpiryWarnedDays] IS NULL OR [ExpiryWarnedDays] > 0");
                    table.CheckConstraint("CK_SystemEmailSettings_Microsoft365", "[Mode] <> 'Microsoft365' OR ([EntraTenantId] IS NOT NULL AND [EntraClientId] IS NOT NULL AND [ClientSecretEncrypted] IS NOT NULL AND [SenderMailbox] IS NOT NULL)");
                    table.CheckConstraint("CK_SystemEmailSettings_Mode", "[Mode] IN ('Development','Microsoft365')");
                    table.CheckConstraint("CK_SystemEmailSettings_State", "[State] IN ('Active','Pending','Retired')");
                    table.ForeignKey(
                        name: "FK_SystemEmailSettings_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SystemEmailSettings_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SystemEmailSettings_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_CreatedAt",
                schema: "core",
                table: "EmailOutbox",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Recipient_Template_CreatedAt",
                schema: "core",
                table: "EmailOutbox",
                columns: new[] { "Recipient", "Template", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_SentAt",
                schema: "core",
                table: "EmailOutbox",
                column: "SentAt",
                filter: "[SentAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_NextAttemptAt",
                schema: "core",
                table: "EmailOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "UX_EmailOutbox_PublicId",
                schema: "core",
                table: "EmailOutbox",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SystemEmailSettings_Active",
                schema: "core",
                table: "SystemEmailSettings",
                column: "State",
                unique: true,
                filter: "[State] = 'Active' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_SystemEmailSettings_Pending",
                schema: "core",
                table: "SystemEmailSettings",
                column: "State",
                unique: true,
                filter: "[State] = 'Pending' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_SystemEmailSettings_PublicId",
                schema: "core",
                table: "SystemEmailSettings",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DT-231 pattern: refuse first, with the reason, rather than drop saved system email settings (the encrypted secret) or emails still
            // waiting to be sent; an empty outbox log and no settings roll back freely.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [core].[SystemEmailSettings]) OR EXISTS (SELECT 1 FROM [core].[EmailOutbox] WHERE [Status] IN ('Pending','Sending'))" +
                " THROW 50233, N'Rev6_SystemEmail cannot be rolled back: system email settings are saved or emails are waiting to be sent.', 1;");

            migrationBuilder.DropTable(
                name: "EmailOutbox",
                schema: "core");

            migrationBuilder.DropTable(
                name: "SystemEmailSettings",
                schema: "core");
        }
    }
}
