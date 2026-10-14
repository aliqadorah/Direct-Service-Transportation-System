using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Application.Audit.AuditVocabulary;

namespace DirectService.Transportation.Application.Audit;

/// <summary>An audit row as stored (core.AuditLogs), the input of <see cref="AuditDescriber"/>.</summary>
public sealed record AuditRow(long Id, DateTime OccurredAt, long? UserId, string Action, string? EntityType, long? EntityId, string? OldValues, string? NewValues);

/// <summary>
/// An audit row in the reader's words: who, the action and record type in words, the record by its number or name, the event as a sentence
/// (<see cref="Sentence"/> with the user, <see cref="Predicate"/> without), the reason, the changes (labels, formatted values, technical
/// columns left out), the lines of a journal entry posted with it and the documents it names.
/// </summary>
public sealed record AuditDescription(AuditRow Row, string? User, string ActionLabel, string EntityLabel, string? Record, string Sentence, string Predicate,
    string? Reason, IReadOnlyList<FieldChange> Changes, IReadOnlyList<HistoryLine> Lines, IReadOnlyList<HistoryLink> Links)
{
    /// <summary>An edit that changed only technical columns (e.g. the journal link set by a posting): not a business event of the record.</summary>
    public bool IsTechnicalOnly => Row.Action == "Update" && Changes.Count == 0 && Reason is null && Lines.Count == 0;
}

/// <summary>Options of a description run.</summary>
/// <param name="CreateValues">Show the values of a created record as changes (audit log); the record's own history does not repeat them.</param>
/// <param name="LinkRecord">Add a link to the record itself (audit log); a record's history is on its own screen.</param>
public sealed record DescribeOptions(bool CreateValues = false, bool LinkRecord = false);

/// <summary>
/// The central audit describer (owner feedback 14 Oct 2026, DT-188): turns audit rows into readable events in the reader's language for
/// every History panel and the audit log (screen and exports). Labels come from <see cref="AuditVocabulary"/>, the company's Field
/// configuration labels and its custom fields; ids are resolved to document numbers, account code and name, names of people, places and
/// users — soft-deleted records by their name, records no longer found by the last name the log recorded. Only records of the reader's
/// tenant and company are read (the query filters stay on). Presentation only: nothing stored is changed.
/// </summary>
public sealed class AuditDescriber(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens)
{
    private const string Dash = "—";
    private const string JournalEntry = "ACC.JournalEntry";
    private const string EntryLine = "ACC.EntryLine";

    private bool En => ctx.Language == "en";

    // For the run below (a nested class cannot read the primary constructor's parameters).
    private IAppDbContext Db => db;

    private IRequestContext Ctx => ctx;

    private IUrlTokenService Tokens => tokens;

    private string W(Words w) => w.In(En);

    private string L(string ar, string en) => En ? en : ar;

    private static readonly Dictionary<string, Type> Types = typeof(AuditLog).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.Namespace is not null && t.GetProperty("Id")?.PropertyType == typeof(long)
            && t.Namespace.Split('.').Last() is "Acc" or "Cfg" or "Core" or "Trn")
        .ToDictionary(t => t.Namespace!.Split('.').Last().ToUpperInvariant() + "." + t.Name, StringComparer.Ordinal);

    /// <summary>The CLR type of a property of a record type (decides how a value is shown), null when unknown.</summary>
    private static Type? ClrType(string? entityType, string column)
    {
        if (entityType is null || !Types.TryGetValue(entityType, out var t)) return null;
        var p = t.GetProperty(column);
        return p is null ? null : Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
    }

    // ================================================================= entry point

    public async Task<IReadOnlyList<AuditDescription>> DescribeAsync(IReadOnlyList<AuditRow> rows, DescribeOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        if (rows.Count == 0) return [];
        var run = new Run(this, options);
        var parsed = rows.Select(r => new Parsed(r, Parse(r.OldValues), Parse(r.NewValues))).ToList();
        var lines = await LinesAsync(parsed, ct).ConfigureAwait(false);
        await run.PrepareAsync(parsed, lines, ct).ConfigureAwait(false);
        return parsed.Select(p => run.Describe(p, lines.GetValueOrDefault(p.Row.Id) ?? [])).ToList();
    }

    private sealed record Parsed(AuditRow Row, Dictionary<string, JsonElement> Old, Dictionary<string, JsonElement> New)
    {
        public JsonElement? Value(string column) => New.TryGetValue(column, out var v) ? v : Old.TryGetValue(column, out var o) ? o : null;

        public long? Long(string column, bool old = false) =>
            (old ? Old : New).TryGetValue(column, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

        public string? Str(string column) => Value(column) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

        public string? Reason => New.TryGetValue("_reason", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new(StringComparer.Ordinal);
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    // ================================================================= journal lines (item 6)

    /// <summary>
    /// The lines saved with each journal entry created in the rows (the interceptor writes one audit row per line in the same save, at the
    /// same time): matched by the entry's id, or — for rows written before DT-188 with a temporary key — by the save they came with.
    /// </summary>
    private async Task<Dictionary<long, List<Parsed>>> LinesAsync(List<Parsed> rows, CancellationToken ct)
    {
        var entries = rows.Where(p => p.Row.EntityType == JournalEntry && p.Row.EntityId is not null && p.Row.Action != "Update" && p.New.ContainsKey("Number")).ToList();
        var result = new Dictionary<long, List<Parsed>>();
        if (entries.Count == 0) return result;
        var companyId = ctx.CompanyId;
        var times = entries.Select(e => e.Row.OccurredAt).Distinct().ToList();
        var stored = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == EntryLine && a.CompanyId == companyId && times.Contains(a.OccurredAt))
            .OrderBy(a => a.Id).Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues))
            .ToListAsync(ct).ConfigureAwait(false);
        var lines = stored.Select(r => new Parsed(r, Parse(r.OldValues), Parse(r.NewValues))).ToList();
        foreach (var e in entries)
        {
            var id = e.Row.EntityId!.Value;
            var mine = lines.Where(l => l.Row.OccurredAt == e.Row.OccurredAt && l.Long("JournalEntryId") == id).ToList();
            if (mine.Count == 0 && entries.Count(x => x.Row.OccurredAt == e.Row.OccurredAt && x.Row.UserId == e.Row.UserId) == 1)
                mine = lines.Where(l => l.Row.OccurredAt == e.Row.OccurredAt && l.Row.UserId == e.Row.UserId && l.Long("JournalEntryId") is < 0).ToList();
            result[e.Row.Id] = mine.OrderBy(l => l.Long("LineNo") ?? 0).ToList();
        }
        return result;
    }

    // ================================================================= one run

    /// <summary>A record found for an id: its name in the reader's language and its public id (links).</summary>
    private sealed record Found(string Name, Guid? PublicId, long Id);

    private sealed class Run(AuditDescriber d, DescribeOptions options)
    {
        private readonly Dictionary<(string Type, long Id), Found> _byId = [];
        private readonly Dictionary<(string Type, string Number), Found> _byNumber = [];
        private readonly Dictionary<long, string> _users = [];
        private readonly Dictionary<(string Entity, string Key), string> _standardLabels = [];
        private readonly Dictionary<(string Entity, string Key), string> _customLabels = [];
        private HashSet<(string Type, long Id)> _erased = [];
        private TimeZoneInfo? _zone;

        private bool En => d.En;

        private string L(string ar, string en) => d.L(ar, en);

        // ------------------------------------------------------------ preparation (batched look-ups)

        public async Task PrepareAsync(List<Parsed> rows, Dictionary<long, List<Parsed>> lines, CancellationToken ct)
        {
            var wanted = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            var numbers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void Want(string? type, long? id)
            {
                if (type is null || id is not long n || n <= 0) return;
                if (!wanted.TryGetValue(type, out var set)) wanted[type] = set = [];
                set.Add(n);
            }
            void WantNumber(string type, string? number)
            {
                if (string.IsNullOrWhiteSpace(number)) return;
                if (!numbers.TryGetValue(type, out var set)) numbers[type] = set = new(StringComparer.Ordinal);
                set.Add(number.Trim());
            }
            foreach (var p in rows.Concat(lines.Values.SelectMany(l => l)))
            {
                var type = p.Row.EntityType;
                Want(type, p.Row.EntityId);
                foreach (var (column, value) in p.Old.Concat(p.New))
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var id)) continue;
                    var target = Target(type, column, p);
                    if (target is not null) Want(target, id);
                }
                if (type == JournalEntry) Want(SourceEntity(p.Str("SourceType")), p.Long("SourceId"));
                if (DocumentOf(p) is { } doc)
                {
                    Want(doc.Type, doc.Id);
                    WantNumber(doc.Type, doc.Number);
                }
                if (type is "CORE.UserCompanyRole" or "CFG.DashboardPin" or "CORE.UserRecoveryCode") Want("CORE.User", p.Long("UserId") ?? p.Long("UserId", old: true));
            }
            await d.ResolveAsync(wanted, _byId, ct).ConfigureAwait(false);
            await d.ResolveNumbersAsync(numbers, _byNumber, ct).ConfigureAwait(false);
            await d.LastKnownNamesAsync(wanted, _byId, ct).ConfigureAwait(false);

            var userIds = rows.Where(r => r.Row.UserId is not null).Select(r => r.Row.UserId!.Value).Distinct().ToList();
            if (userIds.Count > 0)
            {
                // Names of the users who acted (platform support staff have no tenant, hence no filter; only ids found in this log).
                foreach (var u in await d.Db.Set<User>().IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName })
                             .ToListAsync(ct).ConfigureAwait(false))
                    _users[u.Id] = u.FullName;
            }

            var types = rows.Select(r => r.Row.EntityType).Where(t => t is not null).Select(t => t!).Distinct().ToList();
            if (types.Any(Configuration.FieldCatalog.IsEntity))
            {
                foreach (var s in await d.Db.Set<StandardFieldConfig>().AsNoTracking().Where(s => s.Label != null && types.Contains(s.EntityType))
                             .Select(s => new { s.EntityType, s.FieldKey, s.Label }).ToListAsync(ct).ConfigureAwait(false))
                    _standardLabels[(s.EntityType, s.FieldKey)] = s.Label!;
                // Deleted custom fields keep their labels in history.
                foreach (var c in await d.Db.Set<CustomFieldDefinition>().AsNoTracking().IgnoreQueryFilters(["SoftDelete"]).Where(c => types.Contains(c.EntityType))
                             .Select(c => new { c.EntityType, c.FieldKey, c.LabelAr, c.LabelEn }).ToListAsync(ct).ConfigureAwait(false))
                    _customLabels[(c.EntityType, c.FieldKey)] = En ? c.LabelEn ?? c.LabelAr : c.LabelAr;
            }
            _erased = await ErasedRecords.FindAsync(d.Db, rows.Where(r => r.Row.EntityType is not null && r.Row.EntityId is not null)
                .Select(r => (r.Row.EntityType!, r.Row.EntityId!.Value)), ct).ConfigureAwait(false);
            if (rows.Any(r => r.Old.Concat(r.New).Any(kv => KindOf(r.Row.EntityType, kv.Key) == Kind.DateTime)))
            {
                var zone = await d.Db.Set<Company>().AsNoTracking().Where(c => c.Id == d.Ctx.CompanyId).Select(c => c.TimeZone).SingleOrDefaultAsync(ct).ConfigureAwait(false);
                if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz)) _zone = tz;
            }
        }

        /// <summary>The record type an id column points at (per record type where the same name means different things).</summary>
        private static string? Target(string? entityType, string column, Parsed row)
        {
            if (column == "PartyId" && entityType == EntryLine) return PartyEntity(row.Str("PartyType"));
            if (column == "SourceId") return null;
            if (column == "UserId" && entityType is "CORE.UserCompanyRole" or "CFG.DashboardPin" or "CORE.UserRecoveryCode") return "CORE.User";
            return Field(entityType, column) is { Kind: Kind.Ref, Target: { } t } ? t : null;
        }

        // ------------------------------------------------------------ documents named by actions

        /// <summary>The document an action names (reason holds its number, or the row its id): type, id and number.</summary>
        private static (string Type, long? Id, string? Number)? DocumentOf(Parsed p)
        {
            var reason = p.Reason;
            return p.Row.Action switch
            {
                "AddedToInvoice" => ("TRN.Invoice", p.Long("InvoiceId"), null),
                "RemovedFromInvoice" => ("TRN.Invoice", p.Long("InvoiceId", old: true), LooksLikeNumber(reason) ? reason : null),
                "Invoiced" => ("TRN.Invoice", null, reason),
                "Settled" => ("TRN.Settlement", p.Long("SettlementId"), reason),
                "SettlementVoided" => ("TRN.Settlement", p.Long("SettlementId", old: true), reason),
                "ReturnedByCreditNote" or "CreditNote" or "CreditNoteVoided" => ("TRN.CreditNote", null, reason),
                "Receipt" or "ReceiptVoid" => ("TRN.Receipt", null, reason),
                "Payment" or "PaymentVoid" => ("ACC.ApArPayment", null, reason),
                "Return" or "ReturnVoid" => ("ACC.ApArReturn", null, reason),
                "Reverse" => (JournalEntry, p.Long("ReversedByEntryId"), reason),
                "Edit" when p.Row.EntityType == JournalEntry => (JournalEntry, null, reason?.Split('→')[0].Trim()),
                _ => null,
            };
        }

        private static bool LooksLikeNumber(string? text) => text is not null && text.Length > 0 && text.Any(char.IsDigit) && !text.Contains(' ', StringComparison.Ordinal);

        private static readonly HashSet<string> DocumentActions =
        [
            "AddedToInvoice", "RemovedFromInvoice", "Invoiced", "Settled", "SettlementVoided", "ReturnedByCreditNote", "CreditNote", "CreditNoteVoided",
            "Receipt", "ReceiptVoid", "Payment", "PaymentVoid", "Return", "ReturnVoid", "Reverse", "Edit", "Accepted", "LinkApArParty", "Approve",
        ];

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

        // ------------------------------------------------------------ describe one row

        public AuditDescription Describe(Parsed p, List<Parsed> lines)
        {
            var row = p.Row;
            var type = row.EntityType;
            var entity = Entity(type);
            var action = Action(row.Action == "Approve" && type == "CORE.SupportAccessGrant" ? "Approve.Support" : row.Action);
            var user = row.UserId is long uid ? _users.GetValueOrDefault(uid) : null;
            var record = RecordName(p);
            var recordPhrase = record is null ? d.W(entity.Noun) : $"{d.W(entity.Noun)} {record}";
            var links = new List<HistoryLink>();
            if (options.LinkRecord && LinkTo(type, row.EntityId, record) is { } self) links.Add(self);

            // The document the action names, the extra detail and the reason left to show.
            string? doc = null;
            var extra = "";
            var reason = Reason(p);
            if (DocumentOf(p) is { } named)
            {
                var found = named.Id is long id && _byId.TryGetValue((named.Type, id), out var byId) ? byId
                    : named.Number is not null && _byNumber.TryGetValue((named.Type, named.Number), out var byNumber) ? byNumber : null;
                doc = DocumentPhrase(named.Type, found?.Name ?? named.Number);
                // Added to a draft that has its number now: "draft invoice INV-00001" (owner's example).
                if (row.Action == "AddedToInvoice" && found is not null && LooksLikeNumber(found.Name)) doc = L($"مسودة الفاتورة {found.Name}", $"draft invoice {found.Name}");
                // A numbered document links by its number; a draft invoice by its words ("a draft invoice").
                var linkText = found is not null && doc.Contains(found.Name, StringComparison.Ordinal) && LooksLikeNumber(found.Name) ? found.Name : doc;
                if (found is not null && LinkTo(named.Type, found, linkText) is { } link) links.Add(link);
            }
            if (DocumentActions.Contains(row.Action)) reason = null;
            switch (row.Action)
            {
                case "Post" or "Import" or "Create" when type == JournalEntry:
                    if (SourceEntity(p.Str("SourceType")) is { } sourceType && p.Long("SourceId") is long sourceId && _byId.TryGetValue((sourceType, sourceId), out var source))
                    {
                        extra = $" ({SourceTitle(p.Str("SourceType"), sourceType)} {source.Name})";
                        if (LinkTo(sourceType, source) is { } link) links.Add(link);
                    }
                    else if (row.Action == "Import" && p.Reason is { } file) extra = $" ({L("استيراد", "import")} {file})";
                    if (row.Action == "Import") reason = null;
                    break;
                case "Accepted":
                    extra = p.Reason ?? Dash;
                    break;
                case "LinkApArParty":
                    doc = p.Reason;
                    break;
                case "SetupDone" or "SetupReopened":
                    extra = p.Reason ?? "";
                    reason = null;
                    break;
            }
            var words = action.Sentence.In(En);
            var predicate = words.Replace("{r}", recordPhrase, StringComparison.Ordinal).Replace("{d}", doc ?? L("مستند", "a document"), StringComparison.Ordinal)
                .Replace("{x}", extra, StringComparison.Ordinal);
            var sentence = Sentence(user, predicate);
            var changes = Changes(p);
            var journal = lines.Select(Line).ToList();
            return new AuditDescription(row, user, d.W(action.Label), d.W(entity.Title), record, sentence, Capitalized(predicate), reason, changes, journal, links);
        }

        private string Sentence(string? user, string predicate) => AuditVocabulary.Sentence(user, predicate, En);

        private string Capitalized(string text) => En && text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

        private string DocumentPhrase(string type, string? number)
        {
            var noun = d.W(Entity(type).Noun);
            if (type == "TRN.Invoice" && (number is null || number == DraftWord(false) || number == DraftWord(true) || number == L("مسودة", "draft")))
                return L("مسودة فاتورة", "a draft invoice");
            return number is null ? noun : $"{noun} {number}";
        }

        private static string DraftWord(bool en) => en ? "Draft invoice" : "مسودة فاتورة";

        private string SourceTitle(string? sourceType, string entityType) =>
            sourceType is "SupplierBill" or "Receivable" ? Code(sourceType, En) : d.W(Entity(entityType).Title);

        // ------------------------------------------------------------ the record's name

        private string? RecordName(Parsed p)
        {
            var type = p.Row.EntityType;
            if (type is null) return null;
            // Link rows are named by whom they concern: a user's role in the company, a role's permission.
            if (type is "CORE.UserCompanyRole" or "CFG.DashboardPin" or "CORE.UserRecoveryCode"
                && (p.Long("UserId") ?? p.Long("UserId", old: true)) is long user && _byId.TryGetValue(("CORE.User", user), out var person)) return person.Name;
            if (type == "CORE.RolePermission" && (p.Long("RoleId") ?? p.Long("RoleId", old: true)) is long role && _byId.TryGetValue(("CORE.Role", role), out var r)) return r.Name;
            if (p.Row.EntityId is long id && _byId.TryGetValue((type, id), out var found)) return found.Name;
            // A record no longer readable: the name it was saved with.
            return NameFromValues(type, p.New, En) ?? NameFromValues(type, p.Old, En);
        }

        // ------------------------------------------------------------ reason

        /// <summary>The reason in the reader's words: codes of a print ("Claim · Copy") translated, a person's own text as typed.</summary>
        private string? Reason(Parsed p)
        {
            var reason = p.Reason;
            if (string.IsNullOrWhiteSpace(reason)) return null;
            var parts = reason.Split(" · ", StringSplitOptions.None).Select(part =>
            {
                var t = part.Trim();
                if (IsCode(t)) return Code(t, En);
                if (t.StartsWith("Statement ", StringComparison.Ordinal)) return L("كشف حساب ", "Statement ") + Dates(t["Statement ".Length..]);
                return t;
            });
            return string.Join(" · ", parts);
        }

        private static string Dates(string text) =>
            string.Join("…", text.Split('…').Select(x => DateOnly.TryParseExact(x.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : x));

        // ------------------------------------------------------------ changes

        private List<FieldChange> Changes(Parsed p)
        {
            var row = p.Row;
            var type = row.EntityType;
            // No old values: a record created (or a business event recorded with its values) — the history tells it in its sentence.
            var isCreate = p.Old.Count == 0;
            if (isCreate && !options.CreateValues) return [];
            var told = ToldByAction.GetValueOrDefault(row.Action) ?? [];
            var erased = type is not null && row.EntityId is long eid && _erased.Contains((type, eid));
            var maskDriver = type == "TRN.Driver" && !d.Ctx.HasPermission(PermissionKeys.DrvView);
            var result = new List<FieldChange>();
            foreach (var column in p.New.Keys.Concat(p.Old.Keys).Distinct().Where(k => !k.StartsWith('_')))
            {
                if (Hidden.Contains(column) || told.Contains(column)) continue;
                if (type == EntryLine && column is "PartyType" or "PartyModule") continue;
                var fieldWords = Field(type, column);
                if (fieldWords?.Kind == Kind.Hidden) continue;
                var oldValue = p.Old.TryGetValue(column, out var o) ? o : (JsonElement?)null;
                var newValue = p.New.TryGetValue(column, out var n) ? n : (JsonElement?)null;
                if (column == "CustomValues")
                {
                    result.AddRange(CustomChanges(type, oldValue, newValue, erased));
                    continue;
                }
                var label = Label(type, column, fieldWords);
                if ((erased && ErasedRecords.IsPersonal(type, column)) || (maskDriver && DriverPersonal.Contains(column)))
                {
                    result.Add(new FieldChange(label, ErasedRecords.Masked, ErasedRecords.Masked));
                    continue;
                }
                var before = isCreate ? Dash : Value(p, column, oldValue);
                var after = Value(p, column, newValue);
                if (before == after) continue;
                if (isCreate && after == Dash) continue;
                result.Add(new FieldChange(label, before, after));
            }
            return result;
        }

        /// <summary>Personal fields of drivers shown only with drv.view (DT-36, review M4).</summary>
        private static readonly HashSet<string> DriverPersonal = ["NationalId", "Iban", "CliqAlias", "BankName", "Phone1", "Phone2", "Address"];

        private IEnumerable<FieldChange> CustomChanges(string? type, JsonElement? oldValue, JsonElement? newValue, bool erased)
        {
            var before = Configuration.CustomFieldService.Read(oldValue is { ValueKind: JsonValueKind.String } ov ? ov.GetString() : null);
            var after = Configuration.CustomFieldService.Read(newValue is { ValueKind: JsonValueKind.String } nv ? nv.GetString() : null);
            foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
            {
                var label = type is not null && _customLabels.TryGetValue((type, key), out var l) ? l : key;
                if (erased) { yield return new FieldChange(label, ErasedRecords.Masked, ErasedRecords.Masked); continue; }
                var o = before.TryGetValue(key, out var a) ? CustomText(a) : Dash;
                var n = after.TryGetValue(key, out var b) ? CustomText(b) : Dash;
                if (o != n) yield return new FieldChange(label, o, n);
            }
        }

        private string CustomText(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => L("نعم", "Yes"),
            JsonValueKind.False => L("لا", "No"),
            JsonValueKind.Number => value.TryGetDecimal(out var n) ? n.ToString("#,##0.###", CultureInfo.InvariantCulture) : value.GetRawText(),
            JsonValueKind.String when DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            JsonValueKind.Null or JsonValueKind.Undefined => Dash,
            _ => Configuration.CustomFieldService.Text(value) is { Length: > 0 } t ? t : Dash,
        };

        private string Label(string? type, string column, FieldWords? words)
        {
            // Trips, drivers and customers: the label the company gave the field on Field configuration (FR-182).
            var key = char.ToLowerInvariant(column[0]) + column[1..];
            if (type is not null && _standardLabels.TryGetValue((type, key), out var custom)) return custom;
            if (type is not null && Configuration.FieldCatalog.IsEntity(type) && Configuration.FieldCatalog.Find(type, key) is { } std) return En ? std.LabelEn : std.LabelAr;
            return words is not null ? words.Label.In(En) : Humanize(column);
        }

        /// <summary>A value in the reader's words: names for ids, amounts with 3 decimals, dates DD/MM/YYYY, yes/no, codes in words, "—" when empty.</summary>
        private string Value(Parsed p, string column, JsonElement? value)
        {
            if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return Dash;
            if (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString())) return Dash;
            var type = p.Row.EntityType;
            if (Target(type, column, p) is { } target)
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var id) && _byId.TryGetValue((target, id), out var found)) return found.Name;
                return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var unknown) && unknown < 0 ? Dash : L("(غير معروف)", "(unknown)");
            }
            var kind = KindOf(type, column);
            if (type == "CFG.CompanySetting" && column == "SettingValue") kind = Kind.Auto;
            switch (kind)
            {
                case Kind.Money when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var m):
                    return m.ToString("#,##0.000", CultureInfo.InvariantCulture);
                case Kind.Percent when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var pct):
                    return pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                case Kind.Number when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var num):
                    // Weights and quantities with 3 decimals as on the screens; counts and days whole.
                    return num.ToString(ClrType(type, column) == typeof(decimal) ? "#,##0.000" : "#,##0.###", CultureInfo.InvariantCulture);
                case Kind.Date when v.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day):
                    return day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                case Kind.DateTime when v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at):
                    var local = _zone is null ? at : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), _zone);
                    return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                case Kind.Code when v.ValueKind == JsonValueKind.String:
                    return CodeText(type, column, v.GetString()!);
            }
            return v.ValueKind switch
            {
                JsonValueKind.True => L("نعم", "Yes"),
                JsonValueKind.False => L("لا", "No"),
                JsonValueKind.Number => v.TryGetDecimal(out var x) ? x.ToString(x == decimal.Truncate(x) ? "#,##0" : "#,##0.000", CultureInfo.InvariantCulture) : v.GetRawText(),
                JsonValueKind.String => SettingText(type, p, v.GetString()!),
                _ => v.GetRawText(),
            };
        }

        private string CodeText(string? type, string column, string code) => column switch
        {
            "PostingKey" => PostingKeys.All.FirstOrDefault(k => k.Key == code) is { } k ? (En ? k.NameEn : k.NameAr) : code,
            "EntityType" => d.W(Entity(code).Title),
            "SettingKey" when code.StartsWith("CORE.Setup.", StringComparison.Ordinal) => L("خطوة إعداد ", "Setup step ") + code["CORE.Setup.".Length..],
            "Status" when type == "TRN.Trip" && code == "Active" => L("نشطة", "Active"),
            _ => Code(code, En),
        };

        /// <summary>A company setting's value ("true", "2.5") in words; other text as typed.</summary>
        private string SettingText(string? type, Parsed p, string text)
        {
            if (type != "CFG.CompanySetting") return text;
            if (bool.TryParse(text, out var b)) return b ? L("نعم", "Yes") : L("لا", "No");
            if (p.Str("SettingKey") == "TRN.ShortageTolerancePct" && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct))
                return pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            return text;
        }

        // ------------------------------------------------------------ journal lines

        private HistoryLine Line(Parsed line)
        {
            var account = line.Long("AccountId") is long a && _byId.TryGetValue(("ACC.Account", a), out var found) ? found.Name : Dash;
            if (PartyEntity(line.Str("PartyType")) is { } partyType && line.Long("PartyId") is long party && _byId.TryGetValue((partyType, party), out var who))
                account += " — " + who.Name;
            static decimal Amount(Parsed l, string column) => l.Value(column) is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var n) ? n : 0;
            var debit = Amount(line, "Debit");
            var credit = Amount(line, "Credit");
            return new HistoryLine(account, debit == 0 ? null : debit.ToString("#,##0.000", CultureInfo.InvariantCulture),
                credit == 0 ? null : credit.ToString("#,##0.000", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------ links

        private HistoryLink? LinkTo(string? type, long? id, string? name) =>
            type is not null && id is long n && _byId.TryGetValue((type, n), out var found) ? LinkTo(type, found, name) : null;

        /// <summary>A link to a document's screen; <paramref name="text"/> is the words of the sentence that become the link.</summary>
        private HistoryLink? LinkTo(string type, Found found, string? text = null)
        {
            // A payable or a receivable opens its own screen.
            var kind = type == "ACC.ApArDocument" ? d._documentKinds.GetValueOrDefault(found.Id) : LinkKind(type);
            if (kind is null || found.PublicId is not Guid pid || !Types.TryGetValue(type, out var clr)) return null;
            return new HistoryLink(kind, d.Tokens.Encode(clr, pid), text ?? found.Name);
        }
    }

    // ================================================================= look-ups

    /// <summary>Name of a record from its values (Create rows hold them all; also the last name of a record no longer found).</summary>
    private static string? NameFromValues(string type, Dictionary<string, JsonElement> values, bool en)
    {
        string? S(string k) => values.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return Special() ?? NameProperties.Select(S).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        string? Special() => type switch
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

    private static readonly string[] NameProperties = ["Number", "ReceiptNo", "Name", "FullName", "Code", "BatchNo", "PeriodMonth", "Title", "Email", "FileName", "Description"];

    private static readonly MethodInfo LoadMethod = typeof(AuditDescriber).GetMethod(nameof(LoadAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Records by id per type — the tenant and company filters stay on, soft-deleted records are included (they keep their names).</summary>
    private async Task ResolveAsync(Dictionary<string, HashSet<long>> wanted, Dictionary<(string, long), Found> into, CancellationToken ct)
    {
        foreach (var (type, ids) in wanted)
        {
            if (!Types.TryGetValue(type, out var clr) || ids.Count == 0) continue;
            var rows = await ((Task<List<object>>)LoadMethod.MakeGenericMethod(clr).Invoke(this, [ids.ToList(), null, ct])!).ConfigureAwait(false);
            foreach (var e in rows) Add(type, e, into);
        }
    }

    /// <summary>Documents by number per type (actions name them by number).</summary>
    private async Task ResolveNumbersAsync(Dictionary<string, HashSet<string>> wanted, Dictionary<(string, string), Found> into, CancellationToken ct)
    {
        foreach (var (type, numbers) in wanted)
        {
            if (!Types.TryGetValue(type, out var clr) || clr.GetProperty("Number") is null || numbers.Count == 0) continue;
            var rows = await ((Task<List<object>>)LoadMethod.MakeGenericMethod(clr).Invoke(this, [null, numbers.ToList(), ct])!).ConfigureAwait(false);
            foreach (var e in rows)
            {
                var found = Describe(type, e);
                if (clr.GetProperty("Number")!.GetValue(e) is string number) into[(type, number)] = found;
            }
        }
    }

    private async Task<List<object>> LoadAsync<T>(List<long>? ids, List<string>? numbers, CancellationToken ct) where T : class
    {
        var q = db.Set<T>().AsNoTracking();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T))) q = q.IgnoreQueryFilters(["SoftDelete"]);
        if (typeof(T) == typeof(User)) q = db.Set<T>().AsNoTracking().IgnoreQueryFilters();   // users: names only, of ids found in this log
        q = ids is not null ? q.Where(e => ids.Contains(EF.Property<long>(e, "Id"))) : q.Where(e => numbers!.Contains(EF.Property<string>(e, "Number")));
        return (await q.Take(2000).ToListAsync(ct).ConfigureAwait(false)).Cast<object>().ToList();
    }

    private void Add(string type, object e, Dictionary<(string, long), Found> into)
    {
        var found = Describe(type, e);
        into[(type, found.Id)] = found;
    }

    private Found Describe(string type, object e)
    {
        var t = e.GetType();
        var id = (long)t.GetProperty("Id")!.GetValue(e)!;
        var pid = t.GetProperty("PublicId")?.GetValue(e) as Guid?;
        var values = t.GetProperties().Where(p => p.PropertyType == typeof(string)).ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.GetValue(e) as string));
        var name = type switch
        {
            "TRN.Invoice" => values["Number"].GetString() ?? L("مسودة", "draft"),
            "CORE.Permission" => En ? values["DescriptionEn"].GetString() : values["DescriptionAr"].GetString(),
            _ => NameFromValues(type, values, En),
        };
        if (type == "ACC.ApArDocument" && t.GetProperty("DocumentKind")?.GetValue(e) is string kind) _documentKinds[id] = kind;
        return new Found(name ?? "", pid, id);
    }

    private readonly Dictionary<long, string> _documentKinds = [];

    /// <summary>
    /// Records not found (never deleted outright, but kept out by a filter of another era or erased): the last name the audit log recorded
    /// for them, so a history still names the target (item 3).
    /// </summary>
    private async Task LastKnownNamesAsync(Dictionary<string, HashSet<long>> wanted, Dictionary<(string, long), Found> into, CancellationToken ct)
    {
        var companyId = ctx.CompanyId;
        foreach (var (type, ids) in wanted)
        {
            var missing = ids.Where(id => !into.ContainsKey((type, id))).Select(id => (long?)id).ToList();
            if (missing.Count == 0 || type == "CORE.User") continue;
            var rows = await db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == type && a.CompanyId == companyId && missing.Contains(a.EntityId) && a.NewValues != null)
                .OrderByDescending(a => a.Id).Select(a => new { a.EntityId, a.NewValues }).Take(500).ToListAsync(ct).ConfigureAwait(false);
            foreach (var r in rows)
            {
                if (into.ContainsKey((type, r.EntityId!.Value))) continue;
                if (NameFromValues(type, Parse(r.NewValues), En) is { } name) into[(type, r.EntityId.Value)] = new Found(name, null, r.EntityId.Value);
            }
        }
    }

    /// <summary>How a column's value is shown: the vocabulary's kind, else from the property's type.</summary>
    private static Kind KindOf(string? entityType, string column)
    {
        if (Field(entityType, column) is { } f && f.Kind != Kind.Auto) return f.Kind;
        var clr = ClrType(entityType, column);
        if (clr == typeof(decimal)) return Kind.Money;
        if (clr == typeof(DateOnly)) return Kind.Date;
        if (clr == typeof(DateTime)) return Kind.DateTime;
        if (clr == typeof(bool)) return Kind.Bool;
        if (clr == typeof(int) || clr == typeof(long) || clr == typeof(short) || clr == typeof(byte)) return Kind.Number;
        return Kind.Text;
    }
}
