IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF SCHEMA_ID(N'acc') IS NULL EXEC(N'CREATE SCHEMA [acc];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF SCHEMA_ID(N'cfg') IS NULL EXEC(N'CREATE SCHEMA [cfg];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF SCHEMA_ID(N'trn') IS NULL EXEC(N'CREATE SCHEMA [trn];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[Accounts] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Accounts_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Code] varchar(30) NOT NULL,
        [NameAr] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NULL,
        [AccountType] varchar(10) NOT NULL,
        [ParentId] bigint NULL,
        [IsPostable] bit NOT NULL CONSTRAINT [DF_Accounts_IsPostable] DEFAULT (1),
        [OpeningDebit] decimal(18,3) NOT NULL CONSTRAINT [DF_Accounts_OpeningDebit] DEFAULT (0),
        [OpeningCredit] decimal(18,3) NOT NULL CONSTRAINT [DF_Accounts_OpeningCredit] DEFAULT (0),
        [IsControlAccount] bit NOT NULL CONSTRAINT [DF_Accounts_IsControlAccount] DEFAULT (0),
        [ControlPartyType] varchar(10) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_Accounts_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Accounts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Accounts_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Accounts_AccountType] CHECK ([AccountType] IN ('Asset','Liability','Equity','Revenue','Expense')),
        CONSTRAINT [CK_Accounts_ControlPartyType] CHECK ([ControlPartyType] IN ('Customer','Driver','Partner','Supplier')),
        CONSTRAINT [CK_Accounts_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Accounts_OpeningCredit] CHECK ([OpeningCredit] >= 0),
        CONSTRAINT [CK_Accounts_OpeningDebit] CHECK ([OpeningDebit] >= 0),
        CONSTRAINT [CK_Accounts_OpeningDebit_OpeningCredit] CHECK (NOT ([OpeningDebit] > 0 AND [OpeningCredit] > 0)),
        CONSTRAINT [CK_Accounts_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_Accounts_Accounts_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [acc].[Accounts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Announcements] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Announcements_PublicId] DEFAULT (NEWID()),
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [StartsAt] datetime2 NOT NULL,
        [EndsAt] datetime2 NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Announcements_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Announcements_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Announcements_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Announcements_EndsAt] CHECK ([EndsAt] > [StartsAt])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArDocumentLines] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArDocumentLines_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DocumentId] bigint NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [AccountId] bigint NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [TaxRate] decimal(5,2) NOT NULL,
        [CostCentreId] bigint NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArDocumentLines_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArDocumentLines_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArDocumentLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArDocumentLines_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_ApArDocumentLines_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArDocumentLines_TaxRate] CHECK ([TaxRate] BETWEEN 0 AND 100),
        CONSTRAINT [FK_ApArDocumentLines_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [acc].[Accounts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArDocuments] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArDocuments_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [DocumentKind] varchar(15) NOT NULL,
        [PartyId] bigint NOT NULL,
        [DocumentDate] date NOT NULL,
        [DueDate] date NOT NULL,
        [Reference] nvarchar(100) NULL,
        [Subtotal] decimal(18,3) NOT NULL,
        [Tax] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArDocuments_Tax] DEFAULT (0),
        [Total] decimal(18,3) NOT NULL,
        [PaidAmount] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArDocuments_PaidAmount] DEFAULT (0),
        [ReturnedAmount] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArDocuments_ReturnedAmount] DEFAULT (0),
        [RemainingAmount] decimal(18,3) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_ApArDocuments_Status] DEFAULT ('Posted'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArDocuments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArDocuments_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArDocuments_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArDocuments_DocumentKind] CHECK ([DocumentKind] IN ('SupplierBill','Receivable')),
        CONSTRAINT [CK_ApArDocuments_DueDate] CHECK ([DueDate] >= [DocumentDate]),
        CONSTRAINT [CK_ApArDocuments_PaidAmount] CHECK ([PaidAmount] >= 0),
        CONSTRAINT [CK_ApArDocuments_RemainingAmount] CHECK ([RemainingAmount] >= 0),
        CONSTRAINT [CK_ApArDocuments_ReturnedAmount] CHECK ([ReturnedAmount] >= 0),
        CONSTRAINT [CK_ApArDocuments_Status] CHECK ([Status] IN ('Posted','Void')),
        CONSTRAINT [CK_ApArDocuments_Subtotal] CHECK ([Subtotal] >= 0),
        CONSTRAINT [CK_ApArDocuments_Tax] CHECK ([Tax] >= 0),
        CONSTRAINT [CK_ApArDocuments_Total] CHECK ([Total] >= 0),
        CONSTRAINT [CK_ApArDocuments_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArEInvoiceSubmissions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArEInvoiceSubmissions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DocumentType] varchar(10) NOT NULL,
        [ApArDocumentId] bigint NULL,
        [ApArReturnId] bigint NULL,
        [Status] varchar(12) NOT NULL CONSTRAINT [DF_ApArEInvoiceSubmissions_Status] DEFAULT ('Queued'),
        [Icv] bigint NULL,
        [UniqueId] uniqueidentifier NULL,
        [SystemNumber] varchar(50) NULL,
        [QrCode] nvarchar(max) NULL,
        [RequestPayload] nvarchar(max) NULL,
        [ResponsePayload] nvarchar(max) NULL,
        [Attempts] int NOT NULL CONSTRAINT [DF_ApArEInvoiceSubmissions_Attempts] DEFAULT (0),
        [LastError] nvarchar(1000) NULL,
        [SubmittedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArEInvoiceSubmissions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArEInvoiceSubmissions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArEInvoiceSubmissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArEInvoiceSubmissions_Attempts] CHECK ([Attempts] >= 0),
        CONSTRAINT [CK_ApArEInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArEInvoiceSubmissions_DocumentType] CHECK ([DocumentType] IN ('Invoice','CreditNote')),
        CONSTRAINT [CK_ApArEInvoiceSubmissions_Status] CHECK ([Status] IN ('Queued','Submitted','Rejected','NotRequired')),
        CONSTRAINT [FK_ApArEInvoiceSubmissions_ApArDocuments_ApArDocumentId] FOREIGN KEY ([ApArDocumentId]) REFERENCES [acc].[ApArDocuments] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArParties] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArParties_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [PartyRole] varchar(10) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [TaxNumber] varchar(30) NULL,
        [Phone] varchar(20) NULL,
        [Address] nvarchar(300) NULL,
        [ControlAccountId] bigint NOT NULL,
        [PaymentTermsDays] int NOT NULL CONSTRAINT [DF_ApArParties_PaymentTermsDays] DEFAULT (0),
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_ApArParties_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArParties_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArParties_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArParties] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArParties_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArParties_PartyRole] CHECK ([PartyRole] IN ('Supplier','Customer')),
        CONSTRAINT [CK_ApArParties_PaymentTermsDays] CHECK ([PaymentTermsDays] >= 0),
        CONSTRAINT [CK_ApArParties_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_ApArParties_Accounts_ControlAccountId] FOREIGN KEY ([ControlAccountId]) REFERENCES [acc].[Accounts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArPayments] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArPayments_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [DocumentId] bigint NOT NULL,
        [PaymentDate] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL,
        [CashBoxId] bigint NULL,
        [BankAccountId] bigint NULL,
        [Reference] nvarchar(100) NULL,
        [AttachmentsCount] int NOT NULL CONSTRAINT [DF_ApArPayments_AttachmentsCount] DEFAULT (0),
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_ApArPayments_Status] DEFAULT ('Active'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArPayments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArPayments_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArPayments_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_ApArPayments_AttachmentsCount] CHECK ([AttachmentsCount] >= 0),
        CONSTRAINT [CK_ApArPayments_CashBoxId_BankAccountId] CHECK (([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)),
        CONSTRAINT [CK_ApArPayments_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArPayments_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_ApArPayments_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_ApArPayments_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_ApArPayments_ApArDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [acc].[ApArDocuments] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[ApArReturns] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ApArReturns_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [DocumentId] bigint NOT NULL,
        [ReturnDate] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [Tax] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArReturns_Tax] DEFAULT (0),
        [Total] decimal(18,3) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_ApArReturns_Status] DEFAULT ('Posted'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ApArReturns_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ApArReturns_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ApArReturns] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApArReturns_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_ApArReturns_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ApArReturns_Status] CHECK ([Status] IN ('Posted','Void')),
        CONSTRAINT [CK_ApArReturns_Tax] CHECK ([Tax] >= 0),
        CONSTRAINT [CK_ApArReturns_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_ApArReturns_ApArDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [acc].[ApArDocuments] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [TenantId] bigint NULL,
        [CompanyId] bigint NULL,
        [UserId] bigint NULL,
        [OccurredAt] datetime2 NOT NULL CONSTRAINT [DF_AuditLogs_OccurredAt] DEFAULT (SYSUTCDATETIME()),
        [Action] varchar(50) NOT NULL,
        [EntityType] varchar(60) NULL,
        [EntityId] bigint NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [IpAddress] varchar(45) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AuditLogs_NewValues] CHECK (ISJSON([NewValues]) = 1),
        CONSTRAINT [CK_AuditLogs_OldValues] CHECK (ISJSON([OldValues]) = 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[BankAccounts] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_BankAccounts_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [BankName] nvarchar(100) NOT NULL,
        [AccountNumber] varchar(40) NULL,
        [Iban] varchar(34) NULL,
        [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_BankAccounts_CurrencyCode] DEFAULT ('JOD'),
        [AccountId] bigint NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_BankAccounts_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_BankAccounts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_BankAccounts_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BankAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BankAccounts_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_BankAccounts_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_BankAccounts_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [acc].[Accounts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[CashBoxes] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_CashBoxes_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [AccountId] bigint NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_CashBoxes_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_CashBoxes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_CashBoxes_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CashBoxes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CashBoxes_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_CashBoxes_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_CashBoxes_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [acc].[Accounts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Companies] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Companies_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [NameEn] nvarchar(200) NULL,
        [TaxNumber] varchar(30) NULL,
        [CommercialRegNo] varchar(30) NULL,
        [Address] nvarchar(300) NULL,
        [Phone] varchar(20) NULL,
        [Email] nvarchar(150) NULL,
        [LogoFile] varbinary(max) NULL,
        [CountryCode] char(2) NOT NULL CONSTRAINT [DF_Companies_CountryCode] DEFAULT ('JO'),
        [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_Companies_CurrencyCode] DEFAULT ('JOD'),
        [CurrencyDecimals] tinyint NOT NULL CONSTRAINT [DF_Companies_CurrencyDecimals] DEFAULT (3),
        [TaxRate] decimal(5,2) NOT NULL CONSTRAINT [DF_Companies_TaxRate] DEFAULT (16.00),
        [TaxInclusive] bit NOT NULL CONSTRAINT [DF_Companies_TaxInclusive] DEFAULT (0),
        [TimeZone] varchar(50) NOT NULL CONSTRAINT [DF_Companies_TimeZone] DEFAULT ('Asia/Amman'),
        [MigrationCompletedAt] datetime2 NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_Companies_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Companies_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Companies_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Companies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Companies_CurrencyDecimals] CHECK ([CurrencyDecimals] BETWEEN 0 AND 3),
        CONSTRAINT [CK_Companies_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Companies_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [CK_Companies_TaxRate] CHECK ([TaxRate] BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[CompanySettings] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_CompanySettings_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SettingKey] varchar(80) NOT NULL,
        [SettingValue] nvarchar(1000) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_CompanySettings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_CompanySettings_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CompanySettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CompanySettings_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_CompanySettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[CostCentres] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_CostCentres_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Code] varchar(30) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_CostCentres_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_CostCentres_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_CostCentres_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CostCentres] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CostCentres_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_CostCentres_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_CostCentres_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[CustomFieldDefinitions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [EntityType] varchar(40) NOT NULL,
        [FieldKey] varchar(50) NOT NULL,
        [LabelAr] nvarchar(100) NOT NULL,
        [LabelEn] nvarchar(100) NULL,
        [FieldType] varchar(10) NOT NULL,
        [Options] nvarchar(max) NULL,
        [DefaultValue] nvarchar(200) NULL,
        [HelpText] nvarchar(300) NULL,
        [IsRequired] bit NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_IsRequired] DEFAULT (0),
        [ShowAsColumn] bit NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_ShowAsColumn] DEFAULT (0),
        [SortOrder] int NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_SortOrder] DEFAULT (0),
        [IsHidden] bit NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_IsHidden] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_CustomFieldDefinitions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CustomFieldDefinitions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CustomFieldDefinitions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_CustomFieldDefinitions_FieldType] CHECK ([FieldType] IN ('Text','Number','Date','List','Checkbox','LongText')),
        CONSTRAINT [CK_CustomFieldDefinitions_Options] CHECK (ISJSON([Options]) = 1),
        CONSTRAINT [FK_CustomFieldDefinitions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[DashboardPins] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_DashboardPins_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [SavedReportId] bigint NOT NULL,
        [Measure] varchar(60) NOT NULL,
        [WarningThreshold] decimal(18,3) NULL,
        [CriticalThreshold] decimal(18,3) NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_DashboardPins_SortOrder] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_DashboardPins_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_DashboardPins_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_DashboardPins] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DashboardPins_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_DashboardPins_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[EInvoiceSettings] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_EInvoiceSettings_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [IsEnabled] bit NOT NULL CONSTRAINT [DF_EInvoiceSettings_IsEnabled] DEFAULT (0),
        [Connector] varchar(30) NOT NULL CONSTRAINT [DF_EInvoiceSettings_Connector] DEFAULT ('JoFotara'),
        [SellerTaxNumber] varchar(30) NULL,
        [RegistrationType] varchar(30) NULL,
        [IncomeSourceSequence] varchar(30) NULL,
        [ClientIdEncrypted] varbinary(512) NULL,
        [SecretKeyEncrypted] varbinary(512) NULL,
        [Environment] varchar(10) NOT NULL CONSTRAINT [DF_EInvoiceSettings_Environment] DEFAULT ('Test'),
        [IcvCounter] bigint NOT NULL CONSTRAINT [DF_EInvoiceSettings_IcvCounter] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_EInvoiceSettings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_EInvoiceSettings_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_EInvoiceSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EInvoiceSettings_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_EInvoiceSettings_Environment] CHECK ([Environment] IN ('Test','Production')),
        CONSTRAINT [FK_EInvoiceSettings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[EntryLines] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_EntryLines_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [JournalEntryId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [AccountId] bigint NOT NULL,
        [Debit] decimal(18,3) NOT NULL CONSTRAINT [DF_EntryLines_Debit] DEFAULT (0),
        [Credit] decimal(18,3) NOT NULL CONSTRAINT [DF_EntryLines_Credit] DEFAULT (0),
        [CostCentreId] bigint NULL,
        [PartyModule] varchar(10) NULL,
        [PartyType] varchar(15) NULL,
        [PartyId] bigint NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_EntryLines_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_EntryLines_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_EntryLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EntryLines_Credit] CHECK ([Credit] >= 0),
        CONSTRAINT [CK_EntryLines_Debit] CHECK ([Debit] >= 0),
        CONSTRAINT [CK_EntryLines_Debit_Credit] CHECK (([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)),
        CONSTRAINT [CK_EntryLines_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_EntryLines_PartyModule] CHECK ([PartyModule] IN ('TRN','ACC')),
        CONSTRAINT [FK_EntryLines_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [acc].[Accounts] ([Id]),
        CONSTRAINT [FK_EntryLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_EntryLines_CostCentres_CostCentreId] FOREIGN KEY ([CostCentreId]) REFERENCES [acc].[CostCentres] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[ImportBatches] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_ImportBatches_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [EntityType] varchar(40) NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Mode] varchar(10) NOT NULL CONSTRAINT [DF_ImportBatches_Mode] DEFAULT ('Strict'),
        [TotalRows] int NOT NULL,
        [ImportedRows] int NOT NULL,
        [RejectedRows] int NOT NULL,
        [ImportedBy] bigint NOT NULL,
        [ImportedAt] datetime2 NOT NULL CONSTRAINT [DF_ImportBatches_ImportedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_ImportBatches_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_ImportBatches_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ImportBatches_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_ImportBatches_ImportedRows] CHECK ([ImportedRows] >= 0),
        CONSTRAINT [CK_ImportBatches_Mode] CHECK ([Mode] IN ('Strict','Tolerant')),
        CONSTRAINT [CK_ImportBatches_TotalRows] CHECK ([TotalRows] >= 0),
        CONSTRAINT [FK_ImportBatches_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Invitations] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Invitations_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Email] nvarchar(150) NOT NULL,
        [RoleId] bigint NOT NULL,
        [TokenHash] nvarchar(200) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [AcceptedAt] datetime2 NULL,
        [InvitedBy] bigint NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Invitations_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Invitations_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Invitations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Invitations_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_Invitations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[JournalEntries] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_JournalEntries_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [EntryDate] date NOT NULL,
        [PeriodMonth] char(7) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [SourceModule] varchar(10) NOT NULL,
        [SourceType] varchar(30) NULL,
        [SourceId] bigint NULL,
        [IsManual] bit NOT NULL CONSTRAINT [DF_JournalEntries_IsManual] DEFAULT (0),
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_JournalEntries_Status] DEFAULT ('Posted'),
        [ReversesEntryId] bigint NULL,
        [ReversedByEntryId] bigint NULL,
        [TotalDebit] decimal(18,3) NOT NULL,
        [TotalCredit] decimal(18,3) NOT NULL,
        [ExportBatchId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_JournalEntries_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_JournalEntries_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_JournalEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_JournalEntries_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_JournalEntries_SourceModule] CHECK ([SourceModule] IN ('TRN','ACC')),
        CONSTRAINT [CK_JournalEntries_Status] CHECK ([Status] IN ('Posted','Reversed','Void')),
        CONSTRAINT [CK_JournalEntries_TotalDebit_TotalCredit] CHECK ([TotalDebit] = [TotalCredit]),
        CONSTRAINT [CK_JournalEntries_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_JournalEntries_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_JournalEntries_JournalEntries_ReversedByEntryId] FOREIGN KEY ([ReversedByEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_JournalEntries_JournalEntries_ReversesEntryId] FOREIGN KEY ([ReversesEntryId]) REFERENCES [acc].[JournalEntries] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[JournalExportBatches] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_JournalExportBatches_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [BatchNo] varchar(30) NOT NULL,
        [PeriodFrom] char(7) NOT NULL,
        [PeriodTo] char(7) NOT NULL,
        [Format] varchar(5) NOT NULL,
        [EntryCount] int NOT NULL,
        [TotalDebit] decimal(18,3) NOT NULL,
        [TotalCredit] decimal(18,3) NOT NULL,
        [ExportedBy] bigint NOT NULL,
        [ExportedAt] datetime2 NOT NULL CONSTRAINT [DF_JournalExportBatches_ExportedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_JournalExportBatches_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_JournalExportBatches_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_JournalExportBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_JournalExportBatches_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_JournalExportBatches_EntryCount] CHECK ([EntryCount] >= 0),
        CONSTRAINT [CK_JournalExportBatches_Format] CHECK ([Format] IN ('JSON','CSV')),
        CONSTRAINT [CK_JournalExportBatches_TotalDebit] CHECK ([TotalDebit] >= 0),
        CONSTRAINT [FK_JournalExportBatches_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[MaintenanceLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [Action] varchar(40) NOT NULL,
        [RecordCounts] nvarchar(max) NOT NULL,
        [OccurredAt] datetime2 NOT NULL CONSTRAINT [DF_MaintenanceLogs_OccurredAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_MaintenanceLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MaintenanceLogs_RecordCounts] CHECK (ISJSON([RecordCounts]) = 1),
        CONSTRAINT [FK_MaintenanceLogs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Notifications] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Notifications_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [CompanyId] bigint NULL,
        [NotificationType] varchar(40) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [Link] nvarchar(300) NULL,
        [ReadAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Notifications_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Notifications_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Notifications_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_Notifications_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[NumberSequences] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_NumberSequences_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DocumentType] varchar(40) NOT NULL,
        [Prefix] varchar(10) NOT NULL,
        [Digits] tinyint NOT NULL CONSTRAINT [DF_NumberSequences_Digits] DEFAULT (5),
        [NextNumber] bigint NOT NULL CONSTRAINT [DF_NumberSequences_NextNumber] DEFAULT (1),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_NumberSequences_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_NumberSequences_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_NumberSequences] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_NumberSequences_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_NumberSequences_Digits] CHECK ([Digits] BETWEEN 3 AND 10),
        CONSTRAINT [FK_NumberSequences_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[PendingPostings] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_PendingPostings_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SourceModule] varchar(10) NOT NULL,
        [SourceType] varchar(30) NOT NULL,
        [SourceId] bigint NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [Attempts] int NOT NULL CONSTRAINT [DF_PendingPostings_Attempts] DEFAULT (0),
        [LastAttemptAt] datetime2 NULL,
        [ResolvedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_PendingPostings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PendingPostings_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PendingPostings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PendingPostings_Attempts] CHECK ([Attempts] >= 0),
        CONSTRAINT [CK_PendingPostings_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_PendingPostings_SourceModule] CHECK ([SourceModule] IN ('TRN','ACC')),
        CONSTRAINT [FK_PendingPostings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[Periods] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Periods_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [PeriodMonth] char(7) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_Periods_Status] DEFAULT ('Open'),
        [ClosedBy] bigint NULL,
        [ClosedAt] datetime2 NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Periods_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Periods_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Periods] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Periods_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Periods_Status] CHECK ([Status] IN ('Open','Closed')),
        CONSTRAINT [FK_Periods_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Permissions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Permissions_PublicId] DEFAULT (NEWID()),
        [PermissionKey] varchar(80) NOT NULL,
        [GroupName] varchar(50) NOT NULL,
        [DescriptionAr] nvarchar(200) NOT NULL,
        [DescriptionEn] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Permissions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Permissions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Permissions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Plans] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Plans_PublicId] DEFAULT (NEWID()),
        [Code] varchar(30) NOT NULL,
        [NameAr] nvarchar(100) NOT NULL,
        [NameEn] nvarchar(100) NOT NULL,
        [Price] decimal(18,3) NOT NULL,
        [BillingCycle] varchar(10) NOT NULL CONSTRAINT [DF_Plans_BillingCycle] DEFAULT ('Monthly'),
        [MaxCompanies] int NOT NULL,
        [MaxActiveUsers] int NOT NULL,
        [MaxTripsPerMonth] int NOT NULL,
        [MaxStorageMb] int NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_Plans_IsActive] DEFAULT (1),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Plans_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Plans_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Plans] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Plans_BillingCycle] CHECK ([BillingCycle] IN ('Monthly','Annual')),
        CONSTRAINT [CK_Plans_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Plans_MaxActiveUsers] CHECK ([MaxActiveUsers] > 0),
        CONSTRAINT [CK_Plans_MaxCompanies] CHECK ([MaxCompanies] > 0),
        CONSTRAINT [CK_Plans_MaxStorageMb] CHECK ([MaxStorageMb] > 0),
        CONSTRAINT [CK_Plans_MaxTripsPerMonth] CHECK ([MaxTripsPerMonth] > 0),
        CONSTRAINT [CK_Plans_Price] CHECK ([Price] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [acc].[PostingKeyMappings] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_PostingKeyMappings_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [PostingKey] varchar(60) NOT NULL,
        [AccountId] bigint NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_PostingKeyMappings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_PostingKeyMappings_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_PostingKeyMappings] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PostingKeyMappings_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_PostingKeyMappings_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [acc].[Accounts] ([Id]),
        CONSTRAINT [FK_PostingKeyMappings_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[RecordLocks] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_RecordLocks_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [EntityType] varchar(60) NOT NULL,
        [EntityId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [AcquiredAt] datetime2 NOT NULL CONSTRAINT [DF_RecordLocks_AcquiredAt] DEFAULT (SYSUTCDATETIME()),
        [LastActivityAt] datetime2 NOT NULL CONSTRAINT [DF_RecordLocks_LastActivityAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_RecordLocks_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_RecordLocks_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_RecordLocks] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RecordLocks_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_RecordLocks_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[RolePermissions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_RolePermissions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        [PermissionId] bigint NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_RolePermissions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_RolePermissions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RolePermissions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [core].[Permissions] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Roles] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Roles_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsPreset] bit NOT NULL CONSTRAINT [DF_Roles_IsPreset] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Roles_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Roles_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Roles_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[SavedReports] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_SavedReports_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [OwnerUserId] bigint NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Source] varchar(60) NOT NULL,
        [Definition] nvarchar(max) NOT NULL,
        [IsShared] bit NOT NULL CONSTRAINT [DF_SavedReports_IsShared] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_SavedReports_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_SavedReports_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SavedReports] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SavedReports_Definition] CHECK (ISJSON([Definition]) = 1),
        CONSTRAINT [CK_SavedReports_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_SavedReports_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[StandardFieldConfigs] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_StandardFieldConfigs_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [EntityType] varchar(40) NOT NULL,
        [FieldKey] varchar(50) NOT NULL,
        [Label] nvarchar(100) NULL,
        [IsHidden] bit NOT NULL CONSTRAINT [DF_StandardFieldConfigs_IsHidden] DEFAULT (0),
        [IsRequired] bit NOT NULL CONSTRAINT [DF_StandardFieldConfigs_IsRequired] DEFAULT (0),
        [SortOrder] int NOT NULL CONSTRAINT [DF_StandardFieldConfigs_SortOrder] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_StandardFieldConfigs_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_StandardFieldConfigs_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_StandardFieldConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StandardFieldConfigs_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_StandardFieldConfigs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[SubscriptionInvoices] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_SubscriptionInvoices_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [PeriodStart] date NOT NULL,
        [PeriodEnd] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [Tax] decimal(18,3) NOT NULL CONSTRAINT [DF_SubscriptionInvoices_Tax] DEFAULT (0),
        [Total] decimal(18,3) NOT NULL,
        [Status] varchar(15) NOT NULL CONSTRAINT [DF_SubscriptionInvoices_Status] DEFAULT ('Due'),
        [PaidAt] datetime2 NULL,
        [GatewayReference] nvarchar(100) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_SubscriptionInvoices_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_SubscriptionInvoices_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SubscriptionInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SubscriptionInvoices_Amount] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_SubscriptionInvoices_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_SubscriptionInvoices_PeriodEnd] CHECK ([PeriodEnd] >= [PeriodStart]),
        CONSTRAINT [CK_SubscriptionInvoices_Status] CHECK ([Status] IN ('Due','Paid','Failed','Credited')),
        CONSTRAINT [CK_SubscriptionInvoices_Tax] CHECK ([Tax] >= 0),
        CONSTRAINT [CK_SubscriptionInvoices_Total] CHECK ([Total] = [Amount] + [Tax])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[SupportAccessGrants] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_SupportAccessGrants_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [RequestedBy] bigint NOT NULL,
        [ApprovedBy] bigint NULL,
        [Reason] nvarchar(500) NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_SupportAccessGrants_Status] DEFAULT ('Requested'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_SupportAccessGrants_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_SupportAccessGrants_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_SupportAccessGrants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SupportAccessGrants_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_SupportAccessGrants_EndsAt] CHECK ([EndsAt] > [StartsAt]),
        CONSTRAINT [CK_SupportAccessGrants_Status] CHECK ([Status] IN ('Requested','Approved','Rejected','Expired'))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [cfg].[TableColumnViews] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TableColumnViews_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [UserId] bigint NULL,
        [ViewKey] varchar(80) NOT NULL,
        [Columns] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TableColumnViews_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TableColumnViews_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TableColumnViews] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TableColumnViews_Columns] CHECK (ISJSON([Columns]) = 1),
        CONSTRAINT [CK_TableColumnViews_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_TableColumnViews_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Tenants] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Tenants_PublicId] DEFAULT (NEWID()),
        [Name] nvarchar(200) NOT NULL,
        [Status] varchar(15) NOT NULL CONSTRAINT [DF_Tenants_Status] DEFAULT ('Trial'),
        [PlanId] bigint NOT NULL,
        [BillingCycle] varchar(10) NOT NULL CONSTRAINT [DF_Tenants_BillingCycle] DEFAULT ('Monthly'),
        [TrialEndsAt] datetime2 NULL,
        [CurrentPeriodEnd] datetime2 NULL,
        [GraceEndsAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [RetentionEndsAt] datetime2 NULL,
        [PasswordMinLength] tinyint NOT NULL CONSTRAINT [DF_Tenants_PasswordMinLength] DEFAULT (8),
        [PasswordRequireLetter] bit NOT NULL CONSTRAINT [DF_Tenants_PasswordRequireLetter] DEFAULT (1),
        [PasswordRequireDigit] bit NOT NULL CONSTRAINT [DF_Tenants_PasswordRequireDigit] DEFAULT (1),
        [SessionIdleTimeoutMinutes] smallint NOT NULL CONSTRAINT [DF_Tenants_SessionIdleTimeoutMinutes] DEFAULT (30),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Tenants_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Tenants_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Tenants_BillingCycle] CHECK ([BillingCycle] IN ('Monthly','Annual')),
        CONSTRAINT [CK_Tenants_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Tenants_PasswordMinLength] CHECK ([PasswordMinLength] BETWEEN 8 AND 64),
        CONSTRAINT [CK_Tenants_SessionIdleTimeoutMinutes] CHECK ([SessionIdleTimeoutMinutes] BETWEEN 5 AND 1440),
        CONSTRAINT [CK_Tenants_Status] CHECK ([Status] IN ('Trial','Active','PastDue','Suspended','Cancelled','Deleted')),
        CONSTRAINT [FK_Tenants_Plans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [core].[Plans] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[Users] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_Users_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NULL,
        [FullName] nvarchar(150) NOT NULL,
        [Email] nvarchar(150) NOT NULL,
        [NormalizedEmail] nvarchar(150) NOT NULL,
        [EmailConfirmedAt] datetime2 NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [SecurityStamp] nvarchar(100) NOT NULL,
        [IsPlatformStaff] bit NOT NULL CONSTRAINT [DF_Users_IsPlatformStaff] DEFAULT (0),
        [TwoFactorSecret] varbinary(256) NULL,
        [TwoFactorEnabledAt] datetime2 NULL,
        [LanguageCode] char(2) NOT NULL CONSTRAINT [DF_Users_LanguageCode] DEFAULT ('ar'),
        [DefaultCompanyId] bigint NULL,
        [DateFormat] varchar(20) NOT NULL CONSTRAINT [DF_Users_DateFormat] DEFAULT ('dd/MM/yyyy'),
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_Users_Status] DEFAULT ('Active'),
        [FailedSignInCount] int NOT NULL CONSTRAINT [DF_Users_FailedSignInCount] DEFAULT (0),
        [LockedUntil] datetime2 NULL,
        [LastSignInAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_Users_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_Users_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Users_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_Users_IsPlatformStaff_TenantId] CHECK (([IsPlatformStaff] = 1 AND [TenantId] IS NULL) OR ([IsPlatformStaff] = 0 AND [TenantId] IS NOT NULL)),
        CONSTRAINT [CK_Users_LanguageCode] CHECK ([LanguageCode] IN ('ar','en')),
        CONSTRAINT [CK_Users_Status] CHECK ([Status] IN ('Active','Disabled','Archived')),
        CONSTRAINT [FK_Users_Companies_DefaultCompanyId] FOREIGN KEY ([DefaultCompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_Users_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_Users_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_Users_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[TermsAcceptances] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TermsAcceptances_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [DocumentType] varchar(20) NOT NULL,
        [DocumentVersion] varchar(20) NOT NULL,
        [AcceptedAt] datetime2 NOT NULL CONSTRAINT [DF_TermsAcceptances_AcceptedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TermsAcceptances_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TermsAcceptances_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TermsAcceptances] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TermsAcceptances_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TermsAcceptances_DocumentType] CHECK ([DocumentType] IN ('Terms','Privacy')),
        CONSTRAINT [FK_TermsAcceptances_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TermsAcceptances_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TermsAcceptances_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TermsAcceptances_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TermsAcceptances_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Customers] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Customers_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [TaxNumber] varchar(30) NULL,
        [RegistrationNo] varchar(30) NULL,
        [Phone] varchar(20) NULL,
        [Email] nvarchar(150) NULL,
        [Address] nvarchar(300) NULL,
        [City] nvarchar(100) NULL,
        [ContactPerson] nvarchar(150) NULL,
        [PaymentTermsDays] int NOT NULL CONSTRAINT [DF_TRN_Customers_PaymentTermsDays] DEFAULT (0),
        [ApArPartyId] bigint NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Customers_Status] DEFAULT ('Active'),
        [Notes] nvarchar(1000) NULL,
        [CustomValues] nvarchar(max) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Customers_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Customers_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Customers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Customers_CustomValues] CHECK (ISJSON([CustomValues]) = 1),
        CONSTRAINT [CK_TRN_Customers_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Customers_PaymentTermsDays] CHECK ([PaymentTermsDays] >= 0),
        CONSTRAINT [CK_TRN_Customers_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_Customers_ApArParties_ApArPartyId] FOREIGN KEY ([ApArPartyId]) REFERENCES [acc].[ApArParties] ([Id]),
        CONSTRAINT [FK_TRN_Customers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Customers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Customers_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Customers_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Customers_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_InvoiceLayouts] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [LayoutType] varchar(10) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [BandText] nvarchar(300) NULL,
        [Grouping] varchar(20) NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_Grouping] DEFAULT ('LoadingSite'),
        [AnnexColumns] nvarchar(max) NOT NULL,
        [Labels] nvarchar(max) NULL,
        [FooterText] nvarchar(500) NULL,
        [Signatures] nvarchar(max) NULL,
        [ShowLogo] bit NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_ShowLogo] DEFAULT (1),
        [ShowQr] bit NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_ShowQr] DEFAULT (1),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_InvoiceLayouts_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_InvoiceLayouts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_InvoiceLayouts_AnnexColumns] CHECK (ISJSON([AnnexColumns]) = 1),
        CONSTRAINT [CK_TRN_InvoiceLayouts_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_InvoiceLayouts_Grouping] CHECK ([Grouping] IN ('LoadingSite','Route','Material')),
        CONSTRAINT [CK_TRN_InvoiceLayouts_Labels] CHECK (ISJSON([Labels]) = 1),
        CONSTRAINT [CK_TRN_InvoiceLayouts_LayoutType] CHECK ([LayoutType] IN ('Claim','TaxInvoice')),
        CONSTRAINT [CK_TRN_InvoiceLayouts_Signatures] CHECK (ISJSON([Signatures]) = 1),
        CONSTRAINT [FK_TRN_InvoiceLayouts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLayouts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLayouts_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLayouts_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLayouts_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_LoadingSites] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_LoadingSites_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Area] nvarchar(100) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_LoadingSites_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_LoadingSites_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_LoadingSites_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_LoadingSites] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_LoadingSites_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_LoadingSites_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_LoadingSites_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_LoadingSites_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_LoadingSites_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_LoadingSites_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_LoadingSites_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Materials] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Materials_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Unit] nvarchar(20) NOT NULL CONSTRAINT [DF_TRN_Materials_Unit] DEFAULT (N'ton'),
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Materials_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Materials_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Materials_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Materials] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Materials_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Materials_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_Materials_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Materials_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Materials_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Materials_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Materials_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_OwnershipTypes] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_OwnershipTypes_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [FleetClass] varchar(10) NOT NULL,
        [IsBuiltIn] bit NOT NULL CONSTRAINT [DF_TRN_OwnershipTypes_IsBuiltIn] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_OwnershipTypes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_OwnershipTypes_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_OwnershipTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_OwnershipTypes_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_OwnershipTypes_FleetClass] CHECK ([FleetClass] IN ('Own','External')),
        CONSTRAINT [FK_TRN_OwnershipTypes_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_OwnershipTypes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_OwnershipTypes_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_OwnershipTypes_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_OwnershipTypes_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Partners] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Partners_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [TaxNumber] varchar(30) NULL,
        [Phone] varchar(20) NULL,
        [Address] nvarchar(300) NULL,
        [ContactPerson] nvarchar(150) NULL,
        [BankName] nvarchar(100) NULL,
        [Iban] varchar(34) NULL,
        [CliqAlias] nvarchar(50) NULL,
        [Notes] nvarchar(1000) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Partners_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Partners_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Partners_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Partners] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Partners_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Partners_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_Partners_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Partners_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Partners_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Partners_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Partners_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_RateHistory] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_RateHistory_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SourceType] varchar(15) NOT NULL,
        [SourceId] bigint NOT NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NOT NULL,
        [ChangedBy] bigint NOT NULL,
        [ChangedAt] datetime2 NOT NULL CONSTRAINT [DF_TRN_RateHistory_ChangedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_RateHistory_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_RateHistory_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_RateHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_RateHistory_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_RateHistory_NewValues] CHECK (ISJSON([NewValues]) = 1),
        CONSTRAINT [CK_TRN_RateHistory_OldValues] CHECK (ISJSON([OldValues]) = 1),
        CONSTRAINT [CK_TRN_RateHistory_SourceType] CHECK ([SourceType] IN ('Tariff','MonthlySiteRate')),
        CONSTRAINT [FK_TRN_RateHistory_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_RateHistory_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_RateHistory_Users_ChangedBy] FOREIGN KEY ([ChangedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_RateHistory_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_RateHistory_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_RateHistory_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_UnloadingSites] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_UnloadingSites_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Area] nvarchar(100) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_UnloadingSites_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_UnloadingSites_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_UnloadingSites_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_UnloadingSites] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_UnloadingSites_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_UnloadingSites_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_UnloadingSites_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_UnloadingSites_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_UnloadingSites_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_UnloadingSites_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_UnloadingSites_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[UserCompanyRoles] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_UserCompanyRoles_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_UserCompanyRoles_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_UserCompanyRoles_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserCompanyRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserCompanyRoles_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_UserCompanyRoles_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [core].[Roles] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserCompanyRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[UserRecoveryCodes] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_UserRecoveryCodes_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NULL,
        [UserId] bigint NOT NULL,
        [CodeHash] nvarchar(200) NOT NULL,
        [UsedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_UserRecoveryCodes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_UserRecoveryCodes_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserRecoveryCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserRecoveryCodes_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_UserRecoveryCodes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_UserRecoveryCodes_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserRecoveryCodes_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserRecoveryCodes_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserRecoveryCodes_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [core].[UserSessions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_UserSessions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NULL,
        [UserId] bigint NOT NULL,
        [TokenHash] nvarchar(200) NOT NULL,
        [LastSeenAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IpAddress] varchar(45) NULL,
        [UserAgent] nvarchar(300) NULL,
        [RevokedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_UserSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_UserSessions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserSessions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_UserSessions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_UserSessions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserSessions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserSessions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_UserSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Invoices] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Invoices_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NULL,
        [InvoiceType] varchar(10) NOT NULL,
        [CustomerId] bigint NOT NULL,
        [InvoiceDate] date NOT NULL,
        [DueDate] date NOT NULL,
        [Subtotal] decimal(18,3) NOT NULL,
        [Discount] decimal(18,3) NOT NULL CONSTRAINT [DF_TRN_Invoices_Discount] DEFAULT (0),
        [ExtraFees] decimal(18,3) NOT NULL CONSTRAINT [DF_TRN_Invoices_ExtraFees] DEFAULT (0),
        [ExtraFeesDescription] nvarchar(200) NULL,
        [BaseAmount] decimal(18,3) NOT NULL,
        [TaxRate] decimal(5,2) NOT NULL,
        [Tax] decimal(18,3) NOT NULL,
        [Total] decimal(18,3) NOT NULL,
        [PaidAmount] decimal(18,3) NOT NULL CONSTRAINT [DF_TRN_Invoices_PaidAmount] DEFAULT (0),
        [ReturnedAmount] decimal(18,3) NOT NULL CONSTRAINT [DF_TRN_Invoices_ReturnedAmount] DEFAULT (0),
        [RemainingAmount] decimal(18,3) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Invoices_Status] DEFAULT ('Draft'),
        [LayoutType] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Invoices_LayoutType] DEFAULT ('Claim'),
        [Notes] nvarchar(1000) NULL,
        [ApprovedBy] bigint NULL,
        [ApprovedAt] datetime2 NULL,
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Invoices_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Invoices_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Invoices_BaseAmount] CHECK ([BaseAmount] = [Subtotal] - [Discount] + [ExtraFees]),
        CONSTRAINT [CK_TRN_Invoices_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Invoices_Discount] CHECK ([Discount] >= 0 AND [Discount] <= [Subtotal]),
        CONSTRAINT [CK_TRN_Invoices_DueDate] CHECK ([DueDate] >= [InvoiceDate]),
        CONSTRAINT [CK_TRN_Invoices_ExtraFees] CHECK ([ExtraFees] >= 0),
        CONSTRAINT [CK_TRN_Invoices_InvoiceType] CHECK ([InvoiceType] IN ('Trip','Manual')),
        CONSTRAINT [CK_TRN_Invoices_LayoutType] CHECK ([LayoutType] IN ('Claim','TaxInvoice')),
        CONSTRAINT [CK_TRN_Invoices_PaidAmount] CHECK ([PaidAmount] >= 0),
        CONSTRAINT [CK_TRN_Invoices_RemainingAmount] CHECK ([RemainingAmount] >= 0),
        CONSTRAINT [CK_TRN_Invoices_ReturnedAmount] CHECK ([ReturnedAmount] >= 0),
        CONSTRAINT [CK_TRN_Invoices_Status] CHECK ([Status] IN ('Draft','Approved','Closed','Void')),
        CONSTRAINT [CK_TRN_Invoices_Subtotal] CHECK ([Subtotal] >= 0),
        CONSTRAINT [CK_TRN_Invoices_Tax] CHECK ([Tax] >= 0),
        CONSTRAINT [CK_TRN_Invoices_TaxRate] CHECK ([TaxRate] BETWEEN 0 AND 100),
        CONSTRAINT [CK_TRN_Invoices_Total] CHECK ([Total] = [BaseAmount] + [Tax]),
        CONSTRAINT [CK_TRN_Invoices_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_Invoices_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_TRN_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [trn].[TRN_Customers] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Users_ApprovedBy] FOREIGN KEY ([ApprovedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Invoices_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_MonthlySiteRates] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_MonthlySiteRates_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [RateMonth] char(7) NOT NULL,
        [LoadingSiteId] bigint NOT NULL,
        [OwnFleetRate] decimal(18,4) NULL,
        [ExternalFleetRate] decimal(18,4) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_MonthlySiteRates_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_MonthlySiteRates_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_MonthlySiteRates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_MonthlySiteRates_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_MonthlySiteRates_ExternalFleetRate] CHECK ([ExternalFleetRate] > 0),
        CONSTRAINT [CK_TRN_MonthlySiteRates_OwnFleetRate] CHECK ([OwnFleetRate] > 0),
        CONSTRAINT [CK_TRN_MonthlySiteRates_OwnFleetRate_ExternalFleetRate] CHECK ([OwnFleetRate] IS NOT NULL OR [ExternalFleetRate] IS NOT NULL),
        CONSTRAINT [CK_TRN_MonthlySiteRates_RateMonth] CHECK ([RateMonth] LIKE '[12][0-9][0-9][0-9]-[01][0-9]'),
        CONSTRAINT [FK_TRN_MonthlySiteRates_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_MonthlySiteRates_TRN_LoadingSites_LoadingSiteId] FOREIGN KEY ([LoadingSiteId]) REFERENCES [trn].[TRN_LoadingSites] ([Id]),
        CONSTRAINT [FK_TRN_MonthlySiteRates_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_MonthlySiteRates_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_MonthlySiteRates_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_MonthlySiteRates_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Drivers] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Drivers_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [NationalId] char(10) NOT NULL,
        [Phone1] varchar(20) NOT NULL,
        [Phone2] varchar(20) NULL,
        [Address] nvarchar(300) NULL,
        [LicenceNo] varchar(30) NOT NULL,
        [LicenceType] nvarchar(30) NOT NULL,
        [LicenceExpiry] date NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Drivers_PaymentMethod] DEFAULT ('Cash'),
        [BankName] nvarchar(100) NULL,
        [Iban] varchar(34) NULL,
        [CliqAlias] nvarchar(50) NULL,
        [PlateNumber] varchar(20) NOT NULL,
        [TruckType] nvarchar(50) NULL,
        [TrailerNo] varchar(20) NULL,
        [CapacityTons] decimal(18,3) NOT NULL,
        [OwnershipTypeId] bigint NOT NULL,
        [PartnerId] bigint NULL,
        [RegistrationExpiry] date NOT NULL,
        [InsuranceExpiry] date NOT NULL,
        [InspectionExpiry] date NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Drivers_Status] DEFAULT ('Active'),
        [Notes] nvarchar(1000) NULL,
        [CustomValues] nvarchar(max) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Drivers_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Drivers_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Drivers] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Drivers_CapacityTons] CHECK ([CapacityTons] > 0),
        CONSTRAINT [CK_TRN_Drivers_CustomValues] CHECK (ISJSON([CustomValues]) = 1),
        CONSTRAINT [CK_TRN_Drivers_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Drivers_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_TRN_Drivers_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [FK_TRN_Drivers_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_TRN_OwnershipTypes_OwnershipTypeId] FOREIGN KEY ([OwnershipTypeId]) REFERENCES [trn].[TRN_OwnershipTypes] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_TRN_Partners_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [trn].[TRN_Partners] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Drivers_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_PartnerPayments] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_PartnerPayments_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [PartnerId] bigint NOT NULL,
        [PaymentDate] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL,
        [CashBoxId] bigint NULL,
        [BankAccountId] bigint NULL,
        [Reference] nvarchar(100) NULL,
        [Notes] nvarchar(500) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_PartnerPayments_Status] DEFAULT ('Active'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_PartnerPayments_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_PartnerPayments_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_PartnerPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_PartnerPayments_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TRN_PartnerPayments_CashBoxId_BankAccountId] CHECK (([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)),
        CONSTRAINT [CK_TRN_PartnerPayments_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_PartnerPayments_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_TRN_PartnerPayments_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_TRN_PartnerPayments_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_PartnerPayments_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [acc].[BankAccounts] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_CashBoxes_CashBoxId] FOREIGN KEY ([CashBoxId]) REFERENCES [acc].[CashBoxes] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_TRN_Partners_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [trn].[TRN_Partners] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_PartnerPayments_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Tariffs] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Tariffs_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [PartyType] varchar(10) NOT NULL,
        [CustomerId] bigint NULL,
        [PartnerId] bigint NULL,
        [MaterialId] bigint NULL,
        [LoadingSiteId] bigint NULL,
        [UnloadingSiteId] bigint NULL,
        [Price] decimal(18,4) NOT NULL,
        [ValidFrom] date NULL,
        [ValidTo] date NULL,
        [Specificity] AS ISNULL(CONVERT(tinyint, CASE WHEN [CustomerId] IS NOT NULL OR [PartnerId] IS NOT NULL THEN 8 ELSE 0 END + CASE WHEN [MaterialId] IS NOT NULL THEN 4 ELSE 0 END + CASE WHEN [LoadingSiteId] IS NOT NULL THEN 2 ELSE 0 END + CASE WHEN [UnloadingSiteId] IS NOT NULL THEN 1 ELSE 0 END), 0) PERSISTED,
        [Notes] nvarchar(500) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Tariffs_Status] DEFAULT ('Active'),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Tariffs_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Tariffs_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Tariffs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Tariffs_CustomerId_PartnerId] CHECK (([PartyType] = 'Customer' AND [PartnerId] IS NULL) OR ([PartyType] = 'Partner' AND [CustomerId] IS NULL)),
        CONSTRAINT [CK_TRN_Tariffs_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Tariffs_PartyType] CHECK ([PartyType] IN ('Customer','Partner')),
        CONSTRAINT [CK_TRN_Tariffs_Price] CHECK ([Price] > 0),
        CONSTRAINT [CK_TRN_Tariffs_Specificity] CHECK ([Specificity] BETWEEN 0 AND 15),
        CONSTRAINT [CK_TRN_Tariffs_Status] CHECK ([Status] IN ('Active','Archived')),
        CONSTRAINT [CK_TRN_Tariffs_ValidTo] CHECK ([ValidTo] >= [ValidFrom]),
        CONSTRAINT [FK_TRN_Tariffs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_TRN_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [trn].[TRN_Customers] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_TRN_LoadingSites_LoadingSiteId] FOREIGN KEY ([LoadingSiteId]) REFERENCES [trn].[TRN_LoadingSites] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_TRN_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [trn].[TRN_Materials] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_TRN_Partners_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [trn].[TRN_Partners] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_TRN_UnloadingSites_UnloadingSiteId] FOREIGN KEY ([UnloadingSiteId]) REFERENCES [trn].[TRN_UnloadingSites] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Tariffs_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_CreditNotes] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_CreditNotes_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [CustomerId] bigint NOT NULL,
        [CreditDate] date NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Subtotal] decimal(18,3) NOT NULL,
        [DiscountShare] decimal(18,3) NOT NULL,
        [FeesShare] decimal(18,3) NOT NULL,
        [Tax] decimal(18,3) NOT NULL,
        [Total] decimal(18,3) NOT NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_CreditNotes_Status] DEFAULT ('Posted'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_CreditNotes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_CreditNotes_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_CreditNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_CreditNotes_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_CreditNotes_Status] CHECK ([Status] IN ('Posted','Void')),
        CONSTRAINT [CK_TRN_CreditNotes_Subtotal] CHECK ([Subtotal] >= 0),
        CONSTRAINT [CK_TRN_CreditNotes_Tax] CHECK ([Tax] >= 0),
        CONSTRAINT [CK_TRN_CreditNotes_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_CreditNotes_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_TRN_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [trn].[TRN_Customers] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_CreditNotes_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Receipts] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Receipts_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [CustomerId] bigint NOT NULL,
        [InvoiceId] bigint NULL,
        [ReceiptDate] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL,
        [CashBoxId] bigint NULL,
        [BankAccountId] bigint NULL,
        [Reference] nvarchar(100) NULL,
        [AttachmentsCount] int NOT NULL CONSTRAINT [DF_TRN_Receipts_AttachmentsCount] DEFAULT (0),
        [Notes] nvarchar(500) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Receipts_Status] DEFAULT ('Active'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Receipts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Receipts_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Receipts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Receipts_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TRN_Receipts_AttachmentsCount] CHECK ([AttachmentsCount] >= 0),
        CONSTRAINT [CK_TRN_Receipts_CashBoxId_BankAccountId] CHECK (([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)),
        CONSTRAINT [CK_TRN_Receipts_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Receipts_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_TRN_Receipts_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_TRN_Receipts_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_Receipts_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [acc].[BankAccounts] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_CashBoxes_CashBoxId] FOREIGN KEY ([CashBoxId]) REFERENCES [acc].[CashBoxes] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_TRN_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [trn].[TRN_Customers] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Receipts_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Settlements] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Settlements_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [DriverId] bigint NOT NULL,
        [SettlementDate] date NOT NULL,
        [GrossWages] decimal(18,3) NOT NULL,
        [AdvancesTotal] decimal(18,3) NOT NULL,
        [DeductionsTotal] decimal(18,3) NOT NULL,
        [NetPayable] decimal(18,3) NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL,
        [CashBoxId] bigint NULL,
        [BankAccountId] bigint NULL,
        [Reference] nvarchar(100) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Settlements_Status] DEFAULT ('Active'),
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Settlements_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Settlements_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Settlements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Settlements_AdvancesTotal] CHECK ([AdvancesTotal] >= 0),
        CONSTRAINT [CK_TRN_Settlements_CashBoxId_BankAccountId] CHECK (([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)),
        CONSTRAINT [CK_TRN_Settlements_DeductionsTotal] CHECK ([DeductionsTotal] >= 0),
        CONSTRAINT [CK_TRN_Settlements_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Settlements_GrossWages] CHECK ([GrossWages] >= 0),
        CONSTRAINT [CK_TRN_Settlements_NetPayable] CHECK ([NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal]),
        CONSTRAINT [CK_TRN_Settlements_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_TRN_Settlements_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_TRN_Settlements_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_Settlements_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [acc].[BankAccounts] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_CashBoxes_CashBoxId] FOREIGN KEY ([CashBoxId]) REFERENCES [acc].[CashBoxes] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_TRN_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [trn].[TRN_Drivers] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Settlements_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_EInvoiceSubmissions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_EInvoiceSubmissions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [DocumentType] varchar(10) NOT NULL,
        [InvoiceId] bigint NULL,
        [CreditNoteId] bigint NULL,
        [Status] varchar(12) NOT NULL CONSTRAINT [DF_TRN_EInvoiceSubmissions_Status] DEFAULT ('Queued'),
        [Icv] bigint NULL,
        [UniqueId] uniqueidentifier NULL,
        [SystemNumber] varchar(50) NULL,
        [QrCode] nvarchar(max) NULL,
        [RequestPayload] nvarchar(max) NULL,
        [ResponsePayload] nvarchar(max) NULL,
        [Attempts] int NOT NULL CONSTRAINT [DF_TRN_EInvoiceSubmissions_Attempts] DEFAULT (0),
        [LastError] nvarchar(1000) NULL,
        [SubmittedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_EInvoiceSubmissions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_EInvoiceSubmissions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_EInvoiceSubmissions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_EInvoiceSubmissions_Attempts] CHECK ([Attempts] >= 0),
        CONSTRAINT [CK_TRN_EInvoiceSubmissions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_EInvoiceSubmissions_DocumentType] CHECK ([DocumentType] IN ('Invoice','CreditNote')),
        CONSTRAINT [CK_TRN_EInvoiceSubmissions_InvoiceId_CreditNoteId] CHECK (([DocumentType] = 'Invoice' AND [InvoiceId] IS NOT NULL AND [CreditNoteId] IS NULL) OR ([DocumentType] = 'CreditNote' AND [CreditNoteId] IS NOT NULL AND [InvoiceId] IS NULL)),
        CONSTRAINT [CK_TRN_EInvoiceSubmissions_Status] CHECK ([Status] IN ('Queued','Submitted','Rejected','NotRequired')),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_TRN_CreditNotes_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [trn].[TRN_CreditNotes] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_EInvoiceSubmissions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Advances] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Advances_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [Number] varchar(30) NOT NULL,
        [DriverId] bigint NOT NULL,
        [AdvanceDate] date NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [PaymentMethod] varchar(10) NOT NULL,
        [CashBoxId] bigint NULL,
        [BankAccountId] bigint NULL,
        [Reference] nvarchar(100) NULL,
        [Notes] nvarchar(500) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Advances_Status] DEFAULT ('Active'),
        [SettlementId] bigint NULL,
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Advances_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Advances_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Advances] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Advances_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TRN_Advances_CashBoxId_BankAccountId] CHECK (([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR ([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)),
        CONSTRAINT [CK_TRN_Advances_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Advances_PaymentMethod] CHECK ([PaymentMethod] IN ('Cash','Bank','CliQ')),
        CONSTRAINT [CK_TRN_Advances_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_TRN_Advances_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_Advances_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [acc].[BankAccounts] ([Id]),
        CONSTRAINT [FK_TRN_Advances_CashBoxes_CashBoxId] FOREIGN KEY ([CashBoxId]) REFERENCES [acc].[CashBoxes] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Advances_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_Advances_TRN_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [trn].[TRN_Drivers] ([Id]),
        CONSTRAINT [FK_TRN_Advances_TRN_Settlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [trn].[TRN_Settlements] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Advances_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_SettlementDeductions] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_SettlementDeductions_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [SettlementId] bigint NOT NULL,
        [DeductionType] varchar(15) NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_SettlementDeductions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_SettlementDeductions_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_SettlementDeductions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_SettlementDeductions_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TRN_SettlementDeductions_DeductionType] CHECK ([DeductionType] IN ('Shortage','Fine','Other')),
        CONSTRAINT [CK_TRN_SettlementDeductions_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_TRN_SettlementDeductions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_SettlementDeductions_TRN_Settlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [trn].[TRN_Settlements] ([Id]),
        CONSTRAINT [FK_TRN_SettlementDeductions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_SettlementDeductions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_SettlementDeductions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_SettlementDeductions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_Trips] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_Trips_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [ReceiptNo] nvarchar(30) NOT NULL,
        [ReceiptNoKey] nvarchar(30) NOT NULL,
        [TripDate] date NOT NULL,
        [LoadingDate] date NOT NULL,
        [UnloadingDate] date NULL,
        [DriverId] bigint NOT NULL,
        [CustomerId] bigint NOT NULL,
        [LoadingSiteId] bigint NOT NULL,
        [UnloadingSiteId] bigint NOT NULL,
        [MaterialId] bigint NOT NULL,
        [PartnerId] bigint NULL,
        [TruckPlateAtSave] varchar(20) NOT NULL,
        [FleetClass] varchar(10) NOT NULL,
        [LoadedWeight] decimal(18,3) NOT NULL,
        [UnloadedWeight] decimal(18,3) NULL,
        [BillingQty] decimal(18,3) NOT NULL,
        [WageQty] decimal(18,3) NOT NULL,
        [CustomerPrice] decimal(18,4) NOT NULL,
        [TariffPrice] decimal(18,4) NULL,
        [DriverRate] decimal(18,4) NOT NULL,
        [RateSource] varchar(10) NOT NULL,
        [RateMonth] char(7) NOT NULL,
        [PartnerRate] decimal(18,4) NULL,
        [Revenue] decimal(18,3) NOT NULL,
        [DriverWage] decimal(18,3) NOT NULL,
        [PartnerCost] decimal(18,3) NOT NULL CONSTRAINT [DF_TRN_Trips_PartnerCost] DEFAULT (0),
        [ShortageWeight] decimal(18,3) NULL,
        [ShortagePct] decimal(9,4) NULL,
        [IsShortageFlagged] bit NOT NULL CONSTRAINT [DF_TRN_Trips_IsShortageFlagged] DEFAULT (0),
        [IsTariffBreach] bit NOT NULL CONSTRAINT [DF_TRN_Trips_IsTariffBreach] DEFAULT (0),
        [IsManualRate] bit NOT NULL CONSTRAINT [DF_TRN_Trips_IsManualRate] DEFAULT (0),
        [NeedsReview] bit NOT NULL CONSTRAINT [DF_TRN_Trips_NeedsReview] DEFAULT (0),
        [ReviewNote] nvarchar(500) NULL,
        [Status] varchar(10) NOT NULL CONSTRAINT [DF_TRN_Trips_Status] DEFAULT ('Active'),
        [InvoiceId] bigint NULL,
        [SettlementId] bigint NULL,
        [JournalEntryId] bigint NULL,
        [VoidReason] nvarchar(500) NULL,
        [VoidedBy] bigint NULL,
        [VoidedAt] datetime2 NULL,
        [ImportBatchId] bigint NULL,
        [Notes] nvarchar(1000) NULL,
        [CustomValues] nvarchar(max) NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_Trips_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_Trips_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_Trips] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_Trips_CustomerPrice] CHECK ([CustomerPrice] >= 0),
        CONSTRAINT [CK_TRN_Trips_CustomValues] CHECK (ISJSON([CustomValues]) = 1),
        CONSTRAINT [CK_TRN_Trips_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_Trips_DriverRate] CHECK ([DriverRate] >= 0),
        CONSTRAINT [CK_TRN_Trips_FleetClass] CHECK ([FleetClass] IN ('Own','External')),
        CONSTRAINT [CK_TRN_Trips_LoadedWeight] CHECK ([LoadedWeight] > 0),
        CONSTRAINT [CK_TRN_Trips_RateSource] CHECK ([RateSource] IN ('Monthly','Manual')),
        CONSTRAINT [CK_TRN_Trips_Status] CHECK ([Status] IN ('Active','Void')),
        CONSTRAINT [CK_TRN_Trips_UnloadedWeight] CHECK ([UnloadedWeight] >= 0),
        CONSTRAINT [CK_TRN_Trips_UnloadingDate] CHECK ([UnloadingDate] >= [LoadingDate]),
        CONSTRAINT [CK_TRN_Trips_VoidReason_VoidedBy_VoidedAt] CHECK ([Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)),
        CONSTRAINT [FK_TRN_Trips_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_Trips_ImportBatches_ImportBatchId] FOREIGN KEY ([ImportBatchId]) REFERENCES [cfg].[ImportBatches] ([Id]),
        CONSTRAINT [FK_TRN_Trips_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [trn].[TRN_Customers] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [trn].[TRN_Drivers] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_LoadingSites_LoadingSiteId] FOREIGN KEY ([LoadingSiteId]) REFERENCES [trn].[TRN_LoadingSites] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [trn].[TRN_Materials] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Partners_PartnerId] FOREIGN KEY ([PartnerId]) REFERENCES [trn].[TRN_Partners] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_Settlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [trn].[TRN_Settlements] ([Id]),
        CONSTRAINT [FK_TRN_Trips_TRN_UnloadingSites_UnloadingSiteId] FOREIGN KEY ([UnloadingSiteId]) REFERENCES [trn].[TRN_UnloadingSites] ([Id]),
        CONSTRAINT [FK_TRN_Trips_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_Trips_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Trips_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Trips_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_Trips_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_InvoiceLines] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_InvoiceLines_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [TripId] bigint NULL,
        [Description] nvarchar(300) NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [UnitPrice] decimal(18,4) NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_TRN_InvoiceLines_SortOrder] DEFAULT (0),
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_InvoiceLines_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_InvoiceLines_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_InvoiceLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_InvoiceLines_Amount] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_TRN_InvoiceLines_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_InvoiceLines_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_TRN_InvoiceLines_UnitPrice] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [FK_TRN_InvoiceLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_TRN_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [trn].[TRN_Trips] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_InvoiceLines_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE TABLE [trn].[TRN_CreditNoteLines] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_CreditNoteLines_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [CreditNoteId] bigint NOT NULL,
        [InvoiceLineId] bigint NOT NULL,
        [TripId] bigint NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_CreditNoteLines_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_CreditNoteLines_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_CreditNoteLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_CreditNoteLines_Amount] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_TRN_CreditNoteLines_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_TRN_CreditNoteLines_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_TRN_CreditNoteLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_TRN_CreditNotes_CreditNoteId] FOREIGN KEY ([CreditNoteId]) REFERENCES [trn].[TRN_CreditNotes] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_TRN_InvoiceLines_InvoiceLineId] FOREIGN KEY ([InvoiceLineId]) REFERENCES [trn].[TRN_InvoiceLines] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_TRN_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [trn].[TRN_Trips] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_CreditNoteLines_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'CreatedBy', N'DeleteReason', N'DeletedAt', N'DeletedBy', N'DescriptionAr', N'DescriptionEn', N'GroupName', N'IsDeleted', N'PermissionKey', N'PublicId', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[core].[Permissions]'))
        SET IDENTITY_INSERT [core].[Permissions] ON;
    EXEC(N'INSERT INTO [core].[Permissions] ([Id], [CreatedAt], [CreatedBy], [DeleteReason], [DeletedAt], [DeletedBy], [DescriptionAr], [DescriptionEn], [GroupName], [IsDeleted], [PermissionKey], [PublicId], [UpdatedAt], [UpdatedBy])
    VALUES (CAST(1 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''عرض الرحلات وسجلّ تغييراتها'', N''View trips and trip history'', ''Trips'', CAST(0 AS bit), ''trn.trips.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a01'', NULL, NULL),
    (CAST(2 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إنشاء الرحلات واستيرادها وتعديلها'', N''Create, import and edit trips'', ''Trips'', CAST(0 AS bit), ''trn.trips.edit'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a02'', NULL, NULL),
    (CAST(3 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إلغاء الرحلات مع ذكر السبب'', N''Void trips with a reason'', ''Trips'', CAST(0 AS bit), ''trn.trips.void'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a03'', NULL, NULL),
    (CAST(4 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''عرض الفواتير والمسودات والإشعارات الدائنة'', N''View invoices, drafts and credit notes'', ''Invoicing'', CAST(0 AS bit), ''trn.inv.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a04'', NULL, NULL),
    (CAST(5 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إنشاء الفواتير (على الرحلات واليدوية)، واعتماد المسودات، وإصدار الإشعارات الدائنة، وتصميم نماذج الفواتير، وإرسال الفواتير الإلكترونية'', N''Create invoices (trip-based and manual), approve drafts, issue credit notes, design invoice layouts, submit e-invoices'', ''Invoicing'', CAST(0 AS bit), ''trn.inv.issue'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a05'', NULL, NULL),
    (CAST(6 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إلغاء الفواتير والإشعارات الدائنة'', N''Void invoices and credit notes'', ''Invoicing'', CAST(0 AS bit), ''trn.inv.void'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a06'', NULL, NULL),
    (CAST(7 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''تسجيل سندات القبض من العملاء وإلغاؤها'', N''Record and void customer receipts'', ''Receipts'', CAST(0 AS bit), ''trn.rcpt.manage'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a07'', NULL, NULL),
    (CAST(8 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''عرض حسابات السائقين والشركاء وكشوفها'', N''View driver and partner accounts and statements'', ''Drivers & partners'', CAST(0 AS bit), ''trn.drv.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a08'', NULL, NULL),
    (CAST(9 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''تسجيل سلف السائقين وتعديلها وإلغاؤها'', N''Record, edit and void driver advances'', ''Drivers & partners'', CAST(0 AS bit), ''trn.drv.advance'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a09'', NULL, NULL),
    (CAST(10 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''تسوية حسابات السائقين، وإلغاء التسويات، وتسجيل دفعات الشركاء'', N''Settle drivers, void settlements, record partner payments'', ''Drivers & partners'', CAST(0 AS bit), ''trn.drv.settle'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a10'', NULL, NULL),
    (CAST(11 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''عرض القيود اليومية المولّدة تلقائياً'', N''View automatically generated journal entries'', ''Journal'', CAST(0 AS bit), ''acc.je.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a11'', NULL, NULL),
    (CAST(12 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''ترحيل القيود إلى الأستاذ العام، وإعادة محاولة الترحيلات المعلّقة، وتصدير دفعات القيود'', N''Post entries to the ledger, retry pending postings, export journal batches'', ''Journal'', CAST(0 AS bit), ''acc.je.export'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a12'', NULL, NULL),
    (CAST(13 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''عرض الأستاذ العام وميزان المراجعة والقوائم المالية وكشوف الحسابات'', N''View the general ledger, trial balance, financial statements and account statements'', ''Accounting'', CAST(0 AS bit), ''acc.acct.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a13'', NULL, NULL),
    (CAST(14 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إنشاء القيود اليدوية وتعديلها وإلغاؤها، واستيراد القيود'', N''Create, edit and void manual journal entries; import entries'', ''Accounting'', CAST(0 AS bit), ''acc.acct.post'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a14'', NULL, NULL),
    (CAST(15 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إدارة مستندات الذمم الدائنة والمدينة ودفعاتها ومرتجعاتها'', N''Manage payable and receivable documents, payments and returns'', ''Accounting'', CAST(0 AS bit), ''acc.acct.apar'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a15'', NULL, NULL),
    (CAST(16 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إدارة دليل الحسابات ومراكز التكلفة والبنوك'', N''Manage the chart of accounts, cost centres and banks'', ''Accounting'', CAST(0 AS bit), ''acc.acct.setup'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a16'', NULL, NULL),
    (CAST(17 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''تنفيذ عمليات صيانة الحسابات: الحذف الجماعي للقيود غير المرتبطة، ومسح بيانات الشركة، واستبدال دليل الحسابات'', N''Run accounting maintenance: bulk deletion of unlinked entries, clearing a company''''s data, or replacing the chart of accounts'', ''Accounting'', CAST(0 AS bit), ''acc.acct.maint'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a17'', NULL, NULL),
    (CAST(18 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''الوصول إلى جميع التقارير ومنشئ التقارير والتقارير المحفوظة'', N''Access all reports, the report builder and saved reports'', ''Reports & master data'', CAST(0 AS bit), ''trn.reports.view'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a18'', NULL, NULL),
    (CAST(19 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إدارة البيانات الأساسية والتعرفات وأسعار المواقع الشهرية والشركاء، وإدخال أسعار السائقين يدوياً'', N''Manage master data, tariffs, monthly site prices and partners; enter manual driver-rate overrides'', ''Reports & master data'', CAST(0 AS bit), ''trn.masters.manage'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a19'', NULL, NULL),
    (CAST(20 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''إعدادات الشركة وربط الحسابات والضريبة والفترات والترقيم وتهيئة الحقول والنماذج'', N''Company settings, account mapping, tax, periods, numbering, configuration of fields and layouts'', ''Administration'', CAST(0 AS bit), ''core.settings.manage'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a20'', NULL, NULL),
    (CAST(21 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''دعوة المستخدمين وتعديلهم وتعطيلهم وفكّ قفلهم وأرشفتهم وتعيين الأدوار'', N''Invite, edit, disable, unlock and archive users; assign roles'', ''Administration'', CAST(0 AS bit), ''core.users.manage'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a21'', NULL, NULL),
    (CAST(22 AS bigint), ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, N''الاشتراك والخطة والفوترة، وإنشاء الشركات وأرشفتها، وإعدادات المشترك العامة'', N''Subscription, plan and billing; create and archive companies; tenant-wide settings'', ''Tenant'', CAST(0 AS bit), ''core.tenant.manage'', ''7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a22'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'CreatedBy', N'DeleteReason', N'DeletedAt', N'DeletedBy', N'DescriptionAr', N'DescriptionEn', N'GroupName', N'IsDeleted', N'PermissionKey', N'PublicId', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[core].[Permissions]'))
        SET IDENTITY_INSERT [core].[Permissions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BillingCycle', N'Code', N'CreatedAt', N'CreatedBy', N'DeleteReason', N'DeletedAt', N'DeletedBy', N'IsActive', N'IsDeleted', N'MaxActiveUsers', N'MaxCompanies', N'MaxStorageMb', N'MaxTripsPerMonth', N'NameAr', N'NameEn', N'Price', N'PublicId', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[core].[Plans]'))
        SET IDENTITY_INSERT [core].[Plans] ON;
    EXEC(N'INSERT INTO [core].[Plans] ([Id], [BillingCycle], [Code], [CreatedAt], [CreatedBy], [DeleteReason], [DeletedAt], [DeletedBy], [IsActive], [IsDeleted], [MaxActiveUsers], [MaxCompanies], [MaxStorageMb], [MaxTripsPerMonth], [NameAr], [NameEn], [Price], [PublicId], [UpdatedAt], [UpdatedBy])
    VALUES (CAST(1 AS bigint), ''Monthly'', ''TRIAL'', ''2026-10-08T00:00:00.000Z'', NULL, NULL, NULL, NULL, CAST(0 AS bit), CAST(0 AS bit), 5, 2, 1024, 2000, N''تجريبية'', N''Trial'', 0.0, ''3f9a1c7e-2d44-4b8e-8f1a-6c0b9e2d7a01'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BillingCycle', N'Code', N'CreatedAt', N'CreatedBy', N'DeleteReason', N'DeletedAt', N'DeletedBy', N'IsActive', N'IsDeleted', N'MaxActiveUsers', N'MaxCompanies', N'MaxStorageMb', N'MaxTripsPerMonth', N'NameAr', N'NameEn', N'Price', N'PublicId', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[core].[Plans]'))
        SET IDENTITY_INSERT [core].[Plans] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Accounts_ParentId] ON [acc].[Accounts] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Accounts_CompanyId_Code] ON [acc].[Accounts] ([CompanyId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Accounts_PublicId] ON [acc].[Accounts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Announcements_PublicId] ON [core].[Announcements] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocumentLines_AccountId] ON [acc].[ApArDocumentLines] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ApArDocumentLines_CompanyId] ON [acc].[ApArDocumentLines] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocumentLines_CostCentreId] ON [acc].[ApArDocumentLines] ([CostCentreId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocumentLines_DocumentId] ON [acc].[ApArDocumentLines] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArDocumentLines_PublicId] ON [acc].[ApArDocumentLines] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocuments_JournalEntryId] ON [acc].[ApArDocuments] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocuments_PartyId] ON [acc].[ApArDocuments] ([PartyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArDocuments_VoidedBy] ON [acc].[ApArDocuments] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ApArDocuments_CompanyId_DocumentKind_Number] ON [acc].[ApArDocuments] ([CompanyId], [DocumentKind], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArDocuments_PublicId] ON [acc].[ApArDocuments] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArEInvoiceSubmissions_ApArDocumentId] ON [acc].[ApArEInvoiceSubmissions] ([ApArDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArEInvoiceSubmissions_ApArReturnId] ON [acc].[ApArEInvoiceSubmissions] ([ApArReturnId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ApArEInvoiceSubmissions_Queue] ON [acc].[ApArEInvoiceSubmissions] ([CompanyId], [Status]) WHERE [Status] IN (''Queued'',''Rejected'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ApArEInvoiceSubmissions_CompanyId_Icv] ON [acc].[ApArEInvoiceSubmissions] ([CompanyId], [Icv]) WHERE [IsDeleted] = 0 AND [Icv] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArEInvoiceSubmissions_PublicId] ON [acc].[ApArEInvoiceSubmissions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ApArParties_CompanyId] ON [acc].[ApArParties] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArParties_ControlAccountId] ON [acc].[ApArParties] ([ControlAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArParties_PublicId] ON [acc].[ApArParties] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArPayments_BankAccountId] ON [acc].[ApArPayments] ([BankAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArPayments_CashBoxId] ON [acc].[ApArPayments] ([CashBoxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArPayments_DocumentId] ON [acc].[ApArPayments] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArPayments_JournalEntryId] ON [acc].[ApArPayments] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArPayments_VoidedBy] ON [acc].[ApArPayments] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ApArPayments_CompanyId_Number] ON [acc].[ApArPayments] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArPayments_PublicId] ON [acc].[ApArPayments] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArReturns_DocumentId] ON [acc].[ApArReturns] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArReturns_JournalEntryId] ON [acc].[ApArReturns] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ApArReturns_VoidedBy] ON [acc].[ApArReturns] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ApArReturns_CompanyId_Number] ON [acc].[ApArReturns] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ApArReturns_PublicId] ON [acc].[ApArReturns] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Entity] ON [core].[AuditLogs] ([CompanyId], [EntityType], [EntityId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId] ON [core].[AuditLogs] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_User] ON [core].[AuditLogs] ([UserId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_BankAccounts_AccountId] ON [acc].[BankAccounts] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_BankAccounts_CompanyId] ON [acc].[BankAccounts] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_BankAccounts_PublicId] ON [acc].[BankAccounts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_CashBoxes_AccountId] ON [acc].[CashBoxes] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_CashBoxes_CompanyId] ON [acc].[CashBoxes] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CashBoxes_PublicId] ON [acc].[CashBoxes] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Companies_PublicId] ON [core].[Companies] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Companies_TenantId_Name] ON [core].[Companies] ([TenantId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CompanySettings_CompanyId_SettingKey] ON [cfg].[CompanySettings] ([CompanyId], [SettingKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CompanySettings_PublicId] ON [cfg].[CompanySettings] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CostCentres_CompanyId_Code] ON [acc].[CostCentres] ([CompanyId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CostCentres_PublicId] ON [acc].[CostCentres] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CustomFieldDefinitions_CompanyId_EntityType_FieldKey] ON [cfg].[CustomFieldDefinitions] ([CompanyId], [EntityType], [FieldKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_CustomFieldDefinitions_PublicId] ON [cfg].[CustomFieldDefinitions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_DashboardPins_CompanyId] ON [cfg].[DashboardPins] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_DashboardPins_SavedReportId] ON [cfg].[DashboardPins] ([SavedReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_DashboardPins_UserId] ON [cfg].[DashboardPins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_DashboardPins_PublicId] ON [cfg].[DashboardPins] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_EInvoiceSettings_CompanyId] ON [cfg].[EInvoiceSettings] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_EInvoiceSettings_PublicId] ON [cfg].[EInvoiceSettings] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_EntryLines_Account] ON [acc].[EntryLines] ([AccountId]) INCLUDE ([Debit], [Credit]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_EntryLines_CostCentreId] ON [acc].[EntryLines] ([CostCentreId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_EntryLines_JournalEntryId] ON [acc].[EntryLines] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_EntryLines_Party] ON [acc].[EntryLines] ([CompanyId], [PartyModule], [PartyType], [PartyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_EntryLines_PublicId] ON [acc].[EntryLines] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_ImportBatches_CompanyId] ON [cfg].[ImportBatches] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_ImportBatches_ImportedBy] ON [cfg].[ImportBatches] ([ImportedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ImportBatches_PublicId] ON [cfg].[ImportBatches] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Invitations_CompanyId] ON [core].[Invitations] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Invitations_InvitedBy] ON [core].[Invitations] ([InvitedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Invitations_RoleId] ON [core].[Invitations] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Invitations_PublicId] ON [core].[Invitations] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Invitations_TokenHash] ON [core].[Invitations] ([TokenHash]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_ExportBatchId] ON [acc].[JournalEntries] ([ExportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_Period] ON [acc].[JournalEntries] ([CompanyId], [PeriodMonth]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_ReversedByEntryId] ON [acc].[JournalEntries] ([ReversedByEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_ReversesEntryId] ON [acc].[JournalEntries] ([ReversesEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_Source] ON [acc].[JournalEntries] ([CompanyId], [SourceModule], [SourceType], [SourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_VoidedBy] ON [acc].[JournalEntries] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_JournalEntries_CompanyId_Number] ON [acc].[JournalEntries] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_JournalEntries_PublicId] ON [acc].[JournalEntries] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_JournalExportBatches_CompanyId] ON [acc].[JournalExportBatches] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_JournalExportBatches_ExportedBy] ON [acc].[JournalExportBatches] ([ExportedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_JournalExportBatches_PublicId] ON [acc].[JournalExportBatches] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_MaintenanceLogs_CompanyId] ON [core].[MaintenanceLogs] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_MaintenanceLogs_TenantId] ON [core].[MaintenanceLogs] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_MaintenanceLogs_UserId] ON [core].[MaintenanceLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Notifications_CompanyId] ON [core].[Notifications] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Notifications_Unread] ON [core].[Notifications] ([UserId], [ReadAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Notifications_PublicId] ON [core].[Notifications] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_NumberSequences_CompanyId_DocumentType] ON [cfg].[NumberSequences] ([CompanyId], [DocumentType]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_NumberSequences_PublicId] ON [cfg].[NumberSequences] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_PendingPostings_Open] ON [acc].[PendingPostings] ([CompanyId], [ResolvedAt]) WHERE [ResolvedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PendingPostings_PublicId] ON [acc].[PendingPostings] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_PendingPostings_Source] ON [acc].[PendingPostings] ([CompanyId], [SourceModule], [SourceType], [SourceId]) WHERE [ResolvedAt] IS NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Periods_ClosedBy] ON [acc].[Periods] ([ClosedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Periods_CompanyId_PeriodMonth] ON [acc].[Periods] ([CompanyId], [PeriodMonth]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Periods_PublicId] ON [acc].[Periods] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Permissions_PermissionKey] ON [core].[Permissions] ([PermissionKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Permissions_PublicId] ON [core].[Permissions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Plans_Code] ON [core].[Plans] ([Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Plans_PublicId] ON [core].[Plans] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_PostingKeyMappings_AccountId] ON [acc].[PostingKeyMappings] ([AccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_PostingKeyMappings_CompanyId_PostingKey] ON [acc].[PostingKeyMappings] ([CompanyId], [PostingKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_PostingKeyMappings_PublicId] ON [acc].[PostingKeyMappings] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_RecordLocks_UserId] ON [cfg].[RecordLocks] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RecordLocks_CompanyId_EntityType_EntityId] ON [cfg].[RecordLocks] ([CompanyId], [EntityType], [EntityId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RecordLocks_PublicId] ON [cfg].[RecordLocks] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [core].[RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RolePermissions_PublicId] ON [core].[RolePermissions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RolePermissions_RoleId_PermissionId] ON [core].[RolePermissions] ([RoleId], [PermissionId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Roles_PublicId] ON [core].[Roles] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Roles_TenantId_Name] ON [core].[Roles] ([TenantId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_SavedReports_CompanyId] ON [cfg].[SavedReports] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_SavedReports_OwnerUserId] ON [cfg].[SavedReports] ([OwnerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SavedReports_PublicId] ON [cfg].[SavedReports] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StandardFieldConfigs_CompanyId_EntityType_FieldKey] ON [cfg].[StandardFieldConfigs] ([CompanyId], [EntityType], [FieldKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_StandardFieldConfigs_PublicId] ON [cfg].[StandardFieldConfigs] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_SubscriptionInvoices_Number] ON [core].[SubscriptionInvoices] ([Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SubscriptionInvoices_PublicId] ON [core].[SubscriptionInvoices] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_SupportAccessGrants_ApprovedBy] ON [core].[SupportAccessGrants] ([ApprovedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_SupportAccessGrants_RequestedBy] ON [core].[SupportAccessGrants] ([RequestedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SupportAccessGrants_PublicId] ON [core].[SupportAccessGrants] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TableColumnViews_UserId] ON [cfg].[TableColumnViews] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TableColumnViews_Key] ON [cfg].[TableColumnViews] ([CompanyId], [ViewKey], [UserId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TableColumnViews_PublicId] ON [cfg].[TableColumnViews] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Tenants_PlanId] ON [core].[Tenants] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Tenants_PublicId] ON [core].[Tenants] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TermsAcceptances_UserId] ON [core].[TermsAcceptances] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TermsAcceptances_PublicId] ON [core].[TermsAcceptances] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_BankAccountId] ON [trn].[TRN_Advances] ([BankAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_CashBoxId] ON [trn].[TRN_Advances] ([CashBoxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_DriverId] ON [trn].[TRN_Advances] ([DriverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_JournalEntryId] ON [trn].[TRN_Advances] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_SettlementId] ON [trn].[TRN_Advances] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Advances_VoidedBy] ON [trn].[TRN_Advances] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Advances_CompanyId_Number] ON [trn].[TRN_Advances] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Advances_PublicId] ON [trn].[TRN_Advances] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_CreditNoteLines_CompanyId] ON [trn].[TRN_CreditNoteLines] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNoteLines_CreditNoteId] ON [trn].[TRN_CreditNoteLines] ([CreditNoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNoteLines_InvoiceLineId] ON [trn].[TRN_CreditNoteLines] ([InvoiceLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNoteLines_TripId] ON [trn].[TRN_CreditNoteLines] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_CreditNoteLines_PublicId] ON [trn].[TRN_CreditNoteLines] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNotes_CustomerId] ON [trn].[TRN_CreditNotes] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNotes_InvoiceId] ON [trn].[TRN_CreditNotes] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNotes_JournalEntryId] ON [trn].[TRN_CreditNotes] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_CreditNotes_VoidedBy] ON [trn].[TRN_CreditNotes] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_CreditNotes_CompanyId_Number] ON [trn].[TRN_CreditNotes] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_CreditNotes_PublicId] ON [trn].[TRN_CreditNotes] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Customers_ApArPartyId] ON [trn].[TRN_Customers] ([ApArPartyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Customers_CompanyId_Name] ON [trn].[TRN_Customers] ([CompanyId], [Name]) WHERE [IsDeleted] = 0 AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Customers_PublicId] ON [trn].[TRN_Customers] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_Drivers_Expiry] ON [trn].[TRN_Drivers] ([CompanyId], [LicenceExpiry], [RegistrationExpiry], [InsuranceExpiry], [InspectionExpiry]) WHERE [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Drivers_OwnershipTypeId] ON [trn].[TRN_Drivers] ([OwnershipTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Drivers_PartnerId] ON [trn].[TRN_Drivers] ([PartnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Drivers_CompanyId_NationalId] ON [trn].[TRN_Drivers] ([CompanyId], [NationalId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Drivers_CompanyId_PlateNumber] ON [trn].[TRN_Drivers] ([CompanyId], [PlateNumber]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Drivers_PublicId] ON [trn].[TRN_Drivers] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_EInvoiceSubmissions_CreditNoteId] ON [trn].[TRN_EInvoiceSubmissions] ([CreditNoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_EInvoiceSubmissions_InvoiceId] ON [trn].[TRN_EInvoiceSubmissions] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_EInvoiceSubmissions_Queue] ON [trn].[TRN_EInvoiceSubmissions] ([CompanyId], [Status]) WHERE [Status] IN (''Queued'',''Rejected'')');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_EInvoiceSubmissions_CompanyId_Icv] ON [trn].[TRN_EInvoiceSubmissions] ([CompanyId], [Icv]) WHERE [IsDeleted] = 0 AND [Icv] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_EInvoiceSubmissions_PublicId] ON [trn].[TRN_EInvoiceSubmissions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_InvoiceLayouts_CompanyId_LayoutType] ON [trn].[TRN_InvoiceLayouts] ([CompanyId], [LayoutType]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_InvoiceLayouts_PublicId] ON [trn].[TRN_InvoiceLayouts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_InvoiceLines_CompanyId] ON [trn].[TRN_InvoiceLines] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_InvoiceLines_InvoiceId] ON [trn].[TRN_InvoiceLines] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_InvoiceLines_TripId] ON [trn].[TRN_InvoiceLines] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_InvoiceLines_PublicId] ON [trn].[TRN_InvoiceLines] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_ApprovedBy] ON [trn].[TRN_Invoices] ([ApprovedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_Customer] ON [trn].[TRN_Invoices] ([CompanyId], [CustomerId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_CustomerId] ON [trn].[TRN_Invoices] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_Date] ON [trn].[TRN_Invoices] ([CompanyId], [InvoiceDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_JournalEntryId] ON [trn].[TRN_Invoices] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Invoices_VoidedBy] ON [trn].[TRN_Invoices] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Invoices_CompanyId_Number] ON [trn].[TRN_Invoices] ([CompanyId], [Number]) WHERE [IsDeleted] = 0 AND [Number] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Invoices_PublicId] ON [trn].[TRN_Invoices] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_LoadingSites_CompanyId_Name] ON [trn].[TRN_LoadingSites] ([CompanyId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_LoadingSites_PublicId] ON [trn].[TRN_LoadingSites] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Materials_CompanyId_Name] ON [trn].[TRN_Materials] ([CompanyId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Materials_PublicId] ON [trn].[TRN_Materials] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_MonthlySiteRates_LoadingSiteId] ON [trn].[TRN_MonthlySiteRates] ([LoadingSiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_MonthlySiteRates_CompanyId_RateMonth_LoadingSiteId] ON [trn].[TRN_MonthlySiteRates] ([CompanyId], [RateMonth], [LoadingSiteId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_MonthlySiteRates_PublicId] ON [trn].[TRN_MonthlySiteRates] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_OwnershipTypes_CompanyId_Name] ON [trn].[TRN_OwnershipTypes] ([CompanyId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_OwnershipTypes_PublicId] ON [trn].[TRN_OwnershipTypes] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_PartnerPayments_BankAccountId] ON [trn].[TRN_PartnerPayments] ([BankAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_PartnerPayments_CashBoxId] ON [trn].[TRN_PartnerPayments] ([CashBoxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_PartnerPayments_JournalEntryId] ON [trn].[TRN_PartnerPayments] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_PartnerPayments_PartnerId] ON [trn].[TRN_PartnerPayments] ([PartnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_PartnerPayments_VoidedBy] ON [trn].[TRN_PartnerPayments] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_PartnerPayments_CompanyId_Number] ON [trn].[TRN_PartnerPayments] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_PartnerPayments_PublicId] ON [trn].[TRN_PartnerPayments] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Partners_CompanyId_Name] ON [trn].[TRN_Partners] ([CompanyId], [Name]) WHERE [IsDeleted] = 0 AND [Status] = ''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Partners_PublicId] ON [trn].[TRN_Partners] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_RateHistory_ChangedBy] ON [trn].[TRN_RateHistory] ([ChangedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_RateHistory_Source] ON [trn].[TRN_RateHistory] ([CompanyId], [SourceType], [SourceId], [ChangedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_RateHistory_PublicId] ON [trn].[TRN_RateHistory] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_BankAccountId] ON [trn].[TRN_Receipts] ([BankAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_CashBoxId] ON [trn].[TRN_Receipts] ([CashBoxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_CustomerId] ON [trn].[TRN_Receipts] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_InvoiceId] ON [trn].[TRN_Receipts] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_JournalEntryId] ON [trn].[TRN_Receipts] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Receipts_VoidedBy] ON [trn].[TRN_Receipts] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Receipts_CompanyId_Number] ON [trn].[TRN_Receipts] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Receipts_PublicId] ON [trn].[TRN_Receipts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_SettlementDeductions_CompanyId] ON [trn].[TRN_SettlementDeductions] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_SettlementDeductions_SettlementId] ON [trn].[TRN_SettlementDeductions] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_SettlementDeductions_PublicId] ON [trn].[TRN_SettlementDeductions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Settlements_BankAccountId] ON [trn].[TRN_Settlements] ([BankAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Settlements_CashBoxId] ON [trn].[TRN_Settlements] ([CashBoxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Settlements_DriverId] ON [trn].[TRN_Settlements] ([DriverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Settlements_JournalEntryId] ON [trn].[TRN_Settlements] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Settlements_VoidedBy] ON [trn].[TRN_Settlements] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Settlements_CompanyId_Number] ON [trn].[TRN_Settlements] ([CompanyId], [Number]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Settlements_PublicId] ON [trn].[TRN_Settlements] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_CustomerId] ON [trn].[TRN_Tariffs] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_LoadingSiteId] ON [trn].[TRN_Tariffs] ([LoadingSiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_Lookup] ON [trn].[TRN_Tariffs] ([CompanyId], [PartyType], [Status], [Specificity] DESC, [ValidFrom] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_MaterialId] ON [trn].[TRN_Tariffs] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_PartnerId] ON [trn].[TRN_Tariffs] ([PartnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Tariffs_UnloadingSiteId] ON [trn].[TRN_Tariffs] ([UnloadingSiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Tariffs_CompanyId_PartyType_CustomerId_PartnerId_MaterialId_LoadingSiteId_UnloadingSiteId_ValidFrom] ON [trn].[TRN_Tariffs] ([CompanyId], [PartyType], [CustomerId], [PartnerId], [MaterialId], [LoadingSiteId], [UnloadingSiteId], [ValidFrom]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Tariffs_PublicId] ON [trn].[TRN_Tariffs] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_CustomerId] ON [trn].[TRN_Trips] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_Date] ON [trn].[TRN_Trips] ([CompanyId], [TripDate]) INCLUDE ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_DriverId] ON [trn].[TRN_Trips] ([DriverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_ImportBatchId] ON [trn].[TRN_Trips] ([ImportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_InvoiceId] ON [trn].[TRN_Trips] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_JournalEntryId] ON [trn].[TRN_Trips] ([JournalEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_LoadingSiteId] ON [trn].[TRN_Trips] ([LoadingSiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_MaterialId] ON [trn].[TRN_Trips] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_PartnerId] ON [trn].[TRN_Trips] ([PartnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_SettlementId] ON [trn].[TRN_Trips] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_Trips_Unbilled] ON [trn].[TRN_Trips] ([CompanyId], [CustomerId], [Status]) WHERE [InvoiceId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_UnloadingSiteId] ON [trn].[TRN_Trips] ([UnloadingSiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_TRN_Trips_Unsettled] ON [trn].[TRN_Trips] ([CompanyId], [DriverId], [Status]) WHERE [SettlementId] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_TRN_Trips_VoidedBy] ON [trn].[TRN_Trips] ([VoidedBy]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_Trips_CompanyId_ReceiptNoKey] ON [trn].[TRN_Trips] ([CompanyId], [ReceiptNoKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_Trips_PublicId] ON [trn].[TRN_Trips] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_UnloadingSites_CompanyId_Name] ON [trn].[TRN_UnloadingSites] ([CompanyId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_UnloadingSites_PublicId] ON [trn].[TRN_UnloadingSites] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_UserCompanyRoles_CompanyId] ON [core].[UserCompanyRoles] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_UserCompanyRoles_RoleId] ON [core].[UserCompanyRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserCompanyRoles_PublicId] ON [core].[UserCompanyRoles] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_UserCompanyRoles_UserId_CompanyId] ON [core].[UserCompanyRoles] ([UserId], [CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_UserRecoveryCodes_UserId] ON [core].[UserRecoveryCodes] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserRecoveryCodes_PublicId] ON [core].[UserRecoveryCodes] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE INDEX [IX_Users_DefaultCompanyId] ON [core].[Users] ([DefaultCompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_Email] ON [core].[Users] ([Email]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_NormalizedEmail] ON [core].[Users] ([NormalizedEmail]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Users_PublicId] ON [core].[Users] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_UserSessions_UserId_ExpiresAt] ON [core].[UserSessions] ([UserId], [ExpiresAt]) WHERE [RevokedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UserSessions_PublicId] ON [core].[UserSessions] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_UserSessions_TokenHash] ON [core].[UserSessions] ([TokenHash]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Accounts] ADD CONSTRAINT [FK_Accounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Accounts] ADD CONSTRAINT [FK_Accounts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Accounts] ADD CONSTRAINT [FK_Accounts_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Accounts] ADD CONSTRAINT [FK_Accounts_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Accounts] ADD CONSTRAINT [FK_Accounts_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Announcements] ADD CONSTRAINT [FK_Announcements_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Announcements] ADD CONSTRAINT [FK_Announcements_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Announcements] ADD CONSTRAINT [FK_Announcements_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_ApArDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [acc].[ApArDocuments] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_CostCentres_CostCentreId] FOREIGN KEY ([CostCentreId]) REFERENCES [acc].[CostCentres] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [FK_ApArDocumentLines_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_ApArParties_PartyId] FOREIGN KEY ([PartyId]) REFERENCES [acc].[ApArParties] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [FK_ApArDocuments_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_ApArReturns_ApArReturnId] FOREIGN KEY ([ApArReturnId]) REFERENCES [acc].[ApArReturns] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArEInvoiceSubmissions] ADD CONSTRAINT [FK_ApArEInvoiceSubmissions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArParties] ADD CONSTRAINT [FK_ApArParties_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArParties] ADD CONSTRAINT [FK_ApArParties_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArParties] ADD CONSTRAINT [FK_ApArParties_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArParties] ADD CONSTRAINT [FK_ApArParties_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArParties] ADD CONSTRAINT [FK_ApArParties_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [acc].[BankAccounts] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_CashBoxes_CashBoxId] FOREIGN KEY ([CashBoxId]) REFERENCES [acc].[CashBoxes] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArPayments] ADD CONSTRAINT [FK_ApArPayments_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [FK_ApArReturns_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[BankAccounts] ADD CONSTRAINT [FK_BankAccounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[BankAccounts] ADD CONSTRAINT [FK_BankAccounts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[BankAccounts] ADD CONSTRAINT [FK_BankAccounts_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[BankAccounts] ADD CONSTRAINT [FK_BankAccounts_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[BankAccounts] ADD CONSTRAINT [FK_BankAccounts_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CashBoxes] ADD CONSTRAINT [FK_CashBoxes_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CashBoxes] ADD CONSTRAINT [FK_CashBoxes_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CashBoxes] ADD CONSTRAINT [FK_CashBoxes_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CashBoxes] ADD CONSTRAINT [FK_CashBoxes_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CashBoxes] ADD CONSTRAINT [FK_CashBoxes_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Companies] ADD CONSTRAINT [FK_Companies_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Companies] ADD CONSTRAINT [FK_Companies_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Companies] ADD CONSTRAINT [FK_Companies_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Companies] ADD CONSTRAINT [FK_Companies_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CompanySettings] ADD CONSTRAINT [FK_CompanySettings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CompanySettings] ADD CONSTRAINT [FK_CompanySettings_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CompanySettings] ADD CONSTRAINT [FK_CompanySettings_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CompanySettings] ADD CONSTRAINT [FK_CompanySettings_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CostCentres] ADD CONSTRAINT [FK_CostCentres_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CostCentres] ADD CONSTRAINT [FK_CostCentres_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CostCentres] ADD CONSTRAINT [FK_CostCentres_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[CostCentres] ADD CONSTRAINT [FK_CostCentres_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CustomFieldDefinitions] ADD CONSTRAINT [FK_CustomFieldDefinitions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CustomFieldDefinitions] ADD CONSTRAINT [FK_CustomFieldDefinitions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CustomFieldDefinitions] ADD CONSTRAINT [FK_CustomFieldDefinitions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[CustomFieldDefinitions] ADD CONSTRAINT [FK_CustomFieldDefinitions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_SavedReports_SavedReportId] FOREIGN KEY ([SavedReportId]) REFERENCES [cfg].[SavedReports] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[DashboardPins] ADD CONSTRAINT [FK_DashboardPins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[EInvoiceSettings] ADD CONSTRAINT [FK_EInvoiceSettings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[EInvoiceSettings] ADD CONSTRAINT [FK_EInvoiceSettings_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[EInvoiceSettings] ADD CONSTRAINT [FK_EInvoiceSettings_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[EInvoiceSettings] ADD CONSTRAINT [FK_EInvoiceSettings_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[EntryLines] ADD CONSTRAINT [FK_EntryLines_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [acc].[JournalEntries] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[EntryLines] ADD CONSTRAINT [FK_EntryLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[EntryLines] ADD CONSTRAINT [FK_EntryLines_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[EntryLines] ADD CONSTRAINT [FK_EntryLines_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[EntryLines] ADD CONSTRAINT [FK_EntryLines_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[ImportBatches] ADD CONSTRAINT [FK_ImportBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[ImportBatches] ADD CONSTRAINT [FK_ImportBatches_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[ImportBatches] ADD CONSTRAINT [FK_ImportBatches_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[ImportBatches] ADD CONSTRAINT [FK_ImportBatches_Users_ImportedBy] FOREIGN KEY ([ImportedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[ImportBatches] ADD CONSTRAINT [FK_ImportBatches_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [core].[Roles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Users_InvitedBy] FOREIGN KEY ([InvitedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Invitations] ADD CONSTRAINT [FK_Invitations_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_JournalExportBatches_ExportBatchId] FOREIGN KEY ([ExportBatchId]) REFERENCES [acc].[JournalExportBatches] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Users_VoidedBy] FOREIGN KEY ([VoidedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalExportBatches] ADD CONSTRAINT [FK_JournalExportBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalExportBatches] ADD CONSTRAINT [FK_JournalExportBatches_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalExportBatches] ADD CONSTRAINT [FK_JournalExportBatches_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalExportBatches] ADD CONSTRAINT [FK_JournalExportBatches_Users_ExportedBy] FOREIGN KEY ([ExportedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[JournalExportBatches] ADD CONSTRAINT [FK_JournalExportBatches_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[MaintenanceLogs] ADD CONSTRAINT [FK_MaintenanceLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[MaintenanceLogs] ADD CONSTRAINT [FK_MaintenanceLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Notifications] ADD CONSTRAINT [FK_Notifications_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Notifications] ADD CONSTRAINT [FK_Notifications_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Notifications] ADD CONSTRAINT [FK_Notifications_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Notifications] ADD CONSTRAINT [FK_Notifications_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Notifications] ADD CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[NumberSequences] ADD CONSTRAINT [FK_NumberSequences_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[NumberSequences] ADD CONSTRAINT [FK_NumberSequences_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[NumberSequences] ADD CONSTRAINT [FK_NumberSequences_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[NumberSequences] ADD CONSTRAINT [FK_NumberSequences_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PendingPostings] ADD CONSTRAINT [FK_PendingPostings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PendingPostings] ADD CONSTRAINT [FK_PendingPostings_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PendingPostings] ADD CONSTRAINT [FK_PendingPostings_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PendingPostings] ADD CONSTRAINT [FK_PendingPostings_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Periods] ADD CONSTRAINT [FK_Periods_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Periods] ADD CONSTRAINT [FK_Periods_Users_ClosedBy] FOREIGN KEY ([ClosedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Periods] ADD CONSTRAINT [FK_Periods_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Periods] ADD CONSTRAINT [FK_Periods_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[Periods] ADD CONSTRAINT [FK_Periods_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Permissions] ADD CONSTRAINT [FK_Permissions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Permissions] ADD CONSTRAINT [FK_Permissions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Permissions] ADD CONSTRAINT [FK_Permissions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Plans] ADD CONSTRAINT [FK_Plans_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Plans] ADD CONSTRAINT [FK_Plans_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Plans] ADD CONSTRAINT [FK_Plans_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PostingKeyMappings] ADD CONSTRAINT [FK_PostingKeyMappings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PostingKeyMappings] ADD CONSTRAINT [FK_PostingKeyMappings_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PostingKeyMappings] ADD CONSTRAINT [FK_PostingKeyMappings_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [acc].[PostingKeyMappings] ADD CONSTRAINT [FK_PostingKeyMappings_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[RecordLocks] ADD CONSTRAINT [FK_RecordLocks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[RecordLocks] ADD CONSTRAINT [FK_RecordLocks_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[RecordLocks] ADD CONSTRAINT [FK_RecordLocks_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[RecordLocks] ADD CONSTRAINT [FK_RecordLocks_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[RecordLocks] ADD CONSTRAINT [FK_RecordLocks_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [core].[Roles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Roles] ADD CONSTRAINT [FK_Roles_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Roles] ADD CONSTRAINT [FK_Roles_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Roles] ADD CONSTRAINT [FK_Roles_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Roles] ADD CONSTRAINT [FK_Roles_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[SavedReports] ADD CONSTRAINT [FK_SavedReports_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[SavedReports] ADD CONSTRAINT [FK_SavedReports_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[SavedReports] ADD CONSTRAINT [FK_SavedReports_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[SavedReports] ADD CONSTRAINT [FK_SavedReports_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[SavedReports] ADD CONSTRAINT [FK_SavedReports_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[StandardFieldConfigs] ADD CONSTRAINT [FK_StandardFieldConfigs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[StandardFieldConfigs] ADD CONSTRAINT [FK_StandardFieldConfigs_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[StandardFieldConfigs] ADD CONSTRAINT [FK_StandardFieldConfigs_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[StandardFieldConfigs] ADD CONSTRAINT [FK_StandardFieldConfigs_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SubscriptionInvoices] ADD CONSTRAINT [FK_SubscriptionInvoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SubscriptionInvoices] ADD CONSTRAINT [FK_SubscriptionInvoices_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SubscriptionInvoices] ADD CONSTRAINT [FK_SubscriptionInvoices_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SubscriptionInvoices] ADD CONSTRAINT [FK_SubscriptionInvoices_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Users_ApprovedBy] FOREIGN KEY ([ApprovedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Users_RequestedBy] FOREIGN KEY ([RequestedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[SupportAccessGrants] ADD CONSTRAINT [FK_SupportAccessGrants_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[TableColumnViews] ADD CONSTRAINT [FK_TableColumnViews_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[TableColumnViews] ADD CONSTRAINT [FK_TableColumnViews_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[TableColumnViews] ADD CONSTRAINT [FK_TableColumnViews_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[TableColumnViews] ADD CONSTRAINT [FK_TableColumnViews_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [cfg].[TableColumnViews] ADD CONSTRAINT [FK_TableColumnViews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Tenants] ADD CONSTRAINT [FK_Tenants_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Tenants] ADD CONSTRAINT [FK_Tenants_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    ALTER TABLE [core].[Tenants] ADD CONSTRAINT [FK_Tenants_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008105537_InitialMasterDatabase'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261008105537_InitialMasterDatabase', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    DROP INDEX [UX_Users_Email] ON [core].[Users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    DROP INDEX [UX_Users_NormalizedEmail] ON [core].[Users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[core].[Users]') AND [c].[name] = N'NormalizedEmail');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [core].[Users] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [core].[Users] ALTER COLUMN [NormalizedEmail] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[core].[Users]') AND [c].[name] = N'Email');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [core].[Users] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [core].[Users] ALTER COLUMN [Email] nvarchar(150) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    ALTER TABLE [core].[Users] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Users_MustChangePassword] DEFAULT (0);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    ALTER TABLE [core].[Users] ADD [NormalizedUserName] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    ALTER TABLE [core].[Users] ADD [UserName] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_Email] ON [core].[Users] ([Email]) WHERE [IsDeleted] = 0 AND [Email] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_NormalizedEmail] ON [core].[Users] ([NormalizedEmail]) WHERE [IsDeleted] = 0 AND [NormalizedEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_NormalizedUserName] ON [core].[Users] ([NormalizedUserName]) WHERE [IsDeleted] = 0 AND [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    EXEC(N'ALTER TABLE [core].[Users] ADD CONSTRAINT [CK_Users_Email_UserName] CHECK ([Email] IS NOT NULL OR [UserName] IS NOT NULL)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017111652_Rev6_UserNameSignIn'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017111652_Rev6_UserNameSignIn', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017113752_Rev6_RequireTwoFactor'
)
BEGIN
    ALTER TABLE [core].[Tenants] ADD [RequireTwoFactor] bit NOT NULL CONSTRAINT [DF_Tenants_RequireTwoFactor] DEFAULT (0);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017113752_Rev6_RequireTwoFactor'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017113752_Rev6_RequireTwoFactor', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017121155_Rev6_JournalReferenceAndDrafts'
)
BEGIN
    ALTER TABLE [acc].[JournalEntries] ADD [Reference] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017121155_Rev6_JournalReferenceAndDrafts'
)
BEGIN
    CREATE TABLE [acc].[JournalDrafts] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_JournalDrafts_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [EntryDate] date NULL,
        [Description] nvarchar(500) NULL,
        [Reference] nvarchar(100) NULL,
        [Lines] nvarchar(max) NOT NULL,
        [TotalDebit] decimal(18,3) NOT NULL,
        [TotalCredit] decimal(18,3) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_JournalDrafts_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_JournalDrafts_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_JournalDrafts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_JournalDrafts_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [CK_JournalDrafts_Lines] CHECK (ISJSON([Lines]) = 1),
        CONSTRAINT [FK_JournalDrafts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_JournalDrafts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_JournalDrafts_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_JournalDrafts_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_JournalDrafts_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017121155_Rev6_JournalReferenceAndDrafts'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_JournalDrafts_CompanyId] ON [acc].[JournalDrafts] ([CompanyId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017121155_Rev6_JournalReferenceAndDrafts'
)
BEGIN
    CREATE UNIQUE INDEX [UX_JournalDrafts_PublicId] ON [acc].[JournalDrafts] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017121155_Rev6_JournalReferenceAndDrafts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017121155_Rev6_JournalReferenceAndDrafts', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    CREATE TABLE [trn].[TRN_ReceiptAllocations] (
        [Id] bigint NOT NULL IDENTITY,
        [PublicId] uniqueidentifier NOT NULL CONSTRAINT [DF_TRN_ReceiptAllocations_PublicId] DEFAULT (NEWID()),
        [TenantId] bigint NOT NULL,
        [CompanyId] bigint NOT NULL,
        [ReceiptId] bigint NOT NULL,
        [InvoiceId] bigint NOT NULL,
        [Amount] decimal(18,3) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL CONSTRAINT [DF_TRN_ReceiptAllocations_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] bigint NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] bigint NULL,
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_TRN_ReceiptAllocations_IsDeleted] DEFAULT (0),
        [DeletedAt] datetime2(3) NULL,
        [DeletedBy] bigint NULL,
        [DeleteReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_TRN_ReceiptAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TRN_ReceiptAllocations_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TRN_ReceiptAllocations_DeletedAt_DeletedBy_DeleteReason] CHECK ([IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)),
        CONSTRAINT [FK_TRN_ReceiptAllocations_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [core].[Companies] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_TRN_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [trn].[TRN_Invoices] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_TRN_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [trn].[TRN_Receipts] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_Users_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_Users_DeletedBy] FOREIGN KEY ([DeletedBy]) REFERENCES [core].[Users] ([Id]),
        CONSTRAINT [FK_TRN_ReceiptAllocations_Users_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [core].[Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    CREATE INDEX [IX_TRN_ReceiptAllocations_InvoiceId] ON [trn].[TRN_ReceiptAllocations] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TRN_ReceiptAllocations_PublicId] ON [trn].[TRN_ReceiptAllocations] ([PublicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TRN_ReceiptAllocations_ReceiptId_InvoiceId] ON [trn].[TRN_ReceiptAllocations] ([ReceiptId], [InvoiceId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    INSERT INTO [trn].[TRN_ReceiptAllocations] ([PublicId], [TenantId], [CompanyId], [ReceiptId], [InvoiceId], [Amount], [CreatedAt], [CreatedBy], [IsDeleted])
    SELECT NEWID(), r.[TenantId], r.[CompanyId], r.[Id], r.[InvoiceId], r.[Amount], r.[CreatedAt], r.[CreatedBy], 0
    FROM [trn].[TRN_Receipts] r
    WHERE r.[InvoiceId] IS NOT NULL AND r.[IsDeleted] = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017122900_Rev6_ReceiptAllocations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017122900_Rev6_ReceiptAllocations', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017131559_Rev6_SupplierBillDeductions'
)
BEGIN
    ALTER TABLE [acc].[ApArDocuments] ADD [Deduction] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArDocuments_Deduction] DEFAULT (0);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017131559_Rev6_SupplierBillDeductions'
)
BEGIN
    ALTER TABLE [acc].[ApArDocumentLines] ADD [Deduction] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArDocumentLines_Deduction] DEFAULT (0);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017131559_Rev6_SupplierBillDeductions'
)
BEGIN
    EXEC(N'ALTER TABLE [acc].[ApArDocuments] ADD CONSTRAINT [CK_ApArDocuments_Deduction] CHECK ([Deduction] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017131559_Rev6_SupplierBillDeductions'
)
BEGIN
    EXEC(N'ALTER TABLE [acc].[ApArDocumentLines] ADD CONSTRAINT [CK_ApArDocumentLines_Deduction] CHECK ([Deduction] >= 0 AND [Deduction] <= [Amount])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017131559_Rev6_SupplierBillDeductions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017131559_Rev6_SupplierBillDeductions', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017141251_Rev6_ReturnDeductions'
)
BEGIN
    ALTER TABLE [acc].[ApArReturns] ADD [Deduction] decimal(18,3) NOT NULL CONSTRAINT [DF_ApArReturns_Deduction] DEFAULT (0);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017141251_Rev6_ReturnDeductions'
)
BEGIN
    EXEC(N'ALTER TABLE [acc].[ApArReturns] ADD CONSTRAINT [CK_ApArReturns_Deduction] CHECK ([Deduction] >= 0 AND [Deduction] <= [Amount])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261017141251_Rev6_ReturnDeductions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261017141251_Rev6_ReturnDeductions', N'10.0.12');
END;

COMMIT;
GO

