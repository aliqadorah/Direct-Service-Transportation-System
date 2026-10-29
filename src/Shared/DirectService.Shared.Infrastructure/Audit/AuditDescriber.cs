using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DirectService.Shared.Contracts;
using DirectService.Shared.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using static DirectService.SharedKernel.AuditWords;

namespace DirectService.Shared.Infrastructure.Audit;

/// <summary>
/// The central audit describer (owner feedback 14 Oct 2026, DT-188): turns audit rows into readable events in the reader's language for
/// every History panel and the audit log (screen and exports). Labels come from the modules' vocabularies (<see cref="AuditVocabularies"/>),
/// the company's Field configuration labels and its custom fields; ids are resolved to document numbers, account code and name, names of
/// people, places and users — soft-deleted records by their name, records no longer found by the last name the log recorded. Only records
/// of the reader's tenant and company are read (the query filters stay on). Presentation only: nothing stored is changed. A shared engine:
/// the rules only a module knows are its <see cref="IAuditVocabularyProvider"/>'s hooks (Stage 2 session 9, DT-329).
/// </summary>
public sealed class AuditDescriber(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, IUserNames users, ICompanyInfo companies,
    IEnumerable<IAuditVocabularyProvider> providers, FieldCatalogs catalog) : IAuditDescriber
{
    private const string Dash = "—";

    private readonly AuditVocabularies _words = new(providers);

    private bool En => ctx.Language == "en";

    // For the run below (a nested class cannot read the primary constructor's parameters).
    private IAppDbContext Db => db;

    private IUrlTokenService Tokens => tokens;

    private IUserNames Users => users;

    private ICompanyInfo Companies => companies;

    private FieldCatalogs Catalog => catalog;

    private AuditVocabularies V => _words;

    private string W(Words w) => w.In(En);

    private string L(string ar, string en) => En ? en : ar;

    /// <summary>Record types by their stored name (pinned, design §e, E-1/E-2), from the EF model.</summary>
    private System.Collections.Frozen.FrozenDictionary<string, Type> Types => EntityTypeMap.WithLongId(db.Model);

    /// <summary>The CLR type of a property of a record type (decides how a value is shown), null when unknown.</summary>
    private Type? ClrType(string? entityType, string column)
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
        var parsed = rows.Select(r => new ParsedAuditRow(r, Parse(r.OldValues), Parse(r.NewValues))).ToList();
        var lines = await LinesAsync(parsed, ct).ConfigureAwait(false);
        await run.PrepareAsync(parsed, lines, ct).ConfigureAwait(false);
        return parsed.Select(p => run.Describe(p, lines.GetValueOrDefault(p.Row.Id) ?? [])).ToList();
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

    // ================================================================= entry lines (item 6)

    /// <summary>
    /// The lines saved with each entry created in the rows (<see cref="IAuditVocabularyProvider.Lines"/>; the interceptor writes one audit
    /// row per line in the same save, at the same time): matched by the entry's id, or — for rows written before DT-188 with a temporary
    /// key — by the save they came with.
    /// </summary>
    private async Task<Dictionary<long, List<ParsedAuditRow>>> LinesAsync(List<ParsedAuditRow> rows, CancellationToken ct)
    {
        var result = new Dictionary<long, List<ParsedAuditRow>>();
        foreach (var spec in V.Providers.Select(p => p.Lines).OfType<AuditLines>())
        {
            var entries = rows.Where(p => p.Row.EntityType == spec.EntryType && p.Row.EntityId is not null && p.Row.Action != "Update" && p.New.ContainsKey("Number")).ToList();
            if (entries.Count == 0) continue;
            var companyId = ctx.CompanyId;
            var times = entries.Select(e => e.Row.OccurredAt).Distinct().ToList();
            var lineType = spec.LineType;
            var stored = await db.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityType == lineType && a.CompanyId == companyId && times.Contains(a.OccurredAt))
                .OrderBy(a => a.Id).Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues))
                .ToListAsync(ct).ConfigureAwait(false);
            var lines = stored.Select(r => new ParsedAuditRow(r, Parse(r.OldValues), Parse(r.NewValues))).ToList();
            foreach (var e in entries)
            {
                var id = e.Row.EntityId!.Value;
                var mine = lines.Where(l => l.Row.OccurredAt == e.Row.OccurredAt && l.Whole(spec.EntryColumn) == id).ToList();
                if (mine.Count == 0 && entries.Count(x => x.Row.OccurredAt == e.Row.OccurredAt && x.Row.UserId == e.Row.UserId) == 1)
                    mine = lines.Where(l => l.Row.OccurredAt == e.Row.OccurredAt && l.Row.UserId == e.Row.UserId && l.Whole(spec.EntryColumn) is < 0).ToList();
                result[e.Row.Id] = mine.OrderBy(l => l.Whole(spec.OrderColumn) ?? 0).ToList();
            }
        }
        return result;
    }

    // ================================================================= one run

    /// <summary>A record found for an id: its name in the reader's language, its public id and screen kind (links).</summary>
    private sealed record Found(string Name, Guid? PublicId, long Id, string? LinkKind);

    private sealed class Run(AuditDescriber d, DescribeOptions options) : IAuditRun
    {
        private readonly Dictionary<(string Type, long Id), Found> _byId = [];
        private readonly Dictionary<(string Type, string Number), Found> _byNumber = [];
        private readonly Dictionary<long, string> _users = [];
        private readonly Dictionary<(string Entity, string Key), string> _standardLabels = [];
        private readonly Dictionary<(string Entity, string Key), string> _customLabels = [];
        private HashSet<(string Type, long Id)> _erased = [];
        private TimeZoneInfo? _zone;

        public bool En => d.En;

        private AuditVocabularies V => d.V;

        private string L(string ar, string en) => d.L(ar, en);

        // ------------------------------------------------------------ what the modules' rules read

        public string? Name(string type, long id) => _byId.TryGetValue((type, id), out var found) ? found.Name : null;

        public HistoryLink? Link(string type, long id, string? text = null) => _byId.TryGetValue((type, id), out var found) ? LinkTo(type, found, text) : null;

        // ------------------------------------------------------------ preparation (batched look-ups)

        public async Task PrepareAsync(List<ParsedAuditRow> rows, Dictionary<long, List<ParsedAuditRow>> lines, CancellationToken ct)
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
                foreach (var provider in V.Providers)
                    foreach (var (relatedType, relatedId) in provider.Related(p))
                        Want(relatedType, relatedId);
                if (DocumentOf(p) is { } doc)
                {
                    Want(doc.Type, doc.Id);
                    WantNumber(doc.Type, doc.Number);
                }
            }
            await d.ResolveAsync(wanted, _byId, ct).ConfigureAwait(false);
            await d.ResolveNumbersAsync(numbers, _byNumber, ct).ConfigureAwait(false);
            await d.LastKnownNamesAsync(wanted, _byId, ct).ConfigureAwait(false);

            var userIds = rows.Where(r => r.Row.UserId is not null).Select(r => r.Row.UserId!.Value).Distinct().ToList();
            if (userIds.Count > 0)
            {
                // Names of the users who acted (platform support staff have no tenant, hence no filter; only ids found in this log).
                foreach (var (id, name) in await d.Users.FullNamesAsync(userIds, ct).ConfigureAwait(false))
                    _users[id] = name;
            }

            var types = rows.Select(r => r.Row.EntityType).Where(t => t is not null).Select(t => t!).Distinct().ToList();
            if (types.Any(d.Catalog.IsEntity))
            {
                foreach (var s in await d.Db.Set<StandardFieldConfig>().AsNoTracking().Where(s => s.Label != null && types.Contains(s.EntityType))
                             .Select(s => new { s.EntityType, s.FieldKey, s.Label }).ToListAsync(ct).ConfigureAwait(false))
                    _standardLabels[(s.EntityType, s.FieldKey)] = s.Label!;
                // Deleted custom fields keep their labels in history.
                foreach (var c in await d.Db.Set<CustomFieldDefinition>().AsNoTracking().IgnoreQueryFilters(["SoftDelete"]).Where(c => types.Contains(c.EntityType))
                             .Select(c => new { c.EntityType, c.FieldKey, c.LabelAr, c.LabelEn }).ToListAsync(ct).ConfigureAwait(false))
                    _customLabels[(c.EntityType, c.FieldKey)] = En ? c.LabelEn ?? c.LabelAr : c.LabelAr;
            }
            _erased = await ErasedRecords.FindAsync(d.Db, V.PersonalFields, rows.Where(r => r.Row.EntityType is not null && r.Row.EntityId is not null)
                .Select(r => (r.Row.EntityType!, r.Row.EntityId!.Value)), ct).ConfigureAwait(false);
            if (rows.Any(r => r.Old.Concat(r.New).Any(kv => d.KindOf(r.Row.EntityType, kv.Key) == Kind.DateTime)))
            {
                var zone = await d.Companies.TimeZoneAsync(ct).ConfigureAwait(false);
                if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz)) _zone = tz;
            }
        }

        /// <summary>The record type an id column points at (per record type where the same name means different things).</summary>
        private string? Target(string? entityType, string column, ParsedAuditRow row)
        {
            foreach (var provider in V.Providers)
                if (provider.TryTarget(entityType, column, row, out var target)) return target;
            return V.Field(entityType, column) is { Kind: Kind.Ref, Target: { } t } ? t : null;
        }

        private (string Type, long? Id, string? Number)? DocumentOf(ParsedAuditRow p) => V.FirstValue(provider => provider.DocumentOf(p));

        // ------------------------------------------------------------ describe one row

        public AuditDescription Describe(ParsedAuditRow p, List<ParsedAuditRow> lines)
        {
            var row = p.Row;
            var type = row.EntityType;
            var entity = V.Entity(type);
            var action = V.Action(V.First(provider => provider.ActionKey(row.Action, type)) ?? row.Action);
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
                doc = DocumentPhrase(row.Action, named.Type, found?.Name ?? named.Number, found is not null);
                // A numbered document links by its number; a draft invoice by its words ("a draft invoice").
                var linkText = found is not null && doc.Contains(found.Name, StringComparison.Ordinal) && LooksLikeNumber(found.Name) ? found.Name : doc;
                if (found is not null && LinkTo(named.Type, found, linkText) is { } link) links.Add(link);
            }
            if (V.Any(provider => provider.TellsReason(row.Action))) reason = null;
            if (V.First(provider => provider.Detail(p, this)) is { } detail)
            {
                extra = detail.Extra;
                if (detail.Document is not null) doc = detail.Document;
                if (detail.DropReason) reason = null;
                if (detail.Link is not null) links.Add(detail.Link);
            }
            var words = action.Sentence.In(En);
            var predicate = words.Replace("{r}", recordPhrase, StringComparison.Ordinal).Replace("{d}", doc ?? L("مستند", "a document"), StringComparison.Ordinal)
                .Replace("{x}", extra, StringComparison.Ordinal);
            var sentence = Sentence(user, predicate, En);
            var changes = Changes(p);
            var journal = lines.Select(Line).ToList();
            return new AuditDescription(row, user, d.W(action.Label), d.W(entity.Title), record, sentence, Capitalized(predicate), reason, changes, journal, links);
        }

        private string Capitalized(string text) => En && text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

        private string DocumentPhrase(string action, string type, string? number, bool found)
        {
            if (V.First(provider => provider.DocumentPhrase(action, type, number, found, En)) is { } phrase) return phrase;
            var noun = d.W(V.Entity(type).Noun);
            return number is null ? noun : $"{noun} {number}";
        }

        // ------------------------------------------------------------ the record's name

        private string? RecordName(ParsedAuditRow p)
        {
            var type = p.Row.EntityType;
            if (type is null) return null;
            // Rows named by whom they concern (the modules' rules).
            if (V.FirstValue(provider => provider.NamedBy(p)) is { } by && _byId.TryGetValue(by, out var person)) return person.Name;
            if (p.Row.EntityId is long id && _byId.TryGetValue((type, id), out var found)) return found.Name;
            // A record no longer readable: the name it was saved with.
            return d.NameFromValues(type, Strings(p.New), loaded: false) ?? d.NameFromValues(type, Strings(p.Old), loaded: false);
        }

        // ------------------------------------------------------------ reason

        /// <summary>The reason in the reader's words: codes of a print ("Claim · Copy") translated, a person's own text as typed.</summary>
        private string? Reason(ParsedAuditRow p)
        {
            var reason = p.Reason;
            if (string.IsNullOrWhiteSpace(reason)) return null;
            var parts = reason.Split(" · ", StringSplitOptions.None).Select(part =>
            {
                var t = part.Trim();
                if (V.IsCode(t)) return V.Code(t, En);
                return V.First(provider => provider.ReasonPart(t, En)) ?? t;
            });
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------ changes

        private List<FieldChange> Changes(ParsedAuditRow p)
        {
            var row = p.Row;
            var type = row.EntityType;
            // No old values: a record created (or a business event recorded with its values) — the history tells it in its sentence.
            var isCreate = p.Old.Count == 0;
            if (isCreate && !options.CreateValues) return [];
            var told = V.Providers.SelectMany(provider => provider.ToldBy(row.Action)).ToHashSet(StringComparer.Ordinal);
            var erased = type is not null && row.EntityId is long eid && _erased.Contains((type, eid));
            var result = new List<FieldChange>();
            foreach (var column in p.New.Keys.Concat(p.Old.Keys).Distinct().Where(k => !k.StartsWith('_')))
            {
                if (V.IsHidden(column) || told.Contains(column)) continue;
                if (V.Any(provider => provider.HidesColumn(type, column))) continue;
                var fieldWords = V.Field(type, column);
                if (fieldWords?.Kind == Kind.Hidden) continue;
                var oldValue = p.Old.TryGetValue(column, out var o) ? o : (JsonElement?)null;
                var newValue = p.New.TryGetValue(column, out var n) ? n : (JsonElement?)null;
                if (column == "CustomValues")
                {
                    result.AddRange(CustomChanges(type, oldValue, newValue, erased));
                    continue;
                }
                var label = Label(type, column, fieldWords);
                if ((erased && ErasedRecords.IsPersonal(V.PersonalFields, type, column)) || V.Any(provider => provider.Masks(type, column)))
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

        private IEnumerable<FieldChange> CustomChanges(string? type, JsonElement? oldValue, JsonElement? newValue, bool erased)
        {
            var before = CustomFieldValues.Read(oldValue is { ValueKind: JsonValueKind.String } ov ? ov.GetString() : null);
            var after = CustomFieldValues.Read(newValue is { ValueKind: JsonValueKind.String } nv ? nv.GetString() : null);
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
            _ => CustomFieldValues.Text(value) is { Length: > 0 } t ? t : Dash,
        };

        private string Label(string? type, string column, FieldWords? words)
        {
            // Configurable records (trips, drivers, customers): the label the company gave the field on Field configuration (FR-182).
            var key = char.ToLowerInvariant(column[0]) + column[1..];
            if (type is not null && _standardLabels.TryGetValue((type, key), out var custom)) return custom;
            if (type is not null && d.Catalog.IsEntity(type) && d.Catalog.Find(type, key) is { } std) return En ? std.LabelEn : std.LabelAr;
            return words is not null ? words.Label.In(En) : Humanize(column);
        }

        /// <summary>A value in the reader's words: names for ids, amounts with 3 decimals, dates DD/MM/YYYY, yes/no, codes in words, "—" when empty.</summary>
        private string Value(ParsedAuditRow p, string column, JsonElement? value)
        {
            if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return Dash;
            if (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString())) return Dash;
            var type = p.Row.EntityType;
            if (Target(type, column, p) is { } target)
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var id) && _byId.TryGetValue((target, id), out var found)) return found.Name;
                return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var unknown) && unknown < 0 ? Dash : L("(غير معروف)", "(unknown)");
            }
            var kind = V.FirstValue(provider => provider.ValueKind(type, column)) ?? d.KindOf(type, column);
            switch (kind)
            {
                case Kind.Money when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var m):
                    return m.ToString("#,##0.000", CultureInfo.InvariantCulture);
                case Kind.Percent when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var pct):
                    return pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                case Kind.Number when v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var num):
                    // Weights and quantities with 3 decimals as on the screens; counts and days whole.
                    return num.ToString(d.ClrType(type, column) == typeof(decimal) ? "#,##0.000" : "#,##0.###", CultureInfo.InvariantCulture);
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
                JsonValueKind.String => V.First(provider => provider.TextValue(type, p, v.GetString()!, En)) ?? v.GetString()!,
                _ => v.GetRawText(),
            };
        }

        private string CodeText(string? type, string column, string code) =>
            V.First(provider => provider.CodeText(type, column, code, En))
            ?? (column == "EntityType" ? d.W(V.Entity(code).Title) : V.Code(code, En));

        // ------------------------------------------------------------ entry lines

        private HistoryLine Line(ParsedAuditRow line) =>
            V.Providers.First(provider => provider.Lines?.LineType == line.Row.EntityType).Line(line, this);

        // ------------------------------------------------------------ links

        private HistoryLink? LinkTo(string? type, long? id, string? name) =>
            type is not null && id is long n && _byId.TryGetValue((type, n), out var found) ? LinkTo(type, found, name) : null;

        /// <summary>A link to a document's screen; <paramref name="text"/> is the words of the sentence that become the link.</summary>
        private HistoryLink? LinkTo(string type, Found found, string? text = null)
        {
            var kind = found.LinkKind;
            if (kind is null || found.PublicId is not Guid pid || !d.Types.TryGetValue(type, out var clr)) return null;
            return new HistoryLink(kind, d.Tokens.Encode(clr, pid), text ?? found.Name);
        }
    }

    // ================================================================= look-ups

    /// <summary>Name of a record from its values (Create rows hold them all; also the last name of a record no longer found).</summary>
    private string? NameFromValues(string type, Func<string, string?> S, bool loaded)
    {
        foreach (var provider in V.Providers)
            if (provider.TryRecordName(type, S, loaded, En, out var name)) return name;
        return NameProperties.Select(S).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
    }

    /// <summary>The text values of a row or record by column name.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Reads the read-only values of a ParsedAuditRow as well.")]
    private static Func<string, string?> Strings(IReadOnlyDictionary<string, JsonElement> values) =>
        k => values.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly string[] NameProperties = ["Number", "ReceiptNo", "Name", "FullName", "Code", "BatchNo", "PeriodMonth", "Title", "Email", "FileName", "Description"];

    private static readonly MethodInfo LoadMethod = typeof(AuditDescriber).GetMethod(nameof(LoadAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Records by id per type — the tenant and company filters stay on, soft-deleted records are included (they keep their names).</summary>
    private async Task ResolveAsync(Dictionary<string, HashSet<long>> wanted, Dictionary<(string, long), Found> into, CancellationToken ct)
    {
        foreach (var (type, ids) in wanted)
        {
            if (!Types.TryGetValue(type, out var clr) || ids.Count == 0) continue;
            var rows = await ((Task<List<object>>)LoadMethod.MakeGenericMethod(clr).Invoke(this, [ids.ToList(), null, Unfiltered(type), ct])!).ConfigureAwait(false);
            foreach (var e in rows) Add(type, e, into);
        }
    }

    /// <summary>Documents by number per type (actions name them by number).</summary>
    private async Task ResolveNumbersAsync(Dictionary<string, HashSet<string>> wanted, Dictionary<(string, string), Found> into, CancellationToken ct)
    {
        foreach (var (type, numbers) in wanted)
        {
            if (!Types.TryGetValue(type, out var clr) || clr.GetProperty("Number") is null || numbers.Count == 0) continue;
            var rows = await ((Task<List<object>>)LoadMethod.MakeGenericMethod(clr).Invoke(this, [null, numbers.ToList(), Unfiltered(type), ct])!).ConfigureAwait(false);
            foreach (var e in rows)
            {
                var found = Describe(type, e);
                if (clr.GetProperty("Number")!.GetValue(e) is string number) into[(type, number)] = found;
            }
        }
    }

    private bool Unfiltered(string type) => V.Any(provider => provider.ReadsUnfiltered(type));

    private async Task<List<object>> LoadAsync<T>(List<long>? ids, List<string>? numbers, bool unfiltered, CancellationToken ct) where T : class
    {
        var q = db.Set<T>().AsNoTracking();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T))) q = q.IgnoreQueryFilters(["SoftDelete"]);
        if (unfiltered) q = db.Set<T>().AsNoTracking().IgnoreQueryFilters();   // users: names only, of ids found in this log
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
        var name = NameFromValues(type, Strings(values), loaded: true);
        return new Found(name ?? "", pid, id, V.First(provider => provider.LinkKind(type, e)));
    }

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
            if (missing.Count == 0 || !V.Providers.All(provider => provider.KeepsLastName(type))) continue;
            var rows = await db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == type && a.CompanyId == companyId && missing.Contains(a.EntityId) && a.NewValues != null)
                .OrderByDescending(a => a.Id).Select(a => new { a.EntityId, a.NewValues }).Take(500).ToListAsync(ct).ConfigureAwait(false);
            foreach (var r in rows)
            {
                if (into.ContainsKey((type, r.EntityId!.Value))) continue;
                if (NameFromValues(type, Strings(Parse(r.NewValues)), loaded: false) is { } name) into[(type, r.EntityId.Value)] = new Found(name, null, r.EntityId.Value, null);
            }
        }
    }

    /// <summary>How a column's value is shown: the vocabulary's kind, else from the property's type.</summary>
    private Kind KindOf(string? entityType, string column)
    {
        if (V.Field(entityType, column) is { } f && f.Kind != Kind.Auto) return f.Kind;
        var clr = ClrType(entityType, column);
        if (clr == typeof(decimal)) return Kind.Money;
        if (clr == typeof(DateOnly)) return Kind.Date;
        if (clr == typeof(DateTime)) return Kind.DateTime;
        if (clr == typeof(bool)) return Kind.Bool;
        if (clr == typeof(int) || clr == typeof(long) || clr == typeof(short) || clr == typeof(byte)) return Kind.Number;
        return Kind.Text;
    }
}
