using System.Globalization;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Identity;
using DirectService.Platform.Contracts.Modules;

namespace DirectService.Transportation.Application.Modules;

/// <summary>
/// The steps of the setup checklist (5.4, P-08, G-10) worked out from Transportation and accounting data: market settings, chart of accounts,
/// account mapping, master data, prices (<see cref="ISetupStepProvider"/>, moved unchanged out of <c>SetupChecklistService</c>, DT-346).
/// </summary>
public sealed class TransportationSetupSteps(IAppDbContext db, PostingEngine posting, MasterDataSupport support) : ISetupStepProvider
{
    private const string Done = "Done";
    private const string InProgress = "InProgress";
    private const string NotStarted = "NotStarted";

    public IReadOnlyList<string> Steps { get; } = ["market", "chart", "mapping", "masters", "prices"];

    public async Task<IReadOnlyList<SetupStepState>> StatesAsync(string language, CancellationToken ct)
    {
        var En = language == "en";
        var states = new List<SetupStepState>();
        void Add(string key, string ar, string en, string status, string? detail, string link, IReadOnlyList<string>? items = null) =>
            states.Add(new SetupStepState(key, ar, en, status, detail, link, items));

        var einv = await db.Set<EInvoiceSettings>().AsNoTracking().Select(e => e.IsEnabled).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Add("market", "إعدادات السوق", "Market settings", einv ? Done : InProgress,
            einv ? null : En ? "Currency and tax are set; e-invoicing credentials are optional at this stage" : "العملة والضريبة مضبوطتان؛ بيانات الفوترة الإلكترونية اختيارية الآن", "/settings/e-invoicing");

        var accounts = await db.Set<Account>().AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        Add("chart", "دليل الحسابات", "Chart of accounts", accounts > 0 ? Done : NotStarted, null, "/accounting/accounts");

        var missing = await posting.MissingKeysAsync(ct).ConfigureAwait(false);
        var mapped = await db.Set<PostingKeyMapping>().AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        Add("mapping", "ربط الحسابات", "Account mapping", missing.Count == 0 ? Done : mapped > 0 ? InProgress : NotStarted,
            // DT-224: a count in words and the Account mapping page's names, never the internal posting-key codes.
            missing.Count == 0 ? null
                : En ? (missing.Count == 1 ? "1 posting account still needs to be linked to your chart of accounts" : $"{missing.Count} posting accounts still need to be linked to your chart of accounts")
                : missing.Count == 1 ? "حساب ترحيل واحد لم يُربط بعد بدليل الحسابات" : $"{missing.Count} من حسابات الترحيل لم تُربط بعد بدليل الحسابات",
            "/accounting/posting-keys", missing.Count == 0 ? null : PostingKeys.Names(missing, language));

        var masters = new (string Ar, string En, bool Has)[]
        {
            ("السائقون", "drivers", await db.Set<Driver>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("العملاء", "customers", await db.Set<Customer>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("المواد", "materials", await db.Set<Material>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("مواقع التحميل", "loading sites", await db.Set<LoadingSite>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("مواقع التنزيل", "unloading sites", await db.Set<UnloadingSite>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
        };
        var lacking = masters.Where(m => !m.Has).Select(m => En ? m.En : m.Ar).ToList();
        Add("masters", "البيانات الرئيسية", "Master data", lacking.Count == 0 ? Done : lacking.Count < masters.Length ? InProgress : NotStarted,
            lacking.Count == 0 ? null : string.Join("، ", lacking), "/drivers");

        var tariffs = await db.Set<Tariff>().AsNoTracking().AnyAsync(t => t.PartyType == "Customer", ct).ConfigureAwait(false);
        var month = (await support.TodayAsync(ct).ConfigureAwait(false)).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var activeSites = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        var rated = await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => r.RateMonth == month).Select(r => r.LoadingSiteId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var ratesComplete = activeSites.Count > 0 && activeSites.All(rated.Contains);
        Add("prices", "الأسعار", "Prices", tariffs && ratesComplete ? Done : tariffs || rated.Count > 0 ? InProgress : NotStarted,
            tariffs && ratesComplete ? null : En ? "Customer tariffs and this month's driver rates for every loading site" : "تعرفة العملاء وأسعار السائقين لهذا الشهر لكل موقع تحميل",
            "/monthly-rates");
        return states;
    }
}
