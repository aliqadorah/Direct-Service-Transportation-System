using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DirectService.Transportation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMasterDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "acc");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "cfg");

            migrationBuilder.EnsureSchema(
                name: "trn");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    IsPostable = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_IsPostable"),
                    OpeningDebit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_OpeningDebit"),
                    OpeningCredit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_OpeningCredit"),
                    IsControlAccount = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_IsControlAccount"),
                    ControlPartyType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Accounts_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_AccountType", "[AccountType] IN ('Asset','Liability','Equity','Revenue','Expense')");
                    table.CheckConstraint("CK_Accounts_ControlPartyType", "[ControlPartyType] IN ('Customer','Driver','Partner','Supplier')");
                    table.CheckConstraint("CK_Accounts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Accounts_OpeningCredit", "[OpeningCredit] >= 0");
                    table.CheckConstraint("CK_Accounts_OpeningDebit", "[OpeningDebit] >= 0");
                    table.CheckConstraint("CK_Accounts_OpeningDebit_OpeningCredit", "NOT ([OpeningDebit] > 0 AND [OpeningCredit] > 0)");
                    table.CheckConstraint("CK_Accounts_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Announcements",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Announcements_PublicId"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Announcements_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Announcements_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                    table.CheckConstraint("CK_Announcements_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Announcements_EndsAt", "[EndsAt] > [StartsAt]");
                });

            migrationBuilder.CreateTable(
                name: "ApArDocumentLines",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocumentLines_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CostCentreId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocumentLines_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocumentLines_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArDocumentLines", x => x.Id);
                    table.CheckConstraint("CK_ApArDocumentLines_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_ApArDocumentLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArDocumentLines_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ApArDocumentLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApArDocuments",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentKind = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    PartyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_Tax"),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_PaidAmount"),
                    ReturnedAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_ReturnedAmount"),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Posted'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArDocuments_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArDocuments", x => x.Id);
                    table.CheckConstraint("CK_ApArDocuments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArDocuments_DocumentKind", "[DocumentKind] IN ('SupplierBill','Receivable')");
                    table.CheckConstraint("CK_ApArDocuments_DueDate", "[DueDate] >= [DocumentDate]");
                    table.CheckConstraint("CK_ApArDocuments_PaidAmount", "[PaidAmount] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_RemainingAmount", "[RemainingAmount] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_ReturnedAmount", "[ReturnedAmount] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_Status", "[Status] IN ('Posted','Void')");
                    table.CheckConstraint("CK_ApArDocuments_Subtotal", "[Subtotal] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_Tax", "[Tax] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_Total", "[Total] >= 0");
                    table.CheckConstraint("CK_ApArDocuments_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "ApArEInvoiceSubmissions",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArEInvoiceSubmissions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ApArDocumentId = table.Column<long>(type: "bigint", nullable: true),
                    ApArReturnId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValueSql: "'Queued'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArEInvoiceSubmissions_Status"),
                    Icv = table.Column<long>(type: "bigint", nullable: true),
                    UniqueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SystemNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    QrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArEInvoiceSubmissions_Attempts"),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArEInvoiceSubmissions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArEInvoiceSubmissions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArEInvoiceSubmissions", x => x.Id);
                    table.CheckConstraint("CK_ApArEInvoiceSubmissions_Attempts", "[Attempts] >= 0");
                    table.CheckConstraint("CK_ApArEInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArEInvoiceSubmissions_DocumentType", "[DocumentType] IN ('Invoice','CreditNote')");
                    table.CheckConstraint("CK_ApArEInvoiceSubmissions_Status", "[Status] IN ('Queued','Submitted','Rejected','NotRequired')");
                    table.ForeignKey(
                        name: "FK_ApArEInvoiceSubmissions_ApArDocuments_ApArDocumentId",
                        column: x => x.ApArDocumentId,
                        principalSchema: "acc",
                        principalTable: "ApArDocuments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApArParties",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArParties_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    PartyRole = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ControlAccountId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArParties_PaymentTermsDays"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArParties_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArParties_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArParties_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArParties", x => x.Id);
                    table.CheckConstraint("CK_ApArParties_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArParties_PartyRole", "[PartyRole] IN ('Supplier','Customer')");
                    table.CheckConstraint("CK_ApArParties_PaymentTermsDays", "[PaymentTermsDays] >= 0");
                    table.CheckConstraint("CK_ApArParties_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_ApArParties_Accounts_ControlAccountId",
                        column: x => x.ControlAccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApArPayments",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArPayments_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CashBoxId = table.Column<long>(type: "bigint", nullable: true),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentsCount = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArPayments_AttachmentsCount"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArPayments_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArPayments_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArPayments_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArPayments", x => x.Id);
                    table.CheckConstraint("CK_ApArPayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_ApArPayments_AttachmentsCount", "[AttachmentsCount] >= 0");
                    table.CheckConstraint("CK_ApArPayments_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
                    table.CheckConstraint("CK_ApArPayments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArPayments_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_ApArPayments_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_ApArPayments_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ApArPayments_ApArDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "acc",
                        principalTable: "ApArDocuments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApArReturns",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_Tax"),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Posted'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ApArReturns_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApArReturns", x => x.Id);
                    table.CheckConstraint("CK_ApArReturns_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_ApArReturns_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ApArReturns_Status", "[Status] IN ('Posted','Void')");
                    table.CheckConstraint("CK_ApArReturns_Tax", "[Tax] >= 0");
                    table.CheckConstraint("CK_ApArReturns_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ApArReturns_ApArDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "acc",
                        principalTable: "ApArDocuments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    CompanyId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_AuditLogs_OccurredAt"),
                    Action = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: true),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_NewValues", "ISJSON([NewValues]) = 1");
                    table.CheckConstraint("CK_AuditLogs_OldValues", "ISJSON([OldValues]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_BankAccounts_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false, defaultValueSql: "'JOD'")
                        .Annotation("Relational:DefaultConstraintName", "DF_BankAccounts_CurrencyCode"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_BankAccounts_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_BankAccounts_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_BankAccounts_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.CheckConstraint("CK_BankAccounts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_BankAccounts_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_BankAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CashBoxes",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CashBoxes_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_CashBoxes_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CashBoxes_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CashBoxes_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBoxes", x => x.Id);
                    table.CheckConstraint("CK_CashBoxes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CashBoxes_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_CashBoxes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TaxNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    CommercialRegNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LogoFile = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false, defaultValueSql: "'JO'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_CountryCode"),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false, defaultValueSql: "'JOD'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_CurrencyCode"),
                    CurrencyDecimals = table.Column<byte>(type: "tinyint", nullable: false, defaultValueSql: "3")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_CurrencyDecimals"),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValueSql: "16.00")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_TaxRate"),
                    TaxInclusive = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_TaxInclusive"),
                    TimeZone = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false, defaultValueSql: "'Asia/Amman'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_TimeZone"),
                    MigrationCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Companies_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.CheckConstraint("CK_Companies_CurrencyDecimals", "[CurrencyDecimals] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_Companies_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Companies_Status", "[Status] IN ('Active','Archived')");
                    table.CheckConstraint("CK_Companies_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CompanySettings_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    SettingKey = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CompanySettings_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CompanySettings_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.CheckConstraint("CK_CompanySettings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CompanySettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CostCentres",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CostCentres_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_CostCentres_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CostCentres_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CostCentres_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCentres", x => x.Id);
                    table.CheckConstraint("CK_CostCentres_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CostCentres_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_CostCentres_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    FieldKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FieldType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HelpText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_IsRequired"),
                    ShowAsColumn = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_ShowAsColumn"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_SortOrder"),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_IsHidden"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_CustomFieldDefinitions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinitions", x => x.Id);
                    table.CheckConstraint("CK_CustomFieldDefinitions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CustomFieldDefinitions_FieldType", "[FieldType] IN ('Text','Number','Date','List','Checkbox','LongText')");
                    table.CheckConstraint("CK_CustomFieldDefinitions_Options", "ISJSON([Options]) = 1");
                    table.ForeignKey(
                        name: "FK_CustomFieldDefinitions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DashboardPins",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SavedReportId = table.Column<long>(type: "bigint", nullable: false),
                    Measure = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    WarningThreshold = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CriticalThreshold = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_SortOrder"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_DashboardPins_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardPins", x => x.Id);
                    table.CheckConstraint("CK_DashboardPins_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DashboardPins_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EInvoiceSettings",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_IsEnabled"),
                    Connector = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false, defaultValueSql: "'JoFotara'")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_Connector"),
                    SellerTaxNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    RegistrationType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    IncomeSourceSequence = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    ClientIdEncrypted = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    SecretKeyEncrypted = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    Environment = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Test'")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_Environment"),
                    IcvCounter = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_IcvCounter"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EInvoiceSettings_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoiceSettings", x => x.Id);
                    table.CheckConstraint("CK_EInvoiceSettings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_EInvoiceSettings_Environment", "[Environment] IN ('Test','Production')");
                    table.ForeignKey(
                        name: "FK_EInvoiceSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EntryLines",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EntryLines_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EntryLines_Debit"),
                    Credit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EntryLines_Credit"),
                    CostCentreId = table.Column<long>(type: "bigint", nullable: true),
                    PartyModule = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    PartyType = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    PartyId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_EntryLines_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_EntryLines_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryLines", x => x.Id);
                    table.CheckConstraint("CK_EntryLines_Credit", "[Credit] >= 0");
                    table.CheckConstraint("CK_EntryLines_Debit", "[Debit] >= 0");
                    table.CheckConstraint("CK_EntryLines_Debit_Credit", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
                    table.CheckConstraint("CK_EntryLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_EntryLines_PartyModule", "[PartyModule] IN ('TRN','ACC')");
                    table.ForeignKey(
                        name: "FK_EntryLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntryLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EntryLines_CostCentres_CostCentreId",
                        column: x => x.CostCentreId,
                        principalSchema: "acc",
                        principalTable: "CostCentres",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ImportBatches_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Mode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Strict'")
                        .Annotation("Relational:DefaultConstraintName", "DF_ImportBatches_Mode"),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ImportedRows = table.Column<int>(type: "int", nullable: false),
                    RejectedRows = table.Column<int>(type: "int", nullable: false),
                    ImportedBy = table.Column<long>(type: "bigint", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ImportBatches_ImportedAt"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_ImportBatches_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_ImportBatches_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatches_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_ImportBatches_ImportedRows", "[ImportedRows] >= 0");
                    table.CheckConstraint("CK_ImportBatches_Mode", "[Mode] IN ('Strict','Tolerant')");
                    table.CheckConstraint("CK_ImportBatches_TotalRows", "[TotalRows] >= 0");
                    table.ForeignKey(
                        name: "FK_ImportBatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Invitations_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvitedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Invitations_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Invitations_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.CheckConstraint("CK_Invitations_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Invitations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalEntries_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodMonth = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceModule = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    SourceId = table.Column<long>(type: "bigint", nullable: true),
                    IsManual = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalEntries_IsManual"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Posted'")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalEntries_Status"),
                    ReversesEntryId = table.Column<long>(type: "bigint", nullable: true),
                    ReversedByEntryId = table.Column<long>(type: "bigint", nullable: true),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ExportBatchId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalEntries_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalEntries_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.CheckConstraint("CK_JournalEntries_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_JournalEntries_SourceModule", "[SourceModule] IN ('TRN','ACC')");
                    table.CheckConstraint("CK_JournalEntries_Status", "[Status] IN ('Posted','Reversed','Void')");
                    table.CheckConstraint("CK_JournalEntries_TotalDebit_TotalCredit", "[TotalDebit] = [TotalCredit]");
                    table.CheckConstraint("CK_JournalEntries_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_JournalEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_ReversedByEntryId",
                        column: x => x.ReversedByEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalExportBatches",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalExportBatches_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    BatchNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PeriodFrom = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    PeriodTo = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    Format = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ExportedBy = table.Column<long>(type: "bigint", nullable: false),
                    ExportedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalExportBatches_ExportedAt"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalExportBatches_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_JournalExportBatches_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalExportBatches", x => x.Id);
                    table.CheckConstraint("CK_JournalExportBatches_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_JournalExportBatches_EntryCount", "[EntryCount] >= 0");
                    table.CheckConstraint("CK_JournalExportBatches_Format", "[Format] IN ('JSON','CSV')");
                    table.CheckConstraint("CK_JournalExportBatches_TotalDebit", "[TotalDebit] >= 0");
                    table.ForeignKey(
                        name: "FK_JournalExportBatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceLogs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    RecordCounts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_MaintenanceLogs_OccurredAt")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceLogs", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceLogs_RecordCounts", "ISJSON([RecordCounts]) = 1");
                    table.ForeignKey(
                        name: "FK_MaintenanceLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Notifications_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: true),
                    NotificationType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Notifications_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Notifications_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Notifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NumberSequences",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_NumberSequences_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Digits = table.Column<byte>(type: "tinyint", nullable: false, defaultValueSql: "5")
                        .Annotation("Relational:DefaultConstraintName", "DF_NumberSequences_Digits"),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_NumberSequences_NextNumber"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_NumberSequences_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_NumberSequences_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSequences", x => x.Id);
                    table.CheckConstraint("CK_NumberSequences_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_NumberSequences_Digits", "[Digits] BETWEEN 3 AND 10");
                    table.ForeignKey(
                        name: "FK_NumberSequences_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PendingPostings",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_PendingPostings_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceModule = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_PendingPostings_Attempts"),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_PendingPostings_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_PendingPostings_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPostings", x => x.Id);
                    table.CheckConstraint("CK_PendingPostings_Attempts", "[Attempts] >= 0");
                    table.CheckConstraint("CK_PendingPostings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_PendingPostings_SourceModule", "[SourceModule] IN ('TRN','ACC')");
                    table.ForeignKey(
                        name: "FK_PendingPostings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Periods",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Periods_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodMonth = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Open'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Periods_Status"),
                    ClosedBy = table.Column<long>(type: "bigint", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Periods_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Periods_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Periods", x => x.Id);
                    table.CheckConstraint("CK_Periods_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Periods_Status", "[Status] IN ('Open','Closed')");
                    table.ForeignKey(
                        name: "FK_Periods_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Permissions_PublicId"),
                    PermissionKey = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    GroupName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescriptionEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Permissions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Permissions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                    table.CheckConstraint("CK_Permissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Plans_PublicId"),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BillingCycle = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Monthly'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Plans_BillingCycle"),
                    MaxCompanies = table.Column<int>(type: "int", nullable: false),
                    MaxActiveUsers = table.Column<int>(type: "int", nullable: false),
                    MaxTripsPerMonth = table.Column<int>(type: "int", nullable: false),
                    MaxStorageMb = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_Plans_IsActive"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Plans_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Plans_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.CheckConstraint("CK_Plans_BillingCycle", "[BillingCycle] IN ('Monthly','Annual')");
                    table.CheckConstraint("CK_Plans_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Plans_MaxActiveUsers", "[MaxActiveUsers] > 0");
                    table.CheckConstraint("CK_Plans_MaxCompanies", "[MaxCompanies] > 0");
                    table.CheckConstraint("CK_Plans_MaxStorageMb", "[MaxStorageMb] > 0");
                    table.CheckConstraint("CK_Plans_MaxTripsPerMonth", "[MaxTripsPerMonth] > 0");
                    table.CheckConstraint("CK_Plans_Price", "[Price] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PostingKeyMappings",
                schema: "acc",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_PostingKeyMappings_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    PostingKey = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_PostingKeyMappings_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_PostingKeyMappings_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingKeyMappings", x => x.Id);
                    table.CheckConstraint("CK_PostingKeyMappings_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PostingKeyMappings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "acc",
                        principalTable: "Accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PostingKeyMappings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecordLocks",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RecordLocks_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AcquiredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RecordLocks_AcquiredAt"),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RecordLocks_LastActivityAt"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RecordLocks_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_RecordLocks_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordLocks", x => x.Id);
                    table.CheckConstraint("CK_RecordLocks_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RecordLocks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RolePermissions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_RolePermissions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_RolePermissions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.CheckConstraint("CK_RolePermissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalSchema: "core",
                        principalTable: "Permissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPreset = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_IsPreset"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.CheckConstraint("CK_Roles_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "SavedReports",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SavedReports_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Source = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsShared = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SavedReports_IsShared"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SavedReports_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SavedReports_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedReports", x => x.Id);
                    table.CheckConstraint("CK_SavedReports_Definition", "ISJSON([Definition]) = 1");
                    table.CheckConstraint("CK_SavedReports_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SavedReports_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StandardFieldConfigs",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    FieldKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_IsHidden"),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_IsRequired"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_SortOrder"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_StandardFieldConfigs_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardFieldConfigs", x => x.Id);
                    table.CheckConstraint("CK_StandardFieldConfigs_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_StandardFieldConfigs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionInvoices",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SubscriptionInvoices_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SubscriptionInvoices_Tax"),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, defaultValueSql: "'Due'")
                        .Annotation("Relational:DefaultConstraintName", "DF_SubscriptionInvoices_Status"),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GatewayReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SubscriptionInvoices_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SubscriptionInvoices_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoices", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionInvoices_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_SubscriptionInvoices_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_SubscriptionInvoices_PeriodEnd", "[PeriodEnd] >= [PeriodStart]");
                    table.CheckConstraint("CK_SubscriptionInvoices_Status", "[Status] IN ('Due','Paid','Failed','Credited')");
                    table.CheckConstraint("CK_SubscriptionInvoices_Tax", "[Tax] >= 0");
                    table.CheckConstraint("CK_SubscriptionInvoices_Total", "[Total] = [Amount] + [Tax]");
                });

            migrationBuilder.CreateTable(
                name: "SupportAccessGrants",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SupportAccessGrants_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedBy = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Requested'")
                        .Annotation("Relational:DefaultConstraintName", "DF_SupportAccessGrants_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_SupportAccessGrants_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_SupportAccessGrants_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAccessGrants", x => x.Id);
                    table.CheckConstraint("CK_SupportAccessGrants_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_SupportAccessGrants_EndsAt", "[EndsAt] > [StartsAt]");
                    table.CheckConstraint("CK_SupportAccessGrants_Status", "[Status] IN ('Requested','Approved','Rejected','Expired')");
                });

            migrationBuilder.CreateTable(
                name: "TableColumnViews",
                schema: "cfg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TableColumnViews_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ViewKey = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    Columns = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TableColumnViews_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TableColumnViews_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableColumnViews", x => x.Id);
                    table.CheckConstraint("CK_TableColumnViews_Columns", "ISJSON([Columns]) = 1");
                    table.CheckConstraint("CK_TableColumnViews_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TableColumnViews_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_PublicId"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false, defaultValueSql: "'Trial'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_Status"),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    BillingCycle = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Monthly'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_BillingCycle"),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GraceEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetentionEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordMinLength = table.Column<byte>(type: "tinyint", nullable: false, defaultValueSql: "8")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_PasswordMinLength"),
                    PasswordRequireLetter = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_PasswordRequireLetter"),
                    PasswordRequireDigit = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_PasswordRequireDigit"),
                    SessionIdleTimeoutMinutes = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "30")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_SessionIdleTimeoutMinutes"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_BillingCycle", "[BillingCycle] IN ('Monthly','Annual')");
                    table.CheckConstraint("CK_Tenants_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Tenants_PasswordMinLength", "[PasswordMinLength] BETWEEN 8 AND 64");
                    table.CheckConstraint("CK_Tenants_SessionIdleTimeoutMinutes", "[SessionIdleTimeoutMinutes] BETWEEN 5 AND 1440");
                    table.CheckConstraint("CK_Tenants_Status", "[Status] IN ('Trial','Active','PastDue','Suspended','Cancelled','Deleted')");
                    table.ForeignKey(
                        name: "FK_Tenants_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "core",
                        principalTable: "Plans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EmailConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPlatformStaff = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_IsPlatformStaff"),
                    TwoFactorSecret = table.Column<byte[]>(type: "varbinary(256)", maxLength: 256, nullable: true),
                    TwoFactorEnabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LanguageCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false, defaultValueSql: "'ar'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_LanguageCode"),
                    DefaultCompanyId = table.Column<long>(type: "bigint", nullable: true),
                    DateFormat = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValueSql: "'dd/MM/yyyy'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_DateFormat"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_Status"),
                    FailedSignInCount = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_FailedSignInCount"),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSignInAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_Users_IsPlatformStaff_TenantId", "([IsPlatformStaff] = 1 AND [TenantId] IS NULL) OR ([IsPlatformStaff] = 0 AND [TenantId] IS NOT NULL)");
                    table.CheckConstraint("CK_Users_LanguageCode", "[LanguageCode] IN ('ar','en')");
                    table.CheckConstraint("CK_Users_Status", "[Status] IN ('Active','Disabled','Archived')");
                    table.ForeignKey(
                        name: "FK_Users_Companies_DefaultCompanyId",
                        column: x => x.DefaultCompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TermsAcceptances",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TermsAcceptances_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DocumentVersion = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TermsAcceptances_AcceptedAt"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TermsAcceptances_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TermsAcceptances_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAcceptances", x => x.Id);
                    table.CheckConstraint("CK_TermsAcceptances_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TermsAcceptances_DocumentType", "[DocumentType] IN ('Terms','Privacy')");
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Customers",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Customers_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    RegistrationNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Customers_PaymentTermsDays"),
                    ApArPartyId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Customers_Status"),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Customers_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Customers_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Customers", x => x.Id);
                    table.CheckConstraint("CK_TRN_Customers_CustomValues", "ISJSON([CustomValues]) = 1");
                    table.CheckConstraint("CK_TRN_Customers_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Customers_PaymentTermsDays", "[PaymentTermsDays] >= 0");
                    table.CheckConstraint("CK_TRN_Customers_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_ApArParties_ApArPartyId",
                        column: x => x.ApArPartyId,
                        principalSchema: "acc",
                        principalTable: "ApArParties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Customers_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_InvoiceLayouts",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    LayoutType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BandText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Grouping = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValueSql: "'LoadingSite'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_Grouping"),
                    AnnexColumns = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Labels = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Signatures = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShowLogo = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_ShowLogo"),
                    ShowQr = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "1")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_ShowQr"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLayouts_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_InvoiceLayouts", x => x.Id);
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_AnnexColumns", "ISJSON([AnnexColumns]) = 1");
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_Grouping", "[Grouping] IN ('LoadingSite','Route','Material')");
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_Labels", "ISJSON([Labels]) = 1");
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_LayoutType", "[LayoutType] IN ('Claim','TaxInvoice')");
                    table.CheckConstraint("CK_TRN_InvoiceLayouts_Signatures", "ISJSON([Signatures]) = 1");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLayouts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLayouts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLayouts_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLayouts_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLayouts_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_LoadingSites",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_LoadingSites_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_LoadingSites_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_LoadingSites_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_LoadingSites_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_LoadingSites", x => x.Id);
                    table.CheckConstraint("CK_TRN_LoadingSites_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_LoadingSites_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_LoadingSites_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_LoadingSites_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_LoadingSites_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_LoadingSites_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_LoadingSites_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Materials",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Materials_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValueSql: "N'ton'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Materials_Unit"),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Materials_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Materials_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Materials_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Materials", x => x.Id);
                    table.CheckConstraint("CK_TRN_Materials_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Materials_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_Materials_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Materials_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Materials_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Materials_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Materials_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_OwnershipTypes",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_OwnershipTypes_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FleetClass = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_OwnershipTypes_IsBuiltIn"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_OwnershipTypes_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_OwnershipTypes_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_OwnershipTypes", x => x.Id);
                    table.CheckConstraint("CK_TRN_OwnershipTypes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_OwnershipTypes_FleetClass", "[FleetClass] IN ('Own','External')");
                    table.ForeignKey(
                        name: "FK_TRN_OwnershipTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_OwnershipTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_OwnershipTypes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_OwnershipTypes_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_OwnershipTypes_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Partners",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Partners_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxNumber = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: true),
                    CliqAlias = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Partners_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Partners_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Partners_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Partners", x => x.Id);
                    table.CheckConstraint("CK_TRN_Partners_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Partners_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_Partners_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Partners_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Partners_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Partners_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Partners_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_RateHistory",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_RateHistory_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedBy = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_RateHistory_ChangedAt"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_RateHistory_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_RateHistory_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_RateHistory", x => x.Id);
                    table.CheckConstraint("CK_TRN_RateHistory_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_RateHistory_NewValues", "ISJSON([NewValues]) = 1");
                    table.CheckConstraint("CK_TRN_RateHistory_OldValues", "ISJSON([OldValues]) = 1");
                    table.CheckConstraint("CK_TRN_RateHistory_SourceType", "[SourceType] IN ('Tariff','MonthlySiteRate')");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Users_ChangedBy",
                        column: x => x.ChangedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_RateHistory_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_UnloadingSites",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_UnloadingSites_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_UnloadingSites_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_UnloadingSites_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_UnloadingSites_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_UnloadingSites", x => x.Id);
                    table.CheckConstraint("CK_TRN_UnloadingSites_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_UnloadingSites_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_UnloadingSites_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_UnloadingSites_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_UnloadingSites_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_UnloadingSites_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_UnloadingSites_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserCompanyRoles",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserCompanyRoles_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserCompanyRoles_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserCompanyRoles_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCompanyRoles", x => x.Id);
                    table.CheckConstraint("CK_UserCompanyRoles_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "core",
                        principalTable: "Roles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCompanyRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserRecoveryCodes",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserRecoveryCodes_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserRecoveryCodes_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserRecoveryCodes_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecoveryCodes", x => x.Id);
                    table.CheckConstraint("CK_UserRecoveryCodes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserSessions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserSessions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserSessions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.CheckConstraint("CK_UserSessions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_UserSessions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Invoices",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    InvoiceType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_Discount"),
                    ExtraFees = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_ExtraFees"),
                    ExtraFeesDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_PaidAmount"),
                    ReturnedAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_ReturnedAmount"),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Draft'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_Status"),
                    LayoutType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Claim'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_LayoutType"),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedBy = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Invoices_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Invoices", x => x.Id);
                    table.CheckConstraint("CK_TRN_Invoices_BaseAmount", "[BaseAmount] = [Subtotal] - [Discount] + [ExtraFees]");
                    table.CheckConstraint("CK_TRN_Invoices_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Invoices_Discount", "[Discount] >= 0 AND [Discount] <= [Subtotal]");
                    table.CheckConstraint("CK_TRN_Invoices_DueDate", "[DueDate] >= [InvoiceDate]");
                    table.CheckConstraint("CK_TRN_Invoices_ExtraFees", "[ExtraFees] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_InvoiceType", "[InvoiceType] IN ('Trip','Manual')");
                    table.CheckConstraint("CK_TRN_Invoices_LayoutType", "[LayoutType] IN ('Claim','TaxInvoice')");
                    table.CheckConstraint("CK_TRN_Invoices_PaidAmount", "[PaidAmount] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_RemainingAmount", "[RemainingAmount] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_ReturnedAmount", "[ReturnedAmount] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_Status", "[Status] IN ('Draft','Approved','Closed','Void')");
                    table.CheckConstraint("CK_TRN_Invoices_Subtotal", "[Subtotal] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_Tax", "[Tax] >= 0");
                    table.CheckConstraint("CK_TRN_Invoices_TaxRate", "[TaxRate] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_TRN_Invoices_Total", "[Total] = [BaseAmount] + [Tax]");
                    table.CheckConstraint("CK_TRN_Invoices_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_TRN_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Users_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Invoices_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_MonthlySiteRates",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_MonthlySiteRates_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    RateMonth = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    LoadingSiteId = table.Column<long>(type: "bigint", nullable: false),
                    OwnFleetRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ExternalFleetRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_MonthlySiteRates_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_MonthlySiteRates_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_MonthlySiteRates", x => x.Id);
                    table.CheckConstraint("CK_TRN_MonthlySiteRates_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_MonthlySiteRates_ExternalFleetRate", "[ExternalFleetRate] > 0");
                    table.CheckConstraint("CK_TRN_MonthlySiteRates_OwnFleetRate", "[OwnFleetRate] > 0");
                    table.CheckConstraint("CK_TRN_MonthlySiteRates_OwnFleetRate_ExternalFleetRate", "[OwnFleetRate] IS NOT NULL OR [ExternalFleetRate] IS NOT NULL");
                    table.CheckConstraint("CK_TRN_MonthlySiteRates_RateMonth", "[RateMonth] LIKE '[12][0-9][0-9][0-9]-[01][0-9]'");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_TRN_LoadingSites_LoadingSiteId",
                        column: x => x.LoadingSiteId,
                        principalSchema: "trn",
                        principalTable: "TRN_LoadingSites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_MonthlySiteRates_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Drivers",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Drivers_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NationalId = table.Column<string>(type: "char(10)", unicode: false, fixedLength: true, maxLength: 10, nullable: false),
                    Phone1 = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Phone2 = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LicenceNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    LicenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LicenceExpiry = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Cash'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Drivers_PaymentMethod"),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: true),
                    CliqAlias = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlateNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TruckType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TrailerNo = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CapacityTons = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OwnershipTypeId = table.Column<long>(type: "bigint", nullable: false),
                    PartnerId = table.Column<long>(type: "bigint", nullable: true),
                    RegistrationExpiry = table.Column<DateOnly>(type: "date", nullable: false),
                    InsuranceExpiry = table.Column<DateOnly>(type: "date", nullable: false),
                    InspectionExpiry = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Drivers_Status"),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Drivers_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Drivers_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Drivers", x => x.Id);
                    table.CheckConstraint("CK_TRN_Drivers_CapacityTons", "[CapacityTons] > 0");
                    table.CheckConstraint("CK_TRN_Drivers_CustomValues", "ISJSON([CustomValues]) = 1");
                    table.CheckConstraint("CK_TRN_Drivers_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Drivers_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_TRN_Drivers_Status", "[Status] IN ('Active','Archived')");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_TRN_OwnershipTypes_OwnershipTypeId",
                        column: x => x.OwnershipTypeId,
                        principalSchema: "trn",
                        principalTable: "TRN_OwnershipTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_TRN_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Partners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Drivers_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_PartnerPayments",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PartnerId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CashBoxId = table.Column<long>(type: "bigint", nullable: true),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_PartnerPayments_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_PartnerPayments", x => x.Id);
                    table.CheckConstraint("CK_TRN_PartnerPayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_PartnerPayments_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
                    table.CheckConstraint("CK_TRN_PartnerPayments_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_PartnerPayments_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_TRN_PartnerPayments_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_TRN_PartnerPayments_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalSchema: "acc",
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalSchema: "acc",
                        principalTable: "CashBoxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_TRN_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Partners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_PartnerPayments_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Tariffs",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Tariffs_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    PartyType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    PartnerId = table.Column<long>(type: "bigint", nullable: true),
                    MaterialId = table.Column<long>(type: "bigint", nullable: true),
                    LoadingSiteId = table.Column<long>(type: "bigint", nullable: true),
                    UnloadingSiteId = table.Column<long>(type: "bigint", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Specificity = table.Column<byte>(type: "tinyint", nullable: false, computedColumnSql: "ISNULL(CONVERT(tinyint, CASE WHEN [CustomerId] IS NOT NULL OR [PartnerId] IS NOT NULL THEN 8 ELSE 0 END + CASE WHEN [MaterialId] IS NOT NULL THEN 4 ELSE 0 END + CASE WHEN [LoadingSiteId] IS NOT NULL THEN 2 ELSE 0 END + CASE WHEN [UnloadingSiteId] IS NOT NULL THEN 1 ELSE 0 END), 0)", stored: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Tariffs_Status"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Tariffs_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Tariffs_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Tariffs", x => x.Id);
                    table.CheckConstraint("CK_TRN_Tariffs_CustomerId_PartnerId", "([PartyType] = 'Customer' AND [PartnerId] IS NULL) OR ([PartyType] = 'Partner' AND [CustomerId] IS NULL)");
                    table.CheckConstraint("CK_TRN_Tariffs_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Tariffs_PartyType", "[PartyType] IN ('Customer','Partner')");
                    table.CheckConstraint("CK_TRN_Tariffs_Price", "[Price] > 0");
                    table.CheckConstraint("CK_TRN_Tariffs_Specificity", "[Specificity] BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_TRN_Tariffs_Status", "[Status] IN ('Active','Archived')");
                    table.CheckConstraint("CK_TRN_Tariffs_ValidTo", "[ValidTo] >= [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_TRN_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_TRN_LoadingSites_LoadingSiteId",
                        column: x => x.LoadingSiteId,
                        principalSchema: "trn",
                        principalTable: "TRN_LoadingSites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_TRN_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "trn",
                        principalTable: "TRN_Materials",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_TRN_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Partners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_TRN_UnloadingSites_UnloadingSiteId",
                        column: x => x.UnloadingSiteId,
                        principalSchema: "trn",
                        principalTable: "TRN_UnloadingSites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Tariffs_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_CreditNotes",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNotes_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CreditDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscountShare = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    FeesShare = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Posted'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNotes_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNotes_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNotes_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_TRN_CreditNotes_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_CreditNotes_Status", "[Status] IN ('Posted','Void')");
                    table.CheckConstraint("CK_TRN_CreditNotes_Subtotal", "[Subtotal] >= 0");
                    table.CheckConstraint("CK_TRN_CreditNotes_Tax", "[Tax] >= 0");
                    table.CheckConstraint("CK_TRN_CreditNotes_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_TRN_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNotes_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Receipts",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Receipts_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CashBoxId = table.Column<long>(type: "bigint", nullable: true),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentsCount = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Receipts_AttachmentsCount"),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Receipts_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Receipts_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Receipts_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Receipts", x => x.Id);
                    table.CheckConstraint("CK_TRN_Receipts_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_Receipts_AttachmentsCount", "[AttachmentsCount] >= 0");
                    table.CheckConstraint("CK_TRN_Receipts_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
                    table.CheckConstraint("CK_TRN_Receipts_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Receipts_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_TRN_Receipts_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_TRN_Receipts_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalSchema: "acc",
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalSchema: "acc",
                        principalTable: "CashBoxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_TRN_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Receipts_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Settlements",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DriverId = table.Column<long>(type: "bigint", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossWages = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AdvancesTotal = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DeductionsTotal = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NetPayable = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CashBoxId = table.Column<long>(type: "bigint", nullable: true),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_Status"),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Settlements_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Settlements", x => x.Id);
                    table.CheckConstraint("CK_TRN_Settlements_AdvancesTotal", "[AdvancesTotal] >= 0");
                    table.CheckConstraint("CK_TRN_Settlements_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
                    table.CheckConstraint("CK_TRN_Settlements_DeductionsTotal", "[DeductionsTotal] >= 0");
                    table.CheckConstraint("CK_TRN_Settlements_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Settlements_GrossWages", "[GrossWages] >= 0");
                    table.CheckConstraint("CK_TRN_Settlements_NetPayable", "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal]");
                    table.CheckConstraint("CK_TRN_Settlements_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_TRN_Settlements_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_TRN_Settlements_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalSchema: "acc",
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalSchema: "acc",
                        principalTable: "CashBoxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_TRN_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "trn",
                        principalTable: "TRN_Drivers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Settlements_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_EInvoiceSubmissions",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_EInvoiceSubmissions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    CreditNoteId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false, defaultValueSql: "'Queued'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_EInvoiceSubmissions_Status"),
                    Icv = table.Column<long>(type: "bigint", nullable: true),
                    UniqueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SystemNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    QrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_EInvoiceSubmissions_Attempts"),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_EInvoiceSubmissions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_EInvoiceSubmissions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_EInvoiceSubmissions", x => x.Id);
                    table.CheckConstraint("CK_TRN_EInvoiceSubmissions_Attempts", "[Attempts] >= 0");
                    table.CheckConstraint("CK_TRN_EInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_EInvoiceSubmissions_DocumentType", "[DocumentType] IN ('Invoice','CreditNote')");
                    table.CheckConstraint("CK_TRN_EInvoiceSubmissions_InvoiceId_CreditNoteId", "([DocumentType] = 'Invoice' AND [InvoiceId] IS NOT NULL AND [CreditNoteId] IS NULL) OR ([DocumentType] = 'CreditNote' AND [CreditNoteId] IS NOT NULL AND [InvoiceId] IS NULL)");
                    table.CheckConstraint("CK_TRN_EInvoiceSubmissions_Status", "[Status] IN ('Queued','Submitted','Rejected','NotRequired')");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_TRN_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "trn",
                        principalTable: "TRN_CreditNotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_EInvoiceSubmissions_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Advances",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Advances_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DriverId = table.Column<long>(type: "bigint", nullable: false),
                    AdvanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CashBoxId = table.Column<long>(type: "bigint", nullable: true),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Advances_Status"),
                    SettlementId = table.Column<long>(type: "bigint", nullable: true),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Advances_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Advances_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Advances", x => x.Id);
                    table.CheckConstraint("CK_TRN_Advances_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_Advances_CashBoxId_BankAccountId", "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)");
                    table.CheckConstraint("CK_TRN_Advances_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Advances_PaymentMethod", "[PaymentMethod] IN ('Cash','Bank','CliQ')");
                    table.CheckConstraint("CK_TRN_Advances_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_TRN_Advances_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalSchema: "acc",
                        principalTable: "BankAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_CashBoxes_CashBoxId",
                        column: x => x.CashBoxId,
                        principalSchema: "acc",
                        principalTable: "CashBoxes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_TRN_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "trn",
                        principalTable: "TRN_Drivers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_TRN_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalSchema: "trn",
                        principalTable: "TRN_Settlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Advances_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_SettlementDeductions",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_SettlementDeductions_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    SettlementId = table.Column<long>(type: "bigint", nullable: false),
                    DeductionType = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_SettlementDeductions_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_SettlementDeductions_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_SettlementDeductions", x => x.Id);
                    table.CheckConstraint("CK_TRN_SettlementDeductions_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TRN_SettlementDeductions_DeductionType", "[DeductionType] IN ('Shortage','Fine','Other')");
                    table.CheckConstraint("CK_TRN_SettlementDeductions_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_TRN_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalSchema: "trn",
                        principalTable: "TRN_Settlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_SettlementDeductions_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_Trips",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReceiptNoKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TripDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LoadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UnloadingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DriverId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    LoadingSiteId = table.Column<long>(type: "bigint", nullable: false),
                    UnloadingSiteId = table.Column<long>(type: "bigint", nullable: false),
                    MaterialId = table.Column<long>(type: "bigint", nullable: false),
                    PartnerId = table.Column<long>(type: "bigint", nullable: true),
                    TruckPlateAtSave = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    FleetClass = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    LoadedWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnloadedWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    BillingQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    WageQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TariffPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DriverRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RateSource = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    RateMonth = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    PartnerRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Revenue = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DriverWage = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PartnerCost = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_PartnerCost"),
                    ShortageWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ShortagePct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IsShortageFlagged = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_IsShortageFlagged"),
                    IsTariffBreach = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_IsTariffBreach"),
                    IsManualRate = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_IsManualRate"),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_NeedsReview"),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValueSql: "'Active'")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_Status"),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    SettlementId = table.Column<long>(type: "bigint", nullable: true),
                    JournalEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedBy = table.Column<long>(type: "bigint", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportBatchId = table.Column<long>(type: "bigint", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_Trips_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_Trips", x => x.Id);
                    table.CheckConstraint("CK_TRN_Trips_CustomerPrice", "[CustomerPrice] >= 0");
                    table.CheckConstraint("CK_TRN_Trips_CustomValues", "ISJSON([CustomValues]) = 1");
                    table.CheckConstraint("CK_TRN_Trips_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_Trips_DriverRate", "[DriverRate] >= 0");
                    table.CheckConstraint("CK_TRN_Trips_FleetClass", "[FleetClass] IN ('Own','External')");
                    table.CheckConstraint("CK_TRN_Trips_LoadedWeight", "[LoadedWeight] > 0");
                    table.CheckConstraint("CK_TRN_Trips_RateSource", "[RateSource] IN ('Monthly','Manual')");
                    table.CheckConstraint("CK_TRN_Trips_Status", "[Status] IN ('Active','Void')");
                    table.CheckConstraint("CK_TRN_Trips_UnloadedWeight", "[UnloadedWeight] >= 0");
                    table.CheckConstraint("CK_TRN_Trips_UnloadingDate", "[UnloadingDate] >= [LoadingDate]");
                    table.CheckConstraint("CK_TRN_Trips_VoidReason_VoidedBy_VoidedAt", "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalSchema: "cfg",
                        principalTable: "ImportBatches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "acc",
                        principalTable: "JournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Customers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "trn",
                        principalTable: "TRN_Drivers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_LoadingSites_LoadingSiteId",
                        column: x => x.LoadingSiteId,
                        principalSchema: "trn",
                        principalTable: "TRN_LoadingSites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "trn",
                        principalTable: "TRN_Materials",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "trn",
                        principalTable: "TRN_Partners",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalSchema: "trn",
                        principalTable: "TRN_Settlements",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_TRN_UnloadingSites_UnloadingSiteId",
                        column: x => x.UnloadingSiteId,
                        principalSchema: "trn",
                        principalTable: "TRN_UnloadingSites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_Trips_Users_VoidedBy",
                        column: x => x.VoidedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_InvoiceLines",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLines_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLines_SortOrder"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLines_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_InvoiceLines_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_TRN_InvoiceLines_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_TRN_InvoiceLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_InvoiceLines_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_TRN_InvoiceLines_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_TRN_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "trn",
                        principalTable: "TRN_Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_TRN_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "trn",
                        principalTable: "TRN_Trips",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_InvoiceLines_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TRN_CreditNoteLines",
                schema: "trn",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNoteLines_PublicId"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    CreditNoteId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceLineId = table.Column<long>(type: "bigint", nullable: false),
                    TripId = table.Column<long>(type: "bigint", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNoteLines_CreatedAt"),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "0")
                        .Annotation("Relational:DefaultConstraintName", "DF_TRN_CreditNoteLines_IsDeleted"),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRN_CreditNoteLines", x => x.Id);
                    table.CheckConstraint("CK_TRN_CreditNoteLines_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_TRN_CreditNoteLines_DeletedAt_DeletedBy_DeleteReason", "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)");
                    table.CheckConstraint("CK_TRN_CreditNoteLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_TRN_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "trn",
                        principalTable: "TRN_CreditNotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_TRN_InvoiceLines_InvoiceLineId",
                        column: x => x.InvoiceLineId,
                        principalSchema: "trn",
                        principalTable: "TRN_InvoiceLines",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_TRN_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "trn",
                        principalTable: "TRN_Trips",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "core",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TRN_CreditNoteLines_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Permissions",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DeleteReason", "DeletedAt", "DeletedBy", "DescriptionAr", "DescriptionEn", "GroupName", "IsDeleted", "PermissionKey", "PublicId", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض الرحلات وسجلّ تغييراتها", "View trips and trip history", "Trips", false, "trn.trips.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a01"), null, null },
                    { 2L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إنشاء الرحلات واستيرادها وتعديلها", "Create, import and edit trips", "Trips", false, "trn.trips.edit", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a02"), null, null },
                    { 3L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إلغاء الرحلات مع ذكر السبب", "Void trips with a reason", "Trips", false, "trn.trips.void", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a03"), null, null },
                    { 4L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض الفواتير والمسودات والإشعارات الدائنة", "View invoices, drafts and credit notes", "Invoicing", false, "trn.inv.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a04"), null, null },
                    { 5L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إنشاء الفواتير (على الرحلات واليدوية)، واعتماد المسودات، وإصدار الإشعارات الدائنة، وتصميم نماذج الفواتير، وإرسال الفواتير الإلكترونية", "Create invoices (trip-based and manual), approve drafts, issue credit notes, design invoice layouts, submit e-invoices", "Invoicing", false, "trn.inv.issue", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a05"), null, null },
                    { 6L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إلغاء الفواتير والإشعارات الدائنة", "Void invoices and credit notes", "Invoicing", false, "trn.inv.void", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a06"), null, null },
                    { 7L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "تسجيل سندات القبض من العملاء وإلغاؤها", "Record and void customer receipts", "Receipts", false, "trn.rcpt.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a07"), null, null },
                    { 8L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض حسابات السائقين والشركاء وكشوفها", "View driver and partner accounts and statements", "Drivers & partners", false, "trn.drv.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a08"), null, null },
                    { 9L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "تسجيل سلف السائقين وتعديلها وإلغاؤها", "Record, edit and void driver advances", "Drivers & partners", false, "trn.drv.advance", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a09"), null, null },
                    { 10L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "تسوية حسابات السائقين، وإلغاء التسويات، وتسجيل دفعات الشركاء", "Settle drivers, void settlements, record partner payments", "Drivers & partners", false, "trn.drv.settle", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a10"), null, null },
                    { 11L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض القيود اليومية المولّدة تلقائياً", "View automatically generated journal entries", "Journal", false, "acc.je.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a11"), null, null },
                    { 12L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "ترحيل القيود إلى الأستاذ العام، وإعادة محاولة الترحيلات المعلّقة، وتصدير دفعات القيود", "Post entries to the ledger, retry pending postings, export journal batches", "Journal", false, "acc.je.export", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a12"), null, null },
                    { 13L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "عرض الأستاذ العام وميزان المراجعة والقوائم المالية وكشوف الحسابات", "View the general ledger, trial balance, financial statements and account statements", "Accounting", false, "acc.acct.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a13"), null, null },
                    { 14L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إنشاء القيود اليدوية وتعديلها وإلغاؤها، واستيراد القيود", "Create, edit and void manual journal entries; import entries", "Accounting", false, "acc.acct.post", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a14"), null, null },
                    { 15L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إدارة مستندات الذمم الدائنة والمدينة ودفعاتها ومرتجعاتها", "Manage payable and receivable documents, payments and returns", "Accounting", false, "acc.acct.apar", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a15"), null, null },
                    { 16L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إدارة دليل الحسابات ومراكز التكلفة والبنوك", "Manage the chart of accounts, cost centres and banks", "Accounting", false, "acc.acct.setup", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a16"), null, null },
                    { 17L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "تنفيذ عمليات صيانة الحسابات: الحذف الجماعي للقيود غير المرتبطة، ومسح بيانات الشركة، واستبدال دليل الحسابات", "Run accounting maintenance: bulk deletion of unlinked entries, clearing a company's data, or replacing the chart of accounts", "Accounting", false, "acc.acct.maint", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a17"), null, null },
                    { 18L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "الوصول إلى جميع التقارير ومنشئ التقارير والتقارير المحفوظة", "Access all reports, the report builder and saved reports", "Reports & master data", false, "trn.reports.view", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a18"), null, null },
                    { 19L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إدارة البيانات الأساسية والتعرفات وأسعار المواقع الشهرية والشركاء، وإدخال أسعار السائقين يدوياً", "Manage master data, tariffs, monthly site prices and partners; enter manual driver-rate overrides", "Reports & master data", false, "trn.masters.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a19"), null, null },
                    { 20L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "إعدادات الشركة وربط الحسابات والضريبة والفترات والترقيم وتهيئة الحقول والنماذج", "Company settings, account mapping, tax, periods, numbering, configuration of fields and layouts", "Administration", false, "core.settings.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a20"), null, null },
                    { 21L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "دعوة المستخدمين وتعديلهم وتعطيلهم وفكّ قفلهم وأرشفتهم وتعيين الأدوار", "Invite, edit, disable, unlock and archive users; assign roles", "Administration", false, "core.users.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a21"), null, null },
                    { 22L, new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, "الاشتراك والخطة والفوترة، وإنشاء الشركات وأرشفتها، وإعدادات المشترك العامة", "Subscription, plan and billing; create and archive companies; tenant-wide settings", "Tenant", false, "core.tenant.manage", new Guid("7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a22"), null, null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Plans",
                columns: new[] { "Id", "BillingCycle", "Code", "CreatedAt", "CreatedBy", "DeleteReason", "DeletedAt", "DeletedBy", "IsActive", "IsDeleted", "MaxActiveUsers", "MaxCompanies", "MaxStorageMb", "MaxTripsPerMonth", "NameAr", "NameEn", "Price", "PublicId", "UpdatedAt", "UpdatedBy" },
                values: new object[] { 1L, "Monthly", "TRIAL", new DateTime(2026, 10, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, false, false, 5, 2, 1024, 2000, "تجريبية", "Trial", 0m, new Guid("3f9a1c7e-2d44-4b8e-8f1a-6c0b9e2d7a01"), null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId",
                schema: "acc",
                table: "Accounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UX_Accounts_CompanyId_Code",
                schema: "acc",
                table: "Accounts",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Accounts_PublicId",
                schema: "acc",
                table: "Accounts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Announcements_PublicId",
                schema: "core",
                table: "Announcements",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocumentLines_AccountId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocumentLines_CompanyId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocumentLines_CostCentreId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocumentLines_DocumentId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "UX_ApArDocumentLines_PublicId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocuments_JournalEntryId",
                schema: "acc",
                table: "ApArDocuments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocuments_PartyId",
                schema: "acc",
                table: "ApArDocuments",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArDocuments_VoidedBy",
                schema: "acc",
                table: "ApArDocuments",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_ApArDocuments_CompanyId_DocumentKind_Number",
                schema: "acc",
                table: "ApArDocuments",
                columns: new[] { "CompanyId", "DocumentKind", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ApArDocuments_PublicId",
                schema: "acc",
                table: "ApArDocuments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArEInvoiceSubmissions_ApArDocumentId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "ApArDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArEInvoiceSubmissions_ApArReturnId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "ApArReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArEInvoiceSubmissions_Queue",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                columns: new[] { "CompanyId", "Status" },
                filter: "[Status] IN ('Queued','Rejected')");

            migrationBuilder.CreateIndex(
                name: "UX_ApArEInvoiceSubmissions_CompanyId_Icv",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                columns: new[] { "CompanyId", "Icv" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Icv] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ApArEInvoiceSubmissions_PublicId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArParties_CompanyId",
                schema: "acc",
                table: "ApArParties",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ApArParties_ControlAccountId",
                schema: "acc",
                table: "ApArParties",
                column: "ControlAccountId");

            migrationBuilder.CreateIndex(
                name: "UX_ApArParties_PublicId",
                schema: "acc",
                table: "ApArParties",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArPayments_BankAccountId",
                schema: "acc",
                table: "ApArPayments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArPayments_CashBoxId",
                schema: "acc",
                table: "ApArPayments",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArPayments_DocumentId",
                schema: "acc",
                table: "ApArPayments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArPayments_JournalEntryId",
                schema: "acc",
                table: "ApArPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArPayments_VoidedBy",
                schema: "acc",
                table: "ApArPayments",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_ApArPayments_CompanyId_Number",
                schema: "acc",
                table: "ApArPayments",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ApArPayments_PublicId",
                schema: "acc",
                table: "ApArPayments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApArReturns_DocumentId",
                schema: "acc",
                table: "ApArReturns",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArReturns_JournalEntryId",
                schema: "acc",
                table: "ApArReturns",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ApArReturns_VoidedBy",
                schema: "acc",
                table: "ApArReturns",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_ApArReturns_CompanyId_Number",
                schema: "acc",
                table: "ApArReturns",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_ApArReturns_PublicId",
                schema: "acc",
                table: "ApArReturns",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Entity",
                schema: "core",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                schema: "core",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_User",
                schema: "core",
                table: "AuditLogs",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_AccountId",
                schema: "acc",
                table: "BankAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_CompanyId",
                schema: "acc",
                table: "BankAccounts",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_BankAccounts_PublicId",
                schema: "acc",
                table: "BankAccounts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashBoxes_AccountId",
                schema: "acc",
                table: "CashBoxes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashBoxes_CompanyId",
                schema: "acc",
                table: "CashBoxes",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_CashBoxes_PublicId",
                schema: "acc",
                table: "CashBoxes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Companies_PublicId",
                schema: "core",
                table: "Companies",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Companies_TenantId_Name",
                schema: "core",
                table: "Companies",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_CompanySettings_CompanyId_SettingKey",
                schema: "cfg",
                table: "CompanySettings",
                columns: new[] { "CompanyId", "SettingKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_CompanySettings_PublicId",
                schema: "cfg",
                table: "CompanySettings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CostCentres_CompanyId_Code",
                schema: "acc",
                table: "CostCentres",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_CostCentres_PublicId",
                schema: "acc",
                table: "CostCentres",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CustomFieldDefinitions_CompanyId_EntityType_FieldKey",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                columns: new[] { "CompanyId", "EntityType", "FieldKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_CustomFieldDefinitions_PublicId",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardPins_CompanyId",
                schema: "cfg",
                table: "DashboardPins",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardPins_SavedReportId",
                schema: "cfg",
                table: "DashboardPins",
                column: "SavedReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardPins_UserId",
                schema: "cfg",
                table: "DashboardPins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_DashboardPins_PublicId",
                schema: "cfg",
                table: "DashboardPins",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_EInvoiceSettings_CompanyId",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "CompanyId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EInvoiceSettings_PublicId",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_Account",
                schema: "acc",
                table: "EntryLines",
                column: "AccountId")
                .Annotation("SqlServer:Include", new[] { "Debit", "Credit" });

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_CostCentreId",
                schema: "acc",
                table: "EntryLines",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_JournalEntryId",
                schema: "acc",
                table: "EntryLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryLines_Party",
                schema: "acc",
                table: "EntryLines",
                columns: new[] { "CompanyId", "PartyModule", "PartyType", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "UX_EntryLines_PublicId",
                schema: "acc",
                table: "EntryLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CompanyId",
                schema: "cfg",
                table: "ImportBatches",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ImportedBy",
                schema: "cfg",
                table: "ImportBatches",
                column: "ImportedBy");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_PublicId",
                schema: "cfg",
                table: "ImportBatches",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_CompanyId",
                schema: "core",
                table: "Invitations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InvitedBy",
                schema: "core",
                table: "Invitations",
                column: "InvitedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_RoleId",
                schema: "core",
                table: "Invitations",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UX_Invitations_PublicId",
                schema: "core",
                table: "Invitations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Invitations_TokenHash",
                schema: "core",
                table: "Invitations",
                column: "TokenHash",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ExportBatchId",
                schema: "acc",
                table: "JournalEntries",
                column: "ExportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Period",
                schema: "acc",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversedByEntryId",
                schema: "acc",
                table: "JournalEntries",
                column: "ReversedByEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversesEntryId",
                schema: "acc",
                table: "JournalEntries",
                column: "ReversesEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Source",
                schema: "acc",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "SourceModule", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_VoidedBy",
                schema: "acc",
                table: "JournalEntries",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntries_CompanyId_Number",
                schema: "acc",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntries_PublicId",
                schema: "acc",
                table: "JournalEntries",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalExportBatches_CompanyId",
                schema: "acc",
                table: "JournalExportBatches",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_JournalExportBatches_ExportedBy",
                schema: "acc",
                table: "JournalExportBatches",
                column: "ExportedBy");

            migrationBuilder.CreateIndex(
                name: "UX_JournalExportBatches_PublicId",
                schema: "acc",
                table: "JournalExportBatches",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_CompanyId",
                schema: "core",
                table: "MaintenanceLogs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_TenantId",
                schema: "core",
                table: "MaintenanceLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_UserId",
                schema: "core",
                table: "MaintenanceLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CompanyId",
                schema: "core",
                table: "Notifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unread",
                schema: "core",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_PublicId",
                schema: "core",
                table: "Notifications",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_NumberSequences_CompanyId_DocumentType",
                schema: "cfg",
                table: "NumberSequences",
                columns: new[] { "CompanyId", "DocumentType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_NumberSequences_PublicId",
                schema: "cfg",
                table: "NumberSequences",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingPostings_Open",
                schema: "acc",
                table: "PendingPostings",
                columns: new[] { "CompanyId", "ResolvedAt" },
                filter: "[ResolvedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PendingPostings_PublicId",
                schema: "acc",
                table: "PendingPostings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PendingPostings_Source",
                schema: "acc",
                table: "PendingPostings",
                columns: new[] { "CompanyId", "SourceModule", "SourceType", "SourceId" },
                unique: true,
                filter: "[ResolvedAt] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Periods_ClosedBy",
                schema: "acc",
                table: "Periods",
                column: "ClosedBy");

            migrationBuilder.CreateIndex(
                name: "UX_Periods_CompanyId_PeriodMonth",
                schema: "acc",
                table: "Periods",
                columns: new[] { "CompanyId", "PeriodMonth" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Periods_PublicId",
                schema: "acc",
                table: "Periods",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Permissions_PermissionKey",
                schema: "core",
                table: "Permissions",
                column: "PermissionKey",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Permissions_PublicId",
                schema: "core",
                table: "Permissions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Plans_Code",
                schema: "core",
                table: "Plans",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Plans_PublicId",
                schema: "core",
                table: "Plans",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingKeyMappings_AccountId",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "UX_PostingKeyMappings_CompanyId_PostingKey",
                schema: "acc",
                table: "PostingKeyMappings",
                columns: new[] { "CompanyId", "PostingKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PostingKeyMappings_PublicId",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordLocks_UserId",
                schema: "cfg",
                table: "RecordLocks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_RecordLocks_CompanyId_EntityType_EntityId",
                schema: "cfg",
                table: "RecordLocks",
                columns: new[] { "CompanyId", "EntityType", "EntityId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RecordLocks_PublicId",
                schema: "cfg",
                table: "RecordLocks",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                schema: "core",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "UX_RolePermissions_PublicId",
                schema: "core",
                table: "RolePermissions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RolePermissions_RoleId_PermissionId",
                schema: "core",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_PublicId",
                schema: "core",
                table: "Roles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Roles_TenantId_Name",
                schema: "core",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_CompanyId",
                schema: "cfg",
                table: "SavedReports",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SavedReports_OwnerUserId",
                schema: "cfg",
                table: "SavedReports",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SavedReports_PublicId",
                schema: "cfg",
                table: "SavedReports",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StandardFieldConfigs_CompanyId_EntityType_FieldKey",
                schema: "cfg",
                table: "StandardFieldConfigs",
                columns: new[] { "CompanyId", "EntityType", "FieldKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_StandardFieldConfigs_PublicId",
                schema: "cfg",
                table: "StandardFieldConfigs",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SubscriptionInvoices_Number",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "Number",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_SubscriptionInvoices_PublicId",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_ApprovedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAccessGrants_RequestedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "UX_SupportAccessGrants_PublicId",
                schema: "core",
                table: "SupportAccessGrants",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableColumnViews_UserId",
                schema: "cfg",
                table: "TableColumnViews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_TableColumnViews_Key",
                schema: "cfg",
                table: "TableColumnViews",
                columns: new[] { "CompanyId", "ViewKey", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TableColumnViews_PublicId",
                schema: "cfg",
                table: "TableColumnViews",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_PlanId",
                schema: "core",
                table: "Tenants",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_PublicId",
                schema: "core",
                table: "Tenants",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_UserId",
                schema: "core",
                table: "TermsAcceptances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_TermsAcceptances_PublicId",
                schema: "core",
                table: "TermsAcceptances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_BankAccountId",
                schema: "trn",
                table: "TRN_Advances",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_CashBoxId",
                schema: "trn",
                table: "TRN_Advances",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_DriverId",
                schema: "trn",
                table: "TRN_Advances",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_JournalEntryId",
                schema: "trn",
                table: "TRN_Advances",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_SettlementId",
                schema: "trn",
                table: "TRN_Advances",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Advances_VoidedBy",
                schema: "trn",
                table: "TRN_Advances",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Advances_CompanyId_Number",
                schema: "trn",
                table: "TRN_Advances",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Advances_PublicId",
                schema: "trn",
                table: "TRN_Advances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNoteLines_CompanyId",
                schema: "trn",
                table: "TRN_CreditNoteLines",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNoteLines_CreditNoteId",
                schema: "trn",
                table: "TRN_CreditNoteLines",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNoteLines_InvoiceLineId",
                schema: "trn",
                table: "TRN_CreditNoteLines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNoteLines_TripId",
                schema: "trn",
                table: "TRN_CreditNoteLines",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_CreditNoteLines_PublicId",
                schema: "trn",
                table: "TRN_CreditNoteLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNotes_CustomerId",
                schema: "trn",
                table: "TRN_CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNotes_InvoiceId",
                schema: "trn",
                table: "TRN_CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNotes_JournalEntryId",
                schema: "trn",
                table: "TRN_CreditNotes",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_CreditNotes_VoidedBy",
                schema: "trn",
                table: "TRN_CreditNotes",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_CreditNotes_CompanyId_Number",
                schema: "trn",
                table: "TRN_CreditNotes",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_CreditNotes_PublicId",
                schema: "trn",
                table: "TRN_CreditNotes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Customers_ApArPartyId",
                schema: "trn",
                table: "TRN_Customers",
                column: "ApArPartyId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Customers_CompanyId_Name",
                schema: "trn",
                table: "TRN_Customers",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Customers_PublicId",
                schema: "trn",
                table: "TRN_Customers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Drivers_Expiry",
                schema: "trn",
                table: "TRN_Drivers",
                columns: new[] { "CompanyId", "LicenceExpiry", "RegistrationExpiry", "InsuranceExpiry", "InspectionExpiry" },
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Drivers_OwnershipTypeId",
                schema: "trn",
                table: "TRN_Drivers",
                column: "OwnershipTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Drivers_PartnerId",
                schema: "trn",
                table: "TRN_Drivers",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Drivers_CompanyId_NationalId",
                schema: "trn",
                table: "TRN_Drivers",
                columns: new[] { "CompanyId", "NationalId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Drivers_CompanyId_PlateNumber",
                schema: "trn",
                table: "TRN_Drivers",
                columns: new[] { "CompanyId", "PlateNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Drivers_PublicId",
                schema: "trn",
                table: "TRN_Drivers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_EInvoiceSubmissions_CreditNoteId",
                schema: "trn",
                table: "TRN_EInvoiceSubmissions",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_EInvoiceSubmissions_InvoiceId",
                schema: "trn",
                table: "TRN_EInvoiceSubmissions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_EInvoiceSubmissions_Queue",
                schema: "trn",
                table: "TRN_EInvoiceSubmissions",
                columns: new[] { "CompanyId", "Status" },
                filter: "[Status] IN ('Queued','Rejected')");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_EInvoiceSubmissions_CompanyId_Icv",
                schema: "trn",
                table: "TRN_EInvoiceSubmissions",
                columns: new[] { "CompanyId", "Icv" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Icv] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_EInvoiceSubmissions_PublicId",
                schema: "trn",
                table: "TRN_EInvoiceSubmissions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_InvoiceLayouts_CompanyId_LayoutType",
                schema: "trn",
                table: "TRN_InvoiceLayouts",
                columns: new[] { "CompanyId", "LayoutType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_InvoiceLayouts_PublicId",
                schema: "trn",
                table: "TRN_InvoiceLayouts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_InvoiceLines_CompanyId",
                schema: "trn",
                table: "TRN_InvoiceLines",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_InvoiceLines_InvoiceId",
                schema: "trn",
                table: "TRN_InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_InvoiceLines_TripId",
                schema: "trn",
                table: "TRN_InvoiceLines",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_InvoiceLines_PublicId",
                schema: "trn",
                table: "TRN_InvoiceLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_ApprovedBy",
                schema: "trn",
                table: "TRN_Invoices",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_Customer",
                schema: "trn",
                table: "TRN_Invoices",
                columns: new[] { "CompanyId", "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_CustomerId",
                schema: "trn",
                table: "TRN_Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_Date",
                schema: "trn",
                table: "TRN_Invoices",
                columns: new[] { "CompanyId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_JournalEntryId",
                schema: "trn",
                table: "TRN_Invoices",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Invoices_VoidedBy",
                schema: "trn",
                table: "TRN_Invoices",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Invoices_CompanyId_Number",
                schema: "trn",
                table: "TRN_Invoices",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Invoices_PublicId",
                schema: "trn",
                table: "TRN_Invoices",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_LoadingSites_CompanyId_Name",
                schema: "trn",
                table: "TRN_LoadingSites",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_LoadingSites_PublicId",
                schema: "trn",
                table: "TRN_LoadingSites",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Materials_CompanyId_Name",
                schema: "trn",
                table: "TRN_Materials",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Materials_PublicId",
                schema: "trn",
                table: "TRN_Materials",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_MonthlySiteRates_LoadingSiteId",
                schema: "trn",
                table: "TRN_MonthlySiteRates",
                column: "LoadingSiteId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_MonthlySiteRates_CompanyId_RateMonth_LoadingSiteId",
                schema: "trn",
                table: "TRN_MonthlySiteRates",
                columns: new[] { "CompanyId", "RateMonth", "LoadingSiteId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_MonthlySiteRates_PublicId",
                schema: "trn",
                table: "TRN_MonthlySiteRates",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_OwnershipTypes_CompanyId_Name",
                schema: "trn",
                table: "TRN_OwnershipTypes",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_OwnershipTypes_PublicId",
                schema: "trn",
                table: "TRN_OwnershipTypes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerPayments_BankAccountId",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerPayments_CashBoxId",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerPayments_JournalEntryId",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerPayments_PartnerId",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_PartnerPayments_VoidedBy",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_PartnerPayments_CompanyId_Number",
                schema: "trn",
                table: "TRN_PartnerPayments",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_PartnerPayments_PublicId",
                schema: "trn",
                table: "TRN_PartnerPayments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Partners_CompanyId_Name",
                schema: "trn",
                table: "TRN_Partners",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Partners_PublicId",
                schema: "trn",
                table: "TRN_Partners",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_RateHistory_ChangedBy",
                schema: "trn",
                table: "TRN_RateHistory",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_RateHistory_Source",
                schema: "trn",
                table: "TRN_RateHistory",
                columns: new[] { "CompanyId", "SourceType", "SourceId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_TRN_RateHistory_PublicId",
                schema: "trn",
                table: "TRN_RateHistory",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_BankAccountId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_CashBoxId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_CustomerId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_InvoiceId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_JournalEntryId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Receipts_VoidedBy",
                schema: "trn",
                table: "TRN_Receipts",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Receipts_CompanyId_Number",
                schema: "trn",
                table: "TRN_Receipts",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Receipts_PublicId",
                schema: "trn",
                table: "TRN_Receipts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_SettlementDeductions_CompanyId",
                schema: "trn",
                table: "TRN_SettlementDeductions",
                column: "CompanyId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_SettlementDeductions_SettlementId",
                schema: "trn",
                table: "TRN_SettlementDeductions",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_SettlementDeductions_PublicId",
                schema: "trn",
                table: "TRN_SettlementDeductions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Settlements_BankAccountId",
                schema: "trn",
                table: "TRN_Settlements",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Settlements_CashBoxId",
                schema: "trn",
                table: "TRN_Settlements",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Settlements_DriverId",
                schema: "trn",
                table: "TRN_Settlements",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Settlements_JournalEntryId",
                schema: "trn",
                table: "TRN_Settlements",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Settlements_VoidedBy",
                schema: "trn",
                table: "TRN_Settlements",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Settlements_CompanyId_Number",
                schema: "trn",
                table: "TRN_Settlements",
                columns: new[] { "CompanyId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Settlements_PublicId",
                schema: "trn",
                table: "TRN_Settlements",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_CustomerId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_LoadingSiteId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "LoadingSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_Lookup",
                schema: "trn",
                table: "TRN_Tariffs",
                columns: new[] { "CompanyId", "PartyType", "Status", "Specificity", "ValidFrom" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_MaterialId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_PartnerId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Tariffs_UnloadingSiteId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "UnloadingSiteId");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Tariffs_CompanyId_PartyType_CustomerId_PartnerId_MaterialId_LoadingSiteId_UnloadingSiteId_ValidFrom",
                schema: "trn",
                table: "TRN_Tariffs",
                columns: new[] { "CompanyId", "PartyType", "CustomerId", "PartnerId", "MaterialId", "LoadingSiteId", "UnloadingSiteId", "ValidFrom" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Tariffs_PublicId",
                schema: "trn",
                table: "TRN_Tariffs",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_CustomerId",
                schema: "trn",
                table: "TRN_Trips",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_Date",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "CompanyId", "TripDate" })
                .Annotation("SqlServer:Include", new[] { "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_DriverId",
                schema: "trn",
                table: "TRN_Trips",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_ImportBatchId",
                schema: "trn",
                table: "TRN_Trips",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_InvoiceId",
                schema: "trn",
                table: "TRN_Trips",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_JournalEntryId",
                schema: "trn",
                table: "TRN_Trips",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_LoadingSiteId",
                schema: "trn",
                table: "TRN_Trips",
                column: "LoadingSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_MaterialId",
                schema: "trn",
                table: "TRN_Trips",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_PartnerId",
                schema: "trn",
                table: "TRN_Trips",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_SettlementId",
                schema: "trn",
                table: "TRN_Trips",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_Unbilled",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "CompanyId", "CustomerId", "Status" },
                filter: "[InvoiceId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_UnloadingSiteId",
                schema: "trn",
                table: "TRN_Trips",
                column: "UnloadingSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_Unsettled",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "CompanyId", "DriverId", "Status" },
                filter: "[SettlementId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TRN_Trips_VoidedBy",
                schema: "trn",
                table: "TRN_Trips",
                column: "VoidedBy");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Trips_CompanyId_ReceiptNoKey",
                schema: "trn",
                table: "TRN_Trips",
                columns: new[] { "CompanyId", "ReceiptNoKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_Trips_PublicId",
                schema: "trn",
                table: "TRN_Trips",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TRN_UnloadingSites_CompanyId_Name",
                schema: "trn",
                table: "TRN_UnloadingSites",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TRN_UnloadingSites_PublicId",
                schema: "trn",
                table: "TRN_UnloadingSites",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCompanyRoles_CompanyId",
                schema: "core",
                table: "UserCompanyRoles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCompanyRoles_RoleId",
                schema: "core",
                table: "UserCompanyRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UX_UserCompanyRoles_PublicId",
                schema: "core",
                table: "UserCompanyRoles",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserCompanyRoles_UserId_CompanyId",
                schema: "core",
                table: "UserCompanyRoles",
                columns: new[] { "UserId", "CompanyId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserRecoveryCodes_UserId",
                schema: "core",
                table: "UserRecoveryCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_UserRecoveryCodes_PublicId",
                schema: "core",
                table: "UserRecoveryCodes",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultCompanyId",
                schema: "core",
                table: "Users",
                column: "DefaultCompanyId");

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

            migrationBuilder.CreateIndex(
                name: "UX_Users_PublicId",
                schema: "core",
                table: "Users",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_ExpiresAt",
                schema: "core",
                table: "UserSessions",
                columns: new[] { "UserId", "ExpiresAt" },
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_UserSessions_PublicId",
                schema: "core",
                table: "UserSessions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserSessions_TokenHash",
                schema: "core",
                table: "UserSessions",
                column: "TokenHash",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Companies_CompanyId",
                schema: "acc",
                table: "Accounts",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Tenants_TenantId",
                schema: "acc",
                table: "Accounts",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Users_CreatedBy",
                schema: "acc",
                table: "Accounts",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Users_DeletedBy",
                schema: "acc",
                table: "Accounts",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Users_UpdatedBy",
                schema: "acc",
                table: "Accounts",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_Users_CreatedBy",
                schema: "core",
                table: "Announcements",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_Users_DeletedBy",
                schema: "core",
                table: "Announcements",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_Users_UpdatedBy",
                schema: "core",
                table: "Announcements",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_ApArDocuments_DocumentId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "DocumentId",
                principalSchema: "acc",
                principalTable: "ApArDocuments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_Companies_CompanyId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_CostCentres_CostCentreId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "CostCentreId",
                principalSchema: "acc",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_Tenants_TenantId",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_Users_CreatedBy",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_Users_DeletedBy",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocumentLines_Users_UpdatedBy",
                schema: "acc",
                table: "ApArDocumentLines",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_ApArParties_PartyId",
                schema: "acc",
                table: "ApArDocuments",
                column: "PartyId",
                principalSchema: "acc",
                principalTable: "ApArParties",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Companies_CompanyId",
                schema: "acc",
                table: "ApArDocuments",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_JournalEntries_JournalEntryId",
                schema: "acc",
                table: "ApArDocuments",
                column: "JournalEntryId",
                principalSchema: "acc",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Tenants_TenantId",
                schema: "acc",
                table: "ApArDocuments",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Users_CreatedBy",
                schema: "acc",
                table: "ApArDocuments",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Users_DeletedBy",
                schema: "acc",
                table: "ApArDocuments",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Users_UpdatedBy",
                schema: "acc",
                table: "ApArDocuments",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArDocuments_Users_VoidedBy",
                schema: "acc",
                table: "ApArDocuments",
                column: "VoidedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_ApArReturns_ApArReturnId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "ApArReturnId",
                principalSchema: "acc",
                principalTable: "ApArReturns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_Companies_CompanyId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_Tenants_TenantId",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_Users_CreatedBy",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_Users_DeletedBy",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArEInvoiceSubmissions_Users_UpdatedBy",
                schema: "acc",
                table: "ApArEInvoiceSubmissions",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArParties_Companies_CompanyId",
                schema: "acc",
                table: "ApArParties",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArParties_Tenants_TenantId",
                schema: "acc",
                table: "ApArParties",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArParties_Users_CreatedBy",
                schema: "acc",
                table: "ApArParties",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArParties_Users_DeletedBy",
                schema: "acc",
                table: "ApArParties",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArParties_Users_UpdatedBy",
                schema: "acc",
                table: "ApArParties",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_BankAccounts_BankAccountId",
                schema: "acc",
                table: "ApArPayments",
                column: "BankAccountId",
                principalSchema: "acc",
                principalTable: "BankAccounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_CashBoxes_CashBoxId",
                schema: "acc",
                table: "ApArPayments",
                column: "CashBoxId",
                principalSchema: "acc",
                principalTable: "CashBoxes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Companies_CompanyId",
                schema: "acc",
                table: "ApArPayments",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_JournalEntries_JournalEntryId",
                schema: "acc",
                table: "ApArPayments",
                column: "JournalEntryId",
                principalSchema: "acc",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Tenants_TenantId",
                schema: "acc",
                table: "ApArPayments",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Users_CreatedBy",
                schema: "acc",
                table: "ApArPayments",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Users_DeletedBy",
                schema: "acc",
                table: "ApArPayments",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Users_UpdatedBy",
                schema: "acc",
                table: "ApArPayments",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArPayments_Users_VoidedBy",
                schema: "acc",
                table: "ApArPayments",
                column: "VoidedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Companies_CompanyId",
                schema: "acc",
                table: "ApArReturns",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_JournalEntries_JournalEntryId",
                schema: "acc",
                table: "ApArReturns",
                column: "JournalEntryId",
                principalSchema: "acc",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Tenants_TenantId",
                schema: "acc",
                table: "ApArReturns",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Users_CreatedBy",
                schema: "acc",
                table: "ApArReturns",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Users_DeletedBy",
                schema: "acc",
                table: "ApArReturns",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Users_UpdatedBy",
                schema: "acc",
                table: "ApArReturns",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ApArReturns_Users_VoidedBy",
                schema: "acc",
                table: "ApArReturns",
                column: "VoidedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Companies_CompanyId",
                schema: "core",
                table: "AuditLogs",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                schema: "core",
                table: "AuditLogs",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                schema: "core",
                table: "AuditLogs",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_Companies_CompanyId",
                schema: "acc",
                table: "BankAccounts",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_Tenants_TenantId",
                schema: "acc",
                table: "BankAccounts",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_Users_CreatedBy",
                schema: "acc",
                table: "BankAccounts",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_Users_DeletedBy",
                schema: "acc",
                table: "BankAccounts",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_Users_UpdatedBy",
                schema: "acc",
                table: "BankAccounts",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxes_Companies_CompanyId",
                schema: "acc",
                table: "CashBoxes",
                column: "CompanyId",
                principalSchema: "core",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxes_Tenants_TenantId",
                schema: "acc",
                table: "CashBoxes",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxes_Users_CreatedBy",
                schema: "acc",
                table: "CashBoxes",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxes_Users_DeletedBy",
                schema: "acc",
                table: "CashBoxes",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CashBoxes_Users_UpdatedBy",
                schema: "acc",
                table: "CashBoxes",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Tenants_TenantId",
                schema: "core",
                table: "Companies",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Users_CreatedBy",
                schema: "core",
                table: "Companies",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Users_DeletedBy",
                schema: "core",
                table: "Companies",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Users_UpdatedBy",
                schema: "core",
                table: "Companies",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_Tenants_TenantId",
                schema: "cfg",
                table: "CompanySettings",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_Users_CreatedBy",
                schema: "cfg",
                table: "CompanySettings",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_Users_DeletedBy",
                schema: "cfg",
                table: "CompanySettings",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_Users_UpdatedBy",
                schema: "cfg",
                table: "CompanySettings",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCentres_Tenants_TenantId",
                schema: "acc",
                table: "CostCentres",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCentres_Users_CreatedBy",
                schema: "acc",
                table: "CostCentres",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCentres_Users_DeletedBy",
                schema: "acc",
                table: "CostCentres",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCentres_Users_UpdatedBy",
                schema: "acc",
                table: "CostCentres",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFieldDefinitions_Tenants_TenantId",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFieldDefinitions_Users_CreatedBy",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFieldDefinitions_Users_DeletedBy",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFieldDefinitions_Users_UpdatedBy",
                schema: "cfg",
                table: "CustomFieldDefinitions",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_SavedReports_SavedReportId",
                schema: "cfg",
                table: "DashboardPins",
                column: "SavedReportId",
                principalSchema: "cfg",
                principalTable: "SavedReports",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_Tenants_TenantId",
                schema: "cfg",
                table: "DashboardPins",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_Users_CreatedBy",
                schema: "cfg",
                table: "DashboardPins",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_Users_DeletedBy",
                schema: "cfg",
                table: "DashboardPins",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_Users_UpdatedBy",
                schema: "cfg",
                table: "DashboardPins",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardPins_Users_UserId",
                schema: "cfg",
                table: "DashboardPins",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EInvoiceSettings_Tenants_TenantId",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EInvoiceSettings_Users_CreatedBy",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EInvoiceSettings_Users_DeletedBy",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EInvoiceSettings_Users_UpdatedBy",
                schema: "cfg",
                table: "EInvoiceSettings",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_JournalEntries_JournalEntryId",
                schema: "acc",
                table: "EntryLines",
                column: "JournalEntryId",
                principalSchema: "acc",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_Tenants_TenantId",
                schema: "acc",
                table: "EntryLines",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_Users_CreatedBy",
                schema: "acc",
                table: "EntryLines",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_Users_DeletedBy",
                schema: "acc",
                table: "EntryLines",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EntryLines_Users_UpdatedBy",
                schema: "acc",
                table: "EntryLines",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Tenants_TenantId",
                schema: "cfg",
                table: "ImportBatches",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Users_CreatedBy",
                schema: "cfg",
                table: "ImportBatches",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Users_DeletedBy",
                schema: "cfg",
                table: "ImportBatches",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Users_ImportedBy",
                schema: "cfg",
                table: "ImportBatches",
                column: "ImportedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_Users_UpdatedBy",
                schema: "cfg",
                table: "ImportBatches",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Roles_RoleId",
                schema: "core",
                table: "Invitations",
                column: "RoleId",
                principalSchema: "core",
                principalTable: "Roles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Tenants_TenantId",
                schema: "core",
                table: "Invitations",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_CreatedBy",
                schema: "core",
                table: "Invitations",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_DeletedBy",
                schema: "core",
                table: "Invitations",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_InvitedBy",
                schema: "core",
                table: "Invitations",
                column: "InvitedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Users_UpdatedBy",
                schema: "core",
                table: "Invitations",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_JournalExportBatches_ExportBatchId",
                schema: "acc",
                table: "JournalEntries",
                column: "ExportBatchId",
                principalSchema: "acc",
                principalTable: "JournalExportBatches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Tenants_TenantId",
                schema: "acc",
                table: "JournalEntries",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Users_CreatedBy",
                schema: "acc",
                table: "JournalEntries",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Users_DeletedBy",
                schema: "acc",
                table: "JournalEntries",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Users_UpdatedBy",
                schema: "acc",
                table: "JournalEntries",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Users_VoidedBy",
                schema: "acc",
                table: "JournalEntries",
                column: "VoidedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalExportBatches_Tenants_TenantId",
                schema: "acc",
                table: "JournalExportBatches",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalExportBatches_Users_CreatedBy",
                schema: "acc",
                table: "JournalExportBatches",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalExportBatches_Users_DeletedBy",
                schema: "acc",
                table: "JournalExportBatches",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalExportBatches_Users_ExportedBy",
                schema: "acc",
                table: "JournalExportBatches",
                column: "ExportedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalExportBatches_Users_UpdatedBy",
                schema: "acc",
                table: "JournalExportBatches",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceLogs_Tenants_TenantId",
                schema: "core",
                table: "MaintenanceLogs",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceLogs_Users_UserId",
                schema: "core",
                table: "MaintenanceLogs",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Tenants_TenantId",
                schema: "core",
                table: "Notifications",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_CreatedBy",
                schema: "core",
                table: "Notifications",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_DeletedBy",
                schema: "core",
                table: "Notifications",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_UpdatedBy",
                schema: "core",
                table: "Notifications",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_UserId",
                schema: "core",
                table: "Notifications",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NumberSequences_Tenants_TenantId",
                schema: "cfg",
                table: "NumberSequences",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NumberSequences_Users_CreatedBy",
                schema: "cfg",
                table: "NumberSequences",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NumberSequences_Users_DeletedBy",
                schema: "cfg",
                table: "NumberSequences",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NumberSequences_Users_UpdatedBy",
                schema: "cfg",
                table: "NumberSequences",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPostings_Tenants_TenantId",
                schema: "acc",
                table: "PendingPostings",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPostings_Users_CreatedBy",
                schema: "acc",
                table: "PendingPostings",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPostings_Users_DeletedBy",
                schema: "acc",
                table: "PendingPostings",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingPostings_Users_UpdatedBy",
                schema: "acc",
                table: "PendingPostings",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Periods_Tenants_TenantId",
                schema: "acc",
                table: "Periods",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Periods_Users_ClosedBy",
                schema: "acc",
                table: "Periods",
                column: "ClosedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Periods_Users_CreatedBy",
                schema: "acc",
                table: "Periods",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Periods_Users_DeletedBy",
                schema: "acc",
                table: "Periods",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Periods_Users_UpdatedBy",
                schema: "acc",
                table: "Periods",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Users_CreatedBy",
                schema: "core",
                table: "Permissions",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Users_DeletedBy",
                schema: "core",
                table: "Permissions",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Permissions_Users_UpdatedBy",
                schema: "core",
                table: "Permissions",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Users_CreatedBy",
                schema: "core",
                table: "Plans",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Users_DeletedBy",
                schema: "core",
                table: "Plans",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Plans_Users_UpdatedBy",
                schema: "core",
                table: "Plans",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingKeyMappings_Tenants_TenantId",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingKeyMappings_Users_CreatedBy",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingKeyMappings_Users_DeletedBy",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingKeyMappings_Users_UpdatedBy",
                schema: "acc",
                table: "PostingKeyMappings",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordLocks_Tenants_TenantId",
                schema: "cfg",
                table: "RecordLocks",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordLocks_Users_CreatedBy",
                schema: "cfg",
                table: "RecordLocks",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordLocks_Users_DeletedBy",
                schema: "cfg",
                table: "RecordLocks",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordLocks_Users_UpdatedBy",
                schema: "cfg",
                table: "RecordLocks",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordLocks_Users_UserId",
                schema: "cfg",
                table: "RecordLocks",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Roles_RoleId",
                schema: "core",
                table: "RolePermissions",
                column: "RoleId",
                principalSchema: "core",
                principalTable: "Roles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Tenants_TenantId",
                schema: "core",
                table: "RolePermissions",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Users_CreatedBy",
                schema: "core",
                table: "RolePermissions",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Users_DeletedBy",
                schema: "core",
                table: "RolePermissions",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Users_UpdatedBy",
                schema: "core",
                table: "RolePermissions",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Tenants_TenantId",
                schema: "core",
                table: "Roles",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Users_CreatedBy",
                schema: "core",
                table: "Roles",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Users_DeletedBy",
                schema: "core",
                table: "Roles",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Users_UpdatedBy",
                schema: "core",
                table: "Roles",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReports_Tenants_TenantId",
                schema: "cfg",
                table: "SavedReports",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReports_Users_CreatedBy",
                schema: "cfg",
                table: "SavedReports",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReports_Users_DeletedBy",
                schema: "cfg",
                table: "SavedReports",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReports_Users_OwnerUserId",
                schema: "cfg",
                table: "SavedReports",
                column: "OwnerUserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReports_Users_UpdatedBy",
                schema: "cfg",
                table: "SavedReports",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StandardFieldConfigs_Tenants_TenantId",
                schema: "cfg",
                table: "StandardFieldConfigs",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StandardFieldConfigs_Users_CreatedBy",
                schema: "cfg",
                table: "StandardFieldConfigs",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StandardFieldConfigs_Users_DeletedBy",
                schema: "cfg",
                table: "StandardFieldConfigs",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StandardFieldConfigs_Users_UpdatedBy",
                schema: "cfg",
                table: "StandardFieldConfigs",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionInvoices_Tenants_TenantId",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionInvoices_Users_CreatedBy",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionInvoices_Users_DeletedBy",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionInvoices_Users_UpdatedBy",
                schema: "core",
                table: "SubscriptionInvoices",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Tenants_TenantId",
                schema: "core",
                table: "SupportAccessGrants",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Users_ApprovedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "ApprovedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Users_CreatedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Users_DeletedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Users_RequestedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "RequestedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAccessGrants_Users_UpdatedBy",
                schema: "core",
                table: "SupportAccessGrants",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TableColumnViews_Tenants_TenantId",
                schema: "cfg",
                table: "TableColumnViews",
                column: "TenantId",
                principalSchema: "core",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TableColumnViews_Users_CreatedBy",
                schema: "cfg",
                table: "TableColumnViews",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TableColumnViews_Users_DeletedBy",
                schema: "cfg",
                table: "TableColumnViews",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TableColumnViews_Users_UpdatedBy",
                schema: "cfg",
                table: "TableColumnViews",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TableColumnViews_Users_UserId",
                schema: "cfg",
                table: "TableColumnViews",
                column: "UserId",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_CreatedBy",
                schema: "core",
                table: "Tenants",
                column: "CreatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_DeletedBy",
                schema: "core",
                table: "Tenants",
                column: "DeletedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Users_UpdatedBy",
                schema: "core",
                table: "Tenants",
                column: "UpdatedBy",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_DefaultCompanyId",
                schema: "core",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tenants_TenantId",
                schema: "core",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Announcements",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ApArDocumentLines",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "ApArEInvoiceSubmissions",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "ApArPayments",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "CompanySettings",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "DashboardPins",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "EInvoiceSettings",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "EntryLines",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "MaintenanceLogs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "core");

            migrationBuilder.DropTable(
                name: "NumberSequences",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "PendingPostings",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "Periods",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "PostingKeyMappings",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "RecordLocks",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "RolePermissions",
                schema: "core");

            migrationBuilder.DropTable(
                name: "StandardFieldConfigs",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "SubscriptionInvoices",
                schema: "core");

            migrationBuilder.DropTable(
                name: "SupportAccessGrants",
                schema: "core");

            migrationBuilder.DropTable(
                name: "TableColumnViews",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "TermsAcceptances",
                schema: "core");

            migrationBuilder.DropTable(
                name: "TRN_Advances",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_CreditNoteLines",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_EInvoiceSubmissions",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_InvoiceLayouts",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_MonthlySiteRates",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_PartnerPayments",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_RateHistory",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Receipts",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_SettlementDeductions",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Tariffs",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "UserCompanyRoles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "UserRecoveryCodes",
                schema: "core");

            migrationBuilder.DropTable(
                name: "UserSessions",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ApArReturns",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "SavedReports",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "CostCentres",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "core");

            migrationBuilder.DropTable(
                name: "TRN_InvoiceLines",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_CreditNotes",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ApArDocuments",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "TRN_Trips",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "ImportBatches",
                schema: "cfg");

            migrationBuilder.DropTable(
                name: "TRN_Invoices",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_LoadingSites",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Materials",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Settlements",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_UnloadingSites",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Customers",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "BankAccounts",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "CashBoxes",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "JournalEntries",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "TRN_Drivers",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "ApArParties",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "JournalExportBatches",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "TRN_OwnershipTypes",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "TRN_Partners",
                schema: "trn");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "acc");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Plans",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "core");
        }
    }
}
