using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Persistence.Seed;

/// <summary>
/// Platform-wide reference data defined by the PRD, seeded through EF Core migration seed data:
/// the 22 permission keys (PRD 3.4, namespaced per owner decision D-01), the three budget keys (revision 6 part B) and the TRIAL plan
/// (owner decision D-12; limits are placeholders the provider edits in the console).
/// Per-tenant and per-company reference data (preset roles, built-in ownership type, settings,
/// number sequences, invoice layouts) is created by the application when a tenant/company is created.
/// </summary>
internal static class ReferenceDataSeed
{
    private static readonly DateTime SeededAt = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>(Id, PublicId, key, group, Arabic, English) — English text from PRD 3.4.</summary>
    internal static readonly (long Id, string PublicId, string Key, string Group, string Ar, string En)[] Permissions =
    [
        (1, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a01", "trn.trips.view", "Trips",
            "عرض الرحلات وسجلّ تغييراتها", "View trips and trip history"),
        (2, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a02", "trn.trips.edit", "Trips",
            "إنشاء الرحلات واستيرادها وتعديلها", "Create, import and edit trips"),
        (3, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a03", "trn.trips.void", "Trips",
            "إلغاء الرحلات مع ذكر السبب", "Void trips with a reason"),
        (4, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a04", "trn.inv.view", "Invoicing",
            "عرض الفواتير والمسودات والإشعارات الدائنة", "View invoices, drafts and credit notes"),
        (5, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a05", "trn.inv.issue", "Invoicing",
            "إنشاء الفواتير (على الرحلات واليدوية)، واعتماد المسودات، وإصدار الإشعارات الدائنة، وتصميم نماذج الفواتير، وإرسال الفواتير الإلكترونية",
            "Create invoices (trip-based and manual), approve drafts, issue credit notes, design invoice layouts, submit e-invoices"),
        (6, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a06", "trn.inv.void", "Invoicing",
            "إلغاء الفواتير والإشعارات الدائنة", "Void invoices and credit notes"),
        (7, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a07", "trn.rcpt.manage", "Receipts",
            "تسجيل سندات القبض من العملاء وإلغاؤها", "Record and void customer receipts"),
        (8, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a08", "trn.drv.view", "Drivers & partners",
            "عرض حسابات السائقين والشركاء وكشوفها", "View driver and partner accounts and statements"),
        (9, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a09", "trn.drv.advance", "Drivers & partners",
            "تسجيل سلف السائقين وتعديلها وإلغاؤها", "Record, edit and void driver advances"),
        (10, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a10", "trn.drv.settle", "Drivers & partners",
            "تسوية حسابات السائقين، وإلغاء التسويات، وتسجيل دفعات الشركاء", "Settle drivers, void settlements, record partner payments"),
        (11, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a11", "acc.je.view", "Journal",
            "عرض القيود اليومية المولّدة تلقائياً", "View automatically generated journal entries"),
        (12, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a12", "acc.je.export", "Journal",
            "ترحيل القيود إلى الأستاذ العام، وإعادة محاولة الترحيلات المعلّقة، وتصدير دفعات القيود",
            "Post entries to the ledger, retry pending postings, export journal batches"),
        (13, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a13", "acc.acct.view", "Accounting",
            "عرض الأستاذ العام وميزان المراجعة والقوائم المالية وكشوف الحسابات",
            "View the general ledger, trial balance, financial statements and account statements"),
        (14, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a14", "acc.acct.post", "Accounting",
            "إنشاء القيود اليدوية وتعديلها وإلغاؤها، واستيراد القيود", "Create, edit and void manual journal entries; import entries"),
        (15, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a15", "acc.acct.apar", "Accounting",
            "إدارة مستندات الذمم الدائنة والمدينة ودفعاتها ومرتجعاتها", "Manage payable and receivable documents, payments and returns"),
        (16, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a16", "acc.acct.setup", "Accounting",
            "إدارة دليل الحسابات ومراكز التكلفة والبنوك", "Manage the chart of accounts, cost centres and banks"),
        (17, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a17", "acc.acct.maint", "Accounting",
            "تنفيذ عمليات صيانة الحسابات: الحذف الجماعي للقيود غير المرتبطة، ومسح بيانات الشركة، واستبدال دليل الحسابات",
            "Run accounting maintenance: bulk deletion of unlinked entries, clearing a company's data, or replacing the chart of accounts"),
        (18, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a18", "trn.reports.view", "Reports & master data",
            "الوصول إلى جميع التقارير ومنشئ التقارير والتقارير المحفوظة", "Access all reports, the report builder and saved reports"),
        (19, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a19", "trn.masters.manage", "Reports & master data",
            "إدارة البيانات الأساسية والتعرفات وأسعار المواقع الشهرية والشركاء، وإدخال أسعار السائقين يدوياً",
            "Manage master data, tariffs, monthly site prices and partners; enter manual driver-rate overrides"),
        (20, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a20", "core.settings.manage", "Administration",
            "إعدادات الشركة وربط الحسابات والضريبة والفترات والترقيم وتهيئة الحقول والنماذج",
            "Company settings, account mapping, tax, periods, numbering, configuration of fields and layouts"),
        (21, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a21", "core.users.manage", "Administration",
            "دعوة المستخدمين وتعديلهم وتعطيلهم وفكّ قفلهم وأرشفتهم وتعيين الأدوار",
            "Invite, edit, disable, unlock and archive users; assign roles"),
        (22, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a22", "core.tenant.manage", "Tenant",
            "الاشتراك والخطة والفوترة، وإنشاء الشركات وأرشفتها، وإعدادات المشترك العامة",
            "Subscription, plan and billing; create and archive companies; tenant-wide settings"),
        // Revision 6 part B (owner change request 15 Oct 2026): budgets.
        (23, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a23", "acc.budget.view", "Accounting",
            "عرض الموازنات وتقرير الموازنة مقابل الفعلي", "View budgets and the budget vs actual report"),
        (24, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a24", "acc.budget.manage", "Accounting",
            "إنشاء الموازنات وتعديلها واستيرادها ونسخها من فعلي السنة السابقة وحذف المسودات",
            "Create, edit and import budgets, copy them from last year's actuals, delete drafts"),
        (25, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a25", "acc.budget.approve", "Accounting",
            "اعتماد الموازنات وإعادتها إلى مسودة", "Approve budgets and return them to draft"),
    ];

    internal static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Permission>().HasData(Permissions.Select(p => new Permission
        {
            Id = p.Id,
            PublicId = Guid.Parse(p.PublicId),
            PermissionKey = p.Key,
            GroupName = p.Group,
            DescriptionAr = p.Ar,
            DescriptionEn = p.En,
            CreatedAt = SeededAt,
        }));

        modelBuilder.Entity<Plan>().HasData(new Plan
        {
            Id = 1,
            PublicId = Guid.Parse("3f9a1c7e-2d44-4b8e-8f1a-6c0b9e2d7a01"),
            Code = "TRIAL",
            NameAr = "تجريبية",
            NameEn = "Trial",
            Price = 0m,
            BillingCycle = "Monthly",
            MaxCompanies = 2,
            MaxActiveUsers = 5,
            MaxTripsPerMonth = 2000,
            MaxStorageMb = 1024,
            IsActive = false,
            CreatedAt = SeededAt,
        });
    }
}
