using System.Text.RegularExpressions;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Platform.Contracts.Modules;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// This application's notification types for the platform's notification service (Stage 2 session 14; DT-189): rejected e-invoices,
/// e-invoicing set up wrongly (one per company, its count of affected documents worked out when shown, the older per-document notifications
/// of refused credentials merged into it), failed postings; the links to invoices, credit notes, receivable documents and returns, pending
/// postings and the e-invoicing settings, resolved again when shown with the reader's permissions.
/// </summary>
public sealed class TransportationNotificationTypes(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens) : INotificationTypeProvider
{
    public const string EInvoiceRejected = "EINV.Rejected";
    public const string EInvoiceSetupError = "EINV.SetupError";
    public const string PostingFailed = "ACC.PostingFailed";

    public IReadOnlyCollection<string> Types => TransportationNotificationTexts.Types;

    public NotificationText Render(string type, string message, string language, IReadOnlyDictionary<string, string>? extra = null) =>
        TransportationNotificationTexts.Render(type, message, language, extra);

    public Dictionary<string, string>? Parameters(string type, string message) => TransportationNotificationTexts.Parameters(type, message);

    /// <summary>Older versions notified every document refused for the credentials: replaced by the single set-up notification (DT-189).</summary>
    public IReadOnlyList<NotificationMerge> Merges { get; } =
        [new(EInvoiceRejected, EInvoiceOutcome.CredentialsRefusedText, EInvoiceSetupError, TransportationNotificationLinks.EInvoiceSettings, "reason")];

    public async Task<IReadOnlyDictionary<string, string>?> ExtraAsync(string type, CancellationToken ct) =>
        type == EInvoiceSetupError
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["count"] = PlatformNotifications.Number(await CredentialRejectionsAsync(ct).ConfigureAwait(false)) }
            : null;

    /// <summary>Documents of the current company refused because of the credentials and not resubmitted since (invoices, credit notes, AP/AR).</summary>
    private async Task<int> CredentialRejectionsAsync(CancellationToken ct) =>
        await db.Set<EInvoiceSubmission>().CountAsync(s => s.Status == EInvoiceCodes.Rejected && s.LastError!.StartsWith(EInvoiceOutcome.CredentialsRefusedText), ct).ConfigureAwait(false)
        + await db.Set<ApArEInvoiceSubmission>().CountAsync(s => s.Status == EInvoiceCodes.Rejected && s.LastError!.StartsWith(EInvoiceOutcome.CredentialsRefusedText), ct).ConfigureAwait(false);

    public async Task<NotificationTarget?> ResolveLinkAsync(string link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);
        const string invoice = TransportationNotificationLinks.InvoiceSubmission, apar = TransportationNotificationLinks.ApArSubmission;
        if (link.StartsWith(invoice, StringComparison.Ordinal)) return await SubmissionAsync(link[invoice.Length..], ct).ConfigureAwait(false);
        if (link.StartsWith(apar, StringComparison.Ordinal)) return await ApArSubmissionAsync(link[apar.Length..], ct).ConfigureAwait(false);
        return link switch
        {
            "/ledger/pending-postings" => Screen("accounting/automatic?tab=pending", PermissionKeys.JeExport),
            TransportationNotificationLinks.EInvoiceSettings => Screen("settings/e-invoicing", PermissionKeys.SettingsManage),
            _ => null,
        };
    }

    private NotificationTarget Screen(string route, params string[] permissions) =>
        permissions.Any(ctx.HasPermission) ? new(route, null) : new(null, NotificationTarget.Forbidden);

    /// <summary>A rejected invoice or credit note opens the document itself (the e-invoicing status is shown there).</summary>
    private async Task<NotificationTarget> SubmissionAsync(string token, CancellationToken ct)
    {
        if (tokens.Decode<EInvoiceSubmission>(token) is not Guid pid) return new(null, NotificationTarget.Missing);
        var s = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(x => x.PublicId == pid)
            .Select(x => new { Invoice = x.Invoice != null && !x.Invoice.IsDeleted ? (Guid?)x.Invoice.PublicId : null, Credit = x.CreditNote != null && !x.CreditNote.IsDeleted ? (Guid?)x.CreditNote.PublicId : null })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (s?.Invoice is Guid i) return Screen("invoices/" + tokens.Encode<Invoice>(i), PermissionKeys.InvView);
        if (s?.Credit is Guid c) return Screen("credit-notes/" + tokens.Encode<CreditNote>(c), PermissionKeys.InvView);
        return new(null, NotificationTarget.Missing);
    }

    private async Task<NotificationTarget> ApArSubmissionAsync(string token, CancellationToken ct)
    {
        if (tokens.Decode<ApArEInvoiceSubmission>(token) is not Guid pid) return new(null, NotificationTarget.Missing);
        var s = await db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(x => x.PublicId == pid)
            .Select(x => new { Doc = x.ApArDocument != null && !x.ApArDocument.IsDeleted ? (Guid?)x.ApArDocument.PublicId : null, Ret = x.ApArReturn != null && !x.ApArReturn.IsDeleted ? (Guid?)x.ApArReturn.PublicId : null })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (s?.Doc is Guid d) return Screen("accounting/receivables/" + tokens.Encode<ApArDocument>(d), PermissionKeys.AcctApar, PermissionKeys.AcctView);
        if (s?.Ret is Guid r) return Screen("accounting/apar-returns/" + tokens.Encode<ApArReturn>(r), PermissionKeys.AcctApar, PermissionKeys.AcctView);
        return new(null, NotificationTarget.Missing);
    }
}

/// <summary>The stored links of this application's notifications that lead to its records and screens.</summary>
public static class TransportationNotificationLinks
{
    /// <summary>E-invoicing set up wrongly.</summary>
    public const string EInvoiceSettings = "/settings/e-invoicing";

    public const string InvoiceSubmission = "/einvoice-submissions/";
    public const string ApArSubmission = "/apar/e-invoice-submissions/";
}

/// <summary>The words of this application's notification types (DT-189; split from <c>NotificationTexts</c> in Stage 2 session 14, every text unchanged).</summary>
public static partial class TransportationNotificationTexts
{
    private static readonly Dictionary<string, NotificationWords> Catalog = new(StringComparer.Ordinal)
    {
        [TransportationNotificationTypes.EInvoiceRejected] = new("رُفضت فاتورة إلكترونية", "E-invoice rejected",
            "رفض نظام الفوترة الوطني المستند {number}. افتحه لتصحيحه وإعادة إرساله.",
            "The national e-invoicing system refused {number}. Open it to correct and resubmit.", "reason"),
        [TransportationNotificationTypes.EInvoiceSetupError] = new("الفوترة الإلكترونية غير مضبوطة بشكل صحيح", "E-invoicing is not set up correctly",
            "رفض نظام جو فوترة بيانات الاعتماد. افتح إعدادات الفوترة الإلكترونية. الفواتير المتأثرة: {count}",
            "JoFotara refused the credentials. Open e-invoicing settings. Affected invoices: {count}", "reason"),
        [TransportationNotificationTypes.PostingFailed] = new("تعذّر ترحيل قيد", "An entry was not posted",
            "ينتظر القيد في الترحيلات المعلّقة: {description}", "The entry waits in pending postings: {description}"),
    };

    /// <summary>Every type with words (the tests check that each one renders in both languages).</summary>
    public static IReadOnlyCollection<string> Types => Catalog.Keys;

    /// <summary>The parameters of a stored message: the current form, or read back from an older stored text; null when unknown.</summary>
    public static Dictionary<string, string>? Parameters(string type, string message) => NotificationWording.Parameters(message, en => FromOldEnglish(type, en));

    private static Dictionary<string, string>? FromOldEnglish(string type, string en)
    {
        Match m;
        switch (type)
        {
            case TransportationNotificationTypes.EInvoiceRejected when (m = OldRejected().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["number"] = m.Groups["number"].Value.Trim(), ["reason"] = m.Groups["reason"].Value.Trim() };
            case TransportationNotificationTypes.PostingFailed when (m = OldPosting().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["description"] = m.Groups["v"].Value.Trim() };
            default:
                return null;
        }
    }

    /// <summary>The text of a notification in <paramref name="language"/>; extra parameters (e.g. a count computed when shown) are added to the stored ones.</summary>
    public static NotificationText Render(string type, string message, string language, IReadOnlyDictionary<string, string>? extra = null) =>
        NotificationWording.Render(Catalog.GetValueOrDefault(type), Parameters(type, message), message, language, extra);

    [GeneratedRegex(@"^An e-invoice was rejected:\s*(?<number>\S+)\s*—\s*(?<reason>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldRejected();

    [GeneratedRegex(@"^An entry could not be posted and waits in pending postings:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldPosting();
}
