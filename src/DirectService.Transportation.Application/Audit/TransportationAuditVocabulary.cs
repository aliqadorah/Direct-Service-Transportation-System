using System.Globalization;
using System.Text.Json;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using static DirectService.SharedKernel.AuditWords;
using static DirectService.Transportation.Application.Audit.AuditVocabulary;

namespace DirectService.Transportation.Application.Audit;

/// <summary>
/// The words and rules of this application for the shared audit describer (Stage 2 session 9, DT-329): the vocabulary of
/// <see cref="AuditVocabulary"/> (unchanged) and the rules only this application knows — a journal entry shown with its lines, link rows
/// named by the user or role they concern, the documents named by actions, the draft-invoice wording, the extras of some actions, driver
/// personal fields masked without drv.view, the codes, settings and record names in words, the payable / receivable link kinds and the
/// users read without filters.
/// </summary>
public sealed class TransportationAuditVocabulary(IRequestContext ctx) : IAuditVocabularyProvider
{
    private const string Dash = "—";
    private const string JournalEntry = "ACC.JournalEntry";
    private const string EntryLine = "ACC.EntryLine";

    private static string L(bool en, string ar, string enText) => en ? enText : ar;

    // ------------------------------------------------------------ vocabulary

    public IReadOnlyDictionary<string, EntityWords> Entities => AuditVocabulary.Entities;

    public IReadOnlyDictionary<string, ActionWords> Actions => AuditVocabulary.Actions;

    public IReadOnlyDictionary<string, FieldWords> Fields => AuditVocabulary.Fields;

    public IReadOnlyDictionary<string, FieldWords> EntityFields => AuditVocabulary.EntityFields;

    public IReadOnlyDictionary<string, Words> Codes => AuditVocabulary.Codes;

    public IReadOnlySet<string> Hidden => AuditVocabulary.Hidden;

    public IReadOnlyDictionary<string, IReadOnlySet<string>> PersonalFields => AuditVocabulary.PersonalFields;

    /// <summary>The lines saved with each journal entry (the interceptor writes one audit row per line in the same save, item 6).</summary>
    public AuditLines? Lines { get; } = new(JournalEntry, EntryLine, "JournalEntryId", "LineNo");

    /// <summary>A payable or a receivable opens its own screen (its document kind).</summary>
    public string? LinkKind(string entityType, object? record) =>
        entityType == "ACC.ApArDocument" ? (record as ApArDocument)?.DocumentKind : AuditVocabulary.LinkKind(entityType);

    public string? ActionKey(string action, string? entityType) => action == "Approve" && entityType == "CORE.SupportAccessGrant" ? "Approve.Support" : null;

    // ------------------------------------------------------------ references

    /// <summary>Rows that concern a user (a role in the company, a dashboard pin, recovery codes): named by that user.</summary>
    private static bool IsUserRow(string? entityType) => entityType is "CORE.UserCompanyRole" or "CFG.DashboardPin" or "CORE.UserRecoveryCode";

    public bool TryTarget(string? entityType, string column, ParsedAuditRow row, out string? target)
    {
        ArgumentNullException.ThrowIfNull(row);
        (var handled, target) = column switch
        {
            "PartyId" when entityType == EntryLine => (true, PartyEntity(row.Str("PartyType"))),
            "SourceId" => (true, null),
            "UserId" when IsUserRow(entityType) => (true, "CORE.User"),
            _ => (false, (string?)null),
        };
        return handled;
    }

    public IEnumerable<(string Type, long? Id)> Related(ParsedAuditRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var type = row.Row.EntityType;
        if (type == JournalEntry && SourceEntity(row.Str("SourceType")) is { } source) yield return (source, row.Whole("SourceId"));
        if (IsUserRow(type)) yield return ("CORE.User", row.Whole("UserId") ?? row.Whole("UserId", old: true));
    }

    /// <summary>Link rows are named by whom they concern: a user's role in the company, a role's permission.</summary>
    public (string Type, long Id)? NamedBy(ParsedAuditRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var type = row.Row.EntityType;
        if (IsUserRow(type) && (row.Whole("UserId") ?? row.Whole("UserId", old: true)) is long user) return ("CORE.User", user);
        if (type == "CORE.RolePermission" && (row.Whole("RoleId") ?? row.Whole("RoleId", old: true)) is long role) return ("CORE.Role", role);
        return null;
    }

    // ------------------------------------------------------------ documents named by actions

    public (string Type, long? Id, string? Number)? DocumentOf(ParsedAuditRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var reason = row.Reason;
        return row.Row.Action switch
        {
            "AddedToInvoice" => ("TRN.Invoice", row.Whole("InvoiceId"), null),
            "RemovedFromInvoice" => ("TRN.Invoice", row.Whole("InvoiceId", old: true), LooksLikeNumber(reason) ? reason : null),
            "Invoiced" => ("TRN.Invoice", null, reason),
            "Settled" => ("TRN.Settlement", row.Whole("SettlementId"), reason),
            "SettlementVoided" => ("TRN.Settlement", row.Whole("SettlementId", old: true), reason),
            "ReturnedByCreditNote" or "CreditNote" or "CreditNoteVoided" => ("TRN.CreditNote", null, reason),
            "Receipt" or "ReceiptVoid" => ("TRN.Receipt", null, reason),
            "Payment" or "PaymentVoid" => ("ACC.ApArPayment", null, reason),
            "Return" or "ReturnVoid" => ("ACC.ApArReturn", null, reason),
            "Reverse" => (JournalEntry, row.Whole("ReversedByEntryId"), reason),
            "Edit" when row.Row.EntityType == JournalEntry => (JournalEntry, null, reason?.Split('→')[0].Trim()),
            _ => null,
        };
    }

    private static readonly HashSet<string> DocumentActions =
    [
        "AddedToInvoice", "RemovedFromInvoice", "Invoiced", "Settled", "SettlementVoided", "ReturnedByCreditNote", "CreditNote", "CreditNoteVoided",
        "Receipt", "ReceiptVoid", "Payment", "PaymentVoid", "Return", "ReturnVoid", "Reverse", "Edit", "Accepted", "LinkApArParty", "Approve",
    ];

    public bool TellsReason(string action) => DocumentActions.Contains(action);

    /// <summary>Columns an action already tells in its sentence (e.g. the invoice of a trip added to it).</summary>
    private static readonly Dictionary<string, string[]> ToldByAction = new(StringComparer.Ordinal)
    {
        ["AddedToInvoice"] = ["InvoiceId"],
        ["RemovedFromInvoice"] = ["InvoiceId"],
        ["Invoiced"] = ["InvoiceId"],
        ["Settled"] = ["SettlementId"],
        ["SettlementVoided"] = ["SettlementId"],
        ["ReturnedByCreditNote"] = ["InvoiceId"],
        ["Reverse"] = ["ReversedByEntryId"],
        ["Void"] = ["VoidReason"],
        ["Close"] = ["Reason"],
        ["Reopen"] = ["Reason"],
    };

    public IReadOnlyCollection<string> ToldBy(string action) => ToldByAction.GetValueOrDefault(action) ?? [];

    public string? DocumentPhrase(string action, string type, string? number, bool found, bool en)
    {
        // Added to a draft that has its number now: "draft invoice INV-00001" (owner's example).
        if (action == "AddedToInvoice" && found && LooksLikeNumber(number)) return L(en, $"مسودة الفاتورة {number}", $"draft invoice {number}");
        if (type == "TRN.Invoice" && (number is null || number == DraftWord(false) || number == DraftWord(true) || number == L(en, "مسودة", "draft")))
            return L(en, "مسودة فاتورة", "a draft invoice");
        return null;
    }

    private static string DraftWord(bool en) => en ? "Draft invoice" : "مسودة فاتورة";

    // ------------------------------------------------------------ action extras

    public AuditActionDetail? Detail(ParsedAuditRow row, IAuditRun run)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(run);
        var en = run.En;
        switch (row.Row.Action)
        {
            case "Post" or "Import" or "Create" when row.Row.EntityType == JournalEntry:
                var import = row.Row.Action == "Import";
                if (SourceEntity(row.Str("SourceType")) is { } sourceType && row.Whole("SourceId") is long sourceId && run.Name(sourceType, sourceId) is { } source)
                    return new AuditActionDetail($" ({SourceTitle(row.Str("SourceType"), sourceType, en)} {source})", DropReason: import, Link: run.Link(sourceType, sourceId));
                if (import && row.Reason is { } file) return new AuditActionDetail($" ({L(en, "استيراد", "import")} {file})", DropReason: true);
                return import ? new AuditActionDetail(DropReason: true) : null;
            case "Accepted":
                return new AuditActionDetail(row.Reason ?? Dash);
            case "LinkApArParty":
                return new AuditActionDetail(Document: row.Reason);
            case "SetupDone" or "SetupReopened":
                return new AuditActionDetail(row.Reason ?? "", DropReason: true);
        }
        return null;
    }

    private static string SourceTitle(string? sourceType, string entityType, bool en) =>
        sourceType is "SupplierBill" or "Receivable" ? Code(sourceType, en) : Entity(entityType).Title.In(en);

    // ------------------------------------------------------------ reasons, columns, values

    public string? ReasonPart(string part, bool en)
    {
        ArgumentNullException.ThrowIfNull(part);
        return part.StartsWith("Statement ", StringComparison.Ordinal) ? L(en, "كشف حساب ", "Statement ") + Dates(part["Statement ".Length..]) : null;
    }

    private static string Dates(string text) =>
        string.Join("…", text.Split('…').Select(x => DateOnly.TryParseExact(x.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : x));

    public bool HidesColumn(string? entityType, string column) => entityType == EntryLine && column is "PartyType" or "PartyModule";

    /// <summary>Personal fields of drivers shown only with drv.view (DT-36, review M4).</summary>
    private static readonly HashSet<string> DriverPersonal = ["NationalId", "Iban", "CliqAlias", "BankName", "Phone1", "Phone2", "Address"];

    public bool Masks(string? entityType, string column) => entityType == "TRN.Driver" && DriverPersonal.Contains(column) && !ctx.HasPermission(PermissionKeys.DrvView);

    public Kind? ValueKind(string? entityType, string column) => entityType == "CFG.CompanySetting" && column == "SettingValue" ? Kind.Auto : null;

    public string? CodeText(string? entityType, string column, string code, bool en) => column switch
    {
        "PostingKey" => PostingKeys.All.FirstOrDefault(k => k.Key == code) is { } k ? (en ? k.NameEn : k.NameAr) : code,
        "SettingKey" when code.StartsWith("CORE.Setup.", StringComparison.Ordinal) => L(en, "خطوة إعداد ", "Setup step ") + code["CORE.Setup.".Length..],
        "Status" when entityType == "TRN.Trip" && code == "Active" => L(en, "نشطة", "Active"),
        _ => null,
    };

    /// <summary>A company setting's value ("true", "2.5") in words; other text as typed.</summary>
    public string? TextValue(string? entityType, ParsedAuditRow row, string text, bool en)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (entityType != "CFG.CompanySetting") return null;
        if (bool.TryParse(text, out var b)) return b ? L(en, "نعم", "Yes") : L(en, "لا", "No");
        if (row.Str("SettingKey") == "TRN.ShortageTolerancePct" && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct))
            return pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        return text;
    }

    // ------------------------------------------------------------ record names

    public bool TryRecordName(string entityType, Func<string, string?> value, bool loaded, bool en, out string? name)
    {
        ArgumentNullException.ThrowIfNull(value);
        var S = value;
        (var handled, name) = entityType switch
        {
            "TRN.Invoice" when loaded => (true, S("Number") ?? L(en, "مسودة", "draft")),
            "CORE.Permission" when loaded => (true, en ? S("DescriptionEn") : S("DescriptionAr")),
            _ => Special() is { } special ? (true, special) : (false, (string?)null),
        };
        return handled;

        string? Special() => entityType switch
        {
            "ACC.Account" => S("Code") is { } code ? $"{code} · {(en ? S("NameEn") ?? S("NameAr") : S("NameAr"))}" : null,
            "CORE.Plan" or "CORE.Company" => en ? S("NameEn") ?? S("NameAr") ?? S("Name") : S("NameAr") ?? S("Name"),
            "CFG.CustomFieldDefinition" => en ? S("LabelEn") ?? S("LabelAr") : S("LabelAr"),
            "CFG.CompanySetting" => S("SettingKey") is { } key ? Code(key, en) : null,
            "ACC.PostingKeyMapping" => S("PostingKey") is { } pk ? PostingKeys.All.FirstOrDefault(k => k.Key == pk) is { } def ? (en ? def.NameEn : def.NameAr) : pk : null,
            "CFG.NumberSequence" => S("DocumentType") is { } dt ? Entity(dt).Title.In(en) : null,
            "CFG.StandardFieldConfig" => S("FieldKey") is { } fk && S("EntityType") is { } et
                ? (Configuration.FieldCatalog.IsEntity(et) && Configuration.FieldCatalog.Find(et, fk) is { } f ? (en ? f.LabelEn : f.LabelAr) : fk) : null,
            "CORE.Role" => S("Name") is { } role ? Code(role, en) : null,
            _ => null,
        };
    }

    // ------------------------------------------------------------ journal lines

    public HistoryLine Line(ParsedAuditRow line, IAuditRun run)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(run);
        var account = line.Whole("AccountId") is long a && run.Name("ACC.Account", a) is { } found ? found : Dash;
        if (PartyEntity(line.Str("PartyType")) is { } partyType && line.Whole("PartyId") is long party && run.Name(partyType, party) is { } who)
            account += " — " + who;
        static decimal Amount(ParsedAuditRow l, string column) => l.Value(column) is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var n) ? n : 0;
        var debit = Amount(line, "Debit");
        var credit = Amount(line, "Credit");
        return new HistoryLine(account, debit == 0 ? null : debit.ToString("#,##0.000", CultureInfo.InvariantCulture),
            credit == 0 ? null : credit.ToString("#,##0.000", CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------ users

    /// <summary>Users: names only, of ids found in this log (platform support staff have no tenant).</summary>
    public bool ReadsUnfiltered(string entityType) => entityType == "CORE.User";

    public bool KeepsLastName(string entityType) => entityType != "CORE.User";
}
