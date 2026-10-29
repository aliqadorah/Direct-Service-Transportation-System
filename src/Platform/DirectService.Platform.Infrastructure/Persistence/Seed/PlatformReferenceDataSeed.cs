using DirectService.Platform.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Infrastructure.Persistence.Seed;

/// <summary>
/// Platform-wide reference data defined by the PRD, seeded through EF Core migration seed data (split by owner in Stage 2 session 11, design
/// a.6): the three platform permission keys (PRD 3.4, namespaced per owner decision D-01), the rows every module adds for its own keys
/// (<see cref="IPermissionSeedProvider"/>) and the TRIAL plan (owner decision D-12; limits are placeholders the provider edits in the console).
/// Same Ids, PublicIds and values as before the split. Per-tenant and per-company reference data (preset roles, built-in ownership type,
/// settings, number sequences, invoice layouts) is created by the application when a tenant/company is created.
/// </summary>
public sealed class PlatformReferenceDataSeed : IPermissionSeedProvider
{
    private static readonly DateTime SeededAt = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The platform's own keys (core.*) — English text from PRD 3.4.</summary>
    public IReadOnlyList<PermissionSeed> Permissions { get; } =
    [
        new(20, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a20", "core.settings.manage", "Administration",
            "إعدادات الشركة وربط الحسابات والضريبة والفترات والترقيم وتهيئة الحقول والنماذج",
            "Company settings, account mapping, tax, periods, numbering, configuration of fields and layouts"),
        new(21, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a21", "core.users.manage", "Administration",
            "دعوة المستخدمين وتعديلهم وتعطيلهم وفكّ قفلهم وأرشفتهم وتعيين الأدوار",
            "Invite, edit, disable, unlock and archive users; assign roles"),
        new(22, "7c1f4a2e-0b51-4f0e-9a0d-1d4b5d0c6a22", "core.tenant.manage", "Tenant",
            "الاشتراك والخطة والفوترة، وإنشاء الشركات وأرشفتها، وإعدادات المشترك العامة",
            "Subscription, plan and billing; create and archive companies; tenant-wide settings"),
    ];

    internal static void Apply(ModelBuilder modelBuilder, IEnumerable<IPermissionSeedProvider> modules)
    {
        var rows = new PlatformReferenceDataSeed().Permissions.Concat(modules.SelectMany(m => m.Permissions)).OrderBy(p => p.Id).ToList();
        if (rows.DistinctBy(p => p.Id).Count() != rows.Count || rows.DistinctBy(p => p.Key).Count() != rows.Count)
            throw new InvalidOperationException("A permission seed row (Id or key) is given by more than one module.");
        modelBuilder.Entity<Permission>().HasData(rows.Select(p => new Permission
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
