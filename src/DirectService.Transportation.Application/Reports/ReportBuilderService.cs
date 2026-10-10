using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Reports;

/// <summary>Composite grouping key built in SQL: per slot a text, a number (dates, flags) and a public id (refs).</summary>
public sealed class GroupKey(string? s1, long? n1, Guid? g1, string? s2, long? n2, Guid? g2, string? s3, long? n3, Guid? g3)
{
    public string? S1 { get; } = s1;
    public long? N1 { get; } = n1;
    public Guid? G1 { get; } = g1;
    public string? S2 { get; } = s2;
    public long? N2 { get; } = n2;
    public Guid? G2 { get; } = g2;
    public string? S3 { get; } = s3;
    public long? N3 { get; } = n3;
    public Guid? G3 { get; } = g3;
}

/// <summary>A grouped result row read from SQL: the key slots and up to six measures.</summary>
public sealed class GroupRow(string? s1, long? n1, Guid? g1, string? s2, long? n2, Guid? g2, string? s3, long? n3, Guid? g3,
    decimal? m0, decimal? m1, decimal? m2, decimal? m3, decimal? m4, decimal? m5)
{
    public string? S1 { get; } = s1;
    public long? N1 { get; } = n1;
    public Guid? G1 { get; } = g1;
    public string? S2 { get; } = s2;
    public long? N2 { get; } = n2;
    public Guid? G2 { get; } = g2;
    public string? S3 { get; } = s3;
    public long? N3 { get; } = n3;
    public Guid? G3 { get; } = g3;
    public decimal? M0 { get; } = m0;
    public decimal? M1 { get; } = m1;
    public decimal? M2 { get; } = m2;
    public decimal? M3 { get; } = m3;
    public decimal? M4 { get; } = m4;
    public decimal? M5 { get; } = m5;

    public decimal? Measure(int i) => i switch { 0 => M0, 1 => M1, 2 => M2, 3 => M3, 4 => M4, _ => M5 };
}

/// <summary>
/// The report builder (FR-143 … FR-147): ten data sources with custom fields, each open only with its view permission; row grouping (up to
/// two) and an optional column grouping, dates by day / month / quarter / year; measures sum, count, average, min, max; filters; sort;
/// top-N; detail mode. Grouping and aggregation run in SQL (expression trees over the source query, NFR-01); the column grouping is pivoted
/// from the SQL result. Sources with a status show active documents unless the definition filters on status (DT-115).
/// </summary>
public sealed class ReportBuilderService(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, LedgerReportService ledger)
{
    public const int MaxGroups = 5000;
    public const int MaxDetailRows = 5000;
    public const int MaxMeasures = 6;
    public const int MaxFilters = 20;
    public const int MaxColumns = 30;
    public const int MaxColumnKeys = 60;

    private bool En => ctx.Language == "en";

    private string L(string ar, string en) => En ? en : ar;

    // ============================================================ sources

    public bool CanUse(SourceDef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Permissions.Length == 0 || s.Permissions.Any(ctx.HasPermission);
    }

    public async Task<IReadOnlyList<DataSourceInfo>> SourcesAsync(CancellationToken ct)
    {
        var result = new List<DataSourceInfo>();
        foreach (var s in ReportSources.All.Where(CanUse))
        {
            var fields = await FieldsAsync(s, ct).ConfigureAwait(false);
            result.Add(new DataSourceInfo(s.Key, En ? s.En : s.Ar, fields.Select(f => new SourceField(f.Key, En ? f.En : f.Ar, f.Type, f.Groupable, f.Measurable, f.Custom)).ToList()));
        }
        return result;
    }

    /// <summary>The source's fields plus its custom fields (FR-143 "custom fields included automatically"), keys "cf.&lt;key&gt;".</summary>
    internal async Task<IReadOnlyList<FieldDef>> FieldsAsync(SourceDef s, CancellationToken ct)
    {
        if (s.CustomEntity is null || s.CustomJson is null) return s.Fields;
        var custom = await db.Set<CustomFieldDefinition>().AsNoTracking().Where(c => c.EntityType == s.CustomEntity).OrderBy(c => c.SortOrder)
            .Select(c => new { c.FieldKey, c.LabelAr, c.LabelEn, c.FieldType }).ToListAsync(ct).ConfigureAwait(false);
        var list = s.Fields.ToList();
        foreach (var c in custom.Where(c => c.FieldKey.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')))
        {
            var path = "$.\"" + c.FieldKey + "\"";
            var p = s.CustomJson.Parameters[0];
            var (method, type) = c.FieldType switch
            {
                "Number" => (nameof(SqlJson.Number), FieldTypes.Number),
                "Date" => (nameof(SqlJson.Date), FieldTypes.Date),
                _ => (nameof(SqlJson.Value), FieldTypes.Text),
            };
            var call = Expression.Call(typeof(SqlJson).GetMethod(method)!, s.CustomJson.Body, Expression.Constant(path));
            list.Add(new FieldDef("cf." + c.FieldKey, c.LabelAr, c.LabelEn ?? c.LabelAr, type, Expression.Lambda(call, p),
                Groupable: type == FieldTypes.Text, Custom: true, Nullable: true));
        }
        return list;
    }

    public SourceDef Source(string? key)
    {
        var s = ReportSources.Find(key?.Trim().ToLowerInvariant()) ?? throw Fail.Field("source", ErrorCodes.ValidationFormat, "source");
        if (!CanUse(s)) throw Fail.Forbidden();
        return s;
    }

    // ============================================================ run

    private sealed record Plan(SourceDef Source, IReadOnlyList<FieldDef> Fields, IReadOnlyList<(FieldDef Field, string? Unit)> Rows, (FieldDef Field, string? Unit)? Column,
        List<(string Fn, FieldDef? Field)> Measures, LambdaExpression Where, ReportDefinition Definition, DateOnly? From, DateOnly? To);

    /// <summary>Validates a definition against the source (FR-144) and builds the filter predicate.</summary>
    private async Task<Plan> PlanAsync(SourceDef s, ReportDefinition d, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (d.Rows?.Any(r => r is null) == true) throw Fail.Field("definition.rows", ErrorCodes.ValidationFormat, "definition.rows");
        if (d.Measures?.Any(m => m is null) == true) throw Fail.Field("definition.measures", ErrorCodes.ValidationFormat, "definition.measures");
        if (d.Filters?.Any(f => f is null) == true) throw Fail.Field("definition.filters", ErrorCodes.ValidationFormat, "definition.filters");
        if (d.Columns?.Any(c => c is null) == true) throw Fail.Field("definition.columns", ErrorCodes.ValidationFormat, "definition.columns");
        var fields = await FieldsAsync(s, ct).ConfigureAwait(false);
        FieldDef Field(string? key, string at) => fields.FirstOrDefault(f => f.Key == key) ?? throw Fail.Field(at, ErrorCodes.ValidationFormat, at);
        (FieldDef, string?) Grouping(ReportGrouping g, string at)
        {
            var f = Field(g.Field, at + ".field");
            if (!f.Groupable) throw Fail.Field(at + ".field", ErrorCodes.ValidationFormat, at + ".field");
            string? unit = null;
            if (f.Type == FieldTypes.Date)
            {
                unit = (g.DateUnit?.Trim().ToLowerInvariant()) switch
                {
                    null or "" or "month" => "month",
                    "day" => "day",
                    "quarter" => "quarter",
                    "year" => "year",
                    _ => throw Fail.Field(at + ".dateUnit", ErrorCodes.ValidationFormat, at + ".dateUnit"),
                };
            }
            return (f, unit);
        }

        var rows = (d.Rows ?? []).Select((g, i) => Grouping(g, $"definition.rows[{i}]")).ToList();
        var column = d.Column is null ? ((FieldDef, string?)?)null : Grouping(d.Column, "definition.column");
        var measures = new List<(string, FieldDef?)>();
        if (!d.Detail)
        {
            if (rows.Count is < 1 or > 2) throw Fail.Field("definition.rows", ErrorCodes.ValidationRange, "definition.rows");
            if ((d.Measures?.Count ?? 0) is < 1 or > MaxMeasures) throw Fail.Field("definition.measures", ErrorCodes.ValidationRange, "definition.measures");
            for (var i = 0; i < d.Measures!.Count; i++)
            {
                var m = d.Measures[i];
                var fn = (m.Function?.Trim().ToLowerInvariant()) switch
                {
                    "sum" or "count" or "avg" or "min" or "max" => m.Function!.Trim().ToLowerInvariant(),
                    _ => throw Fail.Field($"definition.measures[{i}].function", ErrorCodes.ValidationFormat, $"definition.measures[{i}].function"),
                };
                FieldDef? f = null;
                if (fn != "count" || !string.IsNullOrWhiteSpace(m.Field))
                {
                    f = Field(m.Field, $"definition.measures[{i}].field");
                    if (fn != "count" && !f.Measurable) throw Fail.Field($"definition.measures[{i}].field", ErrorCodes.ValidationFormat, $"definition.measures[{i}].field");
                }
                measures.Add((fn, f));
            }
        }
        else
        {
            if ((d.Columns?.Count ?? 0) is < 1 or > MaxColumns) throw Fail.Field("definition.columns", ErrorCodes.ValidationRange, "definition.columns");
            foreach (var (c, i) in d.Columns!.Select((c, i) => (c, i))) Field(c, $"definition.columns[{i}]");
        }
        if (d.Top is < 1 or > MaxGroups) throw Fail.Field("definition.top", ErrorCodes.ValidationRange, "definition.top");
        if ((d.Filters?.Count ?? 0) > MaxFilters) throw Fail.Field("definition.filters", ErrorCodes.ValidationRange, "definition.filters");

        var x = Expression.Parameter(s.Row, "x");
        Expression? where = null;
        void And(Expression e) => where = where is null ? e : Expression.AndAlso(where, e);
        // A period given at run time replaces the definition's own filters on the main date (review B10-3).
        var overriding = (from is not null || to is not null) && s.DateField is not null;
        var filters = (d.Filters ?? []).Where(f => !(overriding && f.Field == s.DateField)).ToList();
        if (s.DefaultFilter is not null && !filters.Any(f => f.Field == "status")) And(Replace(s.DefaultFilter, x));
        for (var i = 0; i < filters.Count; i++) And(Filter(Field(filters[i].Field, $"definition.filters[{i}].field"), filters[i], x, $"definition.filters[{i}]"));
        if ((from is not null || to is not null) && s.DateField is not null)
        {
            var dateField = fields.First(f => f.Key == s.DateField);
            var body = Expression.Convert(Replace(dateField.Value, x), typeof(DateOnly?));
            if (from is DateOnly f0) And(Expression.GreaterThanOrEqual(body, Expression.Constant(f0, typeof(DateOnly?))));
            if (to is DateOnly t0) And(Expression.LessThanOrEqual(body, Expression.Constant(t0, typeof(DateOnly?))));
        }
        var predicate = Expression.Lambda(where ?? Expression.Constant(true), x);
        return new Plan(s, fields, rows, column, measures, predicate, d with { Filters = filters }, from, to);
    }

    /// <summary>Runs a definition (builder preview or saved report): grouped with measures, pivoted by the column grouping, or detail rows.</summary>
    public async Task<ReportResult> RunAsync(string? sourceKey, ReportDefinition? definition, DateOnly? from, DateOnly? to, string? title, CancellationToken ct)
    {
        var s = Source(sourceKey);
        var d = definition ?? throw Fail.Field("definition", ErrorCodes.ValidationRequired, "definition");
        if (from is not null || to is not null) (from, to) = await ledger.PeriodAsync(from, to, ct).ConfigureAwait(false);
        var plan = await PlanAsync(s, d, from, to, ct).ConfigureAwait(false);
        var method = (d.Detail ? DetailMethod : GroupedMethod).MakeGenericMethod(s.Row);
        var result = await ((Task<ReportResult>)method.Invoke(this, [plan, ct])!).ConfigureAwait(false);
        var filters = new List<ReportFilterValue> { new(L("المصدر", "Source"), En ? s.En : s.Ar) };
        if (from is not null) filters.Add(new(L("الفترة", "Period"), $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}"));
        filters.AddRange((d.Filters ?? []).Select(f => new ReportFilterValue(Label(plan.Fields.First(x => x.Key == f.Field)), f.Operator + " " + string.Join(", ", f.Values ?? []))));
        return result with { Title = title ?? (En ? s.En : s.Ar), Filters = filters };
    }

    /// <summary>Checks a definition against its source without running it (saved reports).</summary>
    public async Task ValidateAsync(string sourceKey, ReportDefinition d, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(d);
        await PlanAsync(Source(sourceKey), d, null, null, ct).ConfigureAwait(false);
    }

    /// <summary>The grand total of one measure over everything the definition selects (dashboard pins, FR-140).</summary>
    public async Task<decimal?> TotalAsync(string sourceKey, ReportDefinition d, int measure, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(d);
        var s = Source(sourceKey);
        if (d.Detail || d.Measures is null || measure < 0 || measure >= d.Measures.Count) throw Fail.Field("measure", ErrorCodes.ValidationRange, "measure");
        var plan = await PlanAsync(s, d, null, null, ct).ConfigureAwait(false);
        var method = TotalMethod.MakeGenericMethod(s.Row);
        var totals = await ((Task<IReadOnlyList<decimal?>>)method.Invoke(this, [plan, ct])!).ConfigureAwait(false);
        return totals[measure];
    }

    private static readonly MethodInfo GroupedMethod = typeof(ReportBuilderService).GetMethod(nameof(GroupedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo DetailMethod = typeof(ReportBuilderService).GetMethod(nameof(DetailAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo TotalMethod = typeof(ReportBuilderService).GetMethod(nameof(TotalsAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private IQueryable<T> Query<T>(Plan p) where T : class =>
        ((IQueryable<T>)p.Source.Query(db)).AsNoTracking().Where((Expression<Func<T, bool>>)p.Where);

    // ------------------------------------------------------------ grouped

    private async Task<ReportResult> GroupedAsync<T>(Plan p, CancellationToken ct) where T : class
    {
        var slots = p.Rows.Select(r => ((FieldDef, string?)?)r).ToList();
        if (p.Column is not null) slots.Add(p.Column);
        var limit = p.Definition.Top ?? MaxGroups;
        var rows = await GroupAsync<T>(p, slots, p.Column is null, ct).ConfigureAwait(false);
        var truncated = p.Column is null ? p.Definition.Top is null && rows.Count > MaxGroups : rows.Count > MaxGroups * 20;
        var totalLabel = p.Definition.Top is null ? L("الإجمالي", "Total") : L("الإجمالي (كل الصفوف)", "Total (all rows)");
        var totals = await TotalsAsync<T>(p, ct).ConfigureAwait(false);
        var columns = p.Rows.Select(r => new ResultColumn(r.Field.Key, Label(r.Field), "text")).ToList();
        var measureCols = p.Measures.Select((m, i) => (Header: MeasureLabel(m.Fn, m.Field), Kind: MeasureKind(m.Fn, m.Field), i)).ToList();

        if (p.Column is null)
        {
            var ordered = Sort(rows, p, slots.Count).Take(limit).ToList();
            columns.AddRange(measureCols.Select(m => new ResultColumn("m" + m.i, m.Header, m.Kind)));
            var result = ordered.Select(r => new ResultRow(
                [.. p.Rows.Select((g, i) => (object?)KeyLabel(g.Field, g.Unit, r, i)), .. p.Measures.Select((_, i) => (object?)r.Measure(i))], Link(p, r))).ToList();
            return new ReportResult(p.Source.Key, string.Empty, [], columns, result,
                [totalLabel, .. Enumerable.Repeat<object?>(null, p.Rows.Count - 1), .. totals.Take(p.Measures.Count).Cast<object?>()], null, truncated);
        }

        // Pivot: one value column per (column key × measure), plus the row totals from a second grouping without the column key.
        var colIndex = p.Rows.Count;
        var colKeys = rows.Select(r => (Key: KeyOf(r, colIndex), Label: KeyLabel(p.Column.Value.Field, p.Column.Value.Unit, r, colIndex)))
            .DistinctBy(k => k.Key).OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (colKeys.Count > MaxColumnKeys)
        {
            colKeys = colKeys.Take(MaxColumnKeys).ToList();
            truncated = true;
        }
        var rowTotals = await GroupAsync<T>(p, p.Rows.Select(r => ((FieldDef, string?)?)r).ToList(), true, ct).ConfigureAwait(false);
        if (p.Definition.Top is null && rowTotals.Count > MaxGroups) truncated = true;
        var orderedRows = Sort(rowTotals, p, p.Rows.Count).Take(limit).ToList();
        foreach (var k in colKeys)
            columns.AddRange(measureCols.Select(m => new ResultColumn("m" + m.i + ":" + k.Key, k.Label + " · " + m.Header, m.Kind)));
        columns.AddRange(measureCols.Select(m => new ResultColumn("m" + m.i, L("الإجمالي", "Total") + " · " + m.Header, m.Kind)));
        var cells = rows.ToLookup(r => RowKey(r, p.Rows.Count));
        var pivoted = orderedRows.Select(r =>
        {
            var mine = cells[RowKey(r, p.Rows.Count)].ToDictionary(c => KeyOf(c, colIndex));
            var values = new List<object?>(p.Rows.Select((g, i) => (object?)KeyLabel(g.Field, g.Unit, r, i)));
            foreach (var k in colKeys)
                values.AddRange(p.Measures.Select((_, i) => (object?)(mine.TryGetValue(k.Key, out var c) ? c.Measure(i) : null)));
            values.AddRange(p.Measures.Select((_, i) => (object?)r.Measure(i)));
            return new ResultRow(values, Link(p, r));
        }).ToList();
        var totalRow = new List<object?> { totalLabel };
        totalRow.AddRange(Enumerable.Repeat<object?>(null, p.Rows.Count - 1 + colKeys.Count * p.Measures.Count));
        totalRow.AddRange(totals.Take(p.Measures.Count).Cast<object?>());
        return new ReportResult(p.Source.Key, string.Empty, [], columns, pivoted, totalRow, null, truncated);
    }

    private static string RowKey(GroupRow r, int count) => string.Join("|", Enumerable.Range(0, count).Select(i => KeyOf(r, i)));

    private static string KeyOf(GroupRow r, int i) => i switch
    {
        0 => $"{r.S1}\u001f{r.N1}\u001f{r.G1}",
        1 => $"{r.S2}\u001f{r.N2}\u001f{r.G2}",
        _ => $"{r.S3}\u001f{r.N3}\u001f{r.G3}",
    };

    private static IEnumerable<GroupRow> Sort(List<GroupRow> rows, Plan p, int slots)
    {
        var sort = p.Definition.Sort;
        if (sort?.By is { } by && by.StartsWith("measure:", StringComparison.Ordinal) && int.TryParse(by[8..], NumberStyles.None, CultureInfo.InvariantCulture, out var mi)
            && mi >= 0 && mi < p.Measures.Count)
            return sort.Descending ? rows.OrderByDescending(r => r.Measure(mi)) : rows.OrderBy(r => r.Measure(mi));
        if (sort is null && p.Definition.Top is not null) return rows.OrderByDescending(r => r.Measure(0));
        IOrderedEnumerable<GroupRow>? o = null;
        for (var i = 0; i < Math.Min(slots, p.Rows.Count); i++)
        {
            var idx = i;
            Func<GroupRow, string> key = r => SortKey(p.Rows[idx], r, idx);
            o = o is null
                ? sort?.Descending == true ? rows.OrderByDescending(key, StringComparer.CurrentCulture) : rows.OrderBy(key, StringComparer.CurrentCulture)
                : sort?.Descending == true ? o.ThenByDescending(key, StringComparer.CurrentCulture) : o.ThenBy(key, StringComparer.CurrentCulture);
        }
        return o ?? (IEnumerable<GroupRow>)rows;
    }

    private static string SortKey((FieldDef Field, string? Unit) g, GroupRow r, int i)
    {
        var (s, n, _) = Slot(r, i);
        return n is long v ? v.ToString("D12", CultureInfo.InvariantCulture) : s ?? string.Empty;
    }

    private static (string? S, long? N, Guid? G) Slot(GroupRow r, int i) => i switch
    {
        0 => (r.S1, r.N1, r.G1),
        1 => (r.S2, r.N2, r.G2),
        _ => (r.S3, r.N3, r.G3),
    };

    /// <summary>The shown value of a group key: dates by unit, flags yes/no, refs and texts by name.</summary>
    private string? KeyLabel(FieldDef f, string? unit, GroupRow r, int i)
    {
        var (s, n, _) = Slot(r, i);
        if (f.Type == FieldTypes.Date && n is long v)
        {
            return unit switch
            {
                "day" => $"{v / 10000:D4}-{v / 100 % 100:D2}-{v % 100:D2}",
                "quarter" => $"{v / 10:D4}-Q{v % 10}",
                "year" => $"{v:D4}",
                _ => $"{v / 100:D4}-{v % 100:D2}",
            };
        }
        if (f.Type == FieldTypes.Bool) return n == 1 ? L("نعم", "Yes") : L("لا", "No");
        return s ?? L("(فارغ)", "(empty)");
    }

    /// <summary>
    /// The detail behind a row (FR-149, review B10-4): the source with the report's own filters ("filters", JSON), the run period and the row's
    /// group values — refs by token, flags true/false, date groups as a from/to range of the unit.
    /// </summary>
    private DrillLink Link(Plan p, GroupRow r)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (p.Definition.Filters is { Count: > 0 } own) filters["filters"] = System.Text.Json.JsonSerializer.Serialize(own, ReportJson.Options);
        if (p.From is DateOnly from) filters["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (p.To is DateOnly to) filters["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (var i = 0; i < p.Rows.Count; i++)
        {
            var (f, unit) = p.Rows[i];
            var (s, n, g) = Slot(r, i);
            if (f.Type == FieldTypes.Ref && g is Guid pid && f.Entity is not null) filters[f.Key] = tokens.Encode(f.Entity, pid);
            else if (f.Type == FieldTypes.Bool && n is long b) filters[f.Key] = b == 1 ? "true" : "false";
            else if (f.Type == FieldTypes.Date && n is long v)
            {
                var (start, end) = Range(unit, v);
                filters[f.Key + ".from"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                filters[f.Key + ".to"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else if (s is not null) filters[f.Key] = s;
        }
        return new DrillLink("source:" + p.Source.Key, null, filters);
    }

    /// <summary>First and last day of a date group value.</summary>
    private static (DateOnly From, DateOnly To) Range(string? unit, long v)
    {
        switch (unit)
        {
            case "day":
                var d = new DateOnly((int)(v / 10000), (int)(v / 100 % 100), (int)(v % 100));
                return (d, d);
            case "quarter":
                var q = new DateOnly((int)(v / 10), (int)((v % 10 - 1) * 3 + 1), 1);
                return (q, q.AddMonths(3).AddDays(-1));
            case "year":
                return (new DateOnly((int)v, 1, 1), new DateOnly((int)v, 12, 31));
            default:
                var m = new DateOnly((int)(v / 100), (int)(v % 100), 1);
                return (m, m.AddMonths(1).AddDays(-1));
        }
    }

    private string Label(FieldDef f) => En ? f.En : f.Ar;

    private string MeasureLabel(string fn, FieldDef? f) => fn switch
    {
        "count" => L("العدد", "Count"),
        "sum" => L("مجموع ", "Sum of ") + Label(f!),
        "avg" => L("متوسط ", "Average of ") + Label(f!),
        "min" => L("أدنى ", "Minimum of ") + Label(f!),
        _ => L("أعلى ", "Maximum of ") + Label(f!),
    };

    private static string MeasureKind(string fn, FieldDef? f) => fn == "count" ? "number" : f!.Type;

    /// <summary>SQL grouping by the given slots with the plan's measures.</summary>
    private async Task<List<GroupRow>> GroupAsync<T>(Plan p, List<(FieldDef Field, string? Unit)?> slots, bool final, CancellationToken ct) where T : class
    {
        var x = Expression.Parameter(typeof(T), "x");
        var keyArgs = new List<Expression>();
        for (var i = 0; i < 3; i++)
        {
            if (i < slots.Count && slots[i] is { } slot) keyArgs.AddRange(KeySlot(slot.Field, slot.Unit, x));
            else keyArgs.AddRange([Expression.Constant(null, typeof(string)), Expression.Constant(null, typeof(long?)), Expression.Constant(null, typeof(Guid?))]);
        }
        var keyCtor = typeof(GroupKey).GetConstructors()[0];
        var keyMembers = typeof(GroupKey).GetProperties().Select(pi => (MemberInfo)pi).ToArray();
        var keySelector = Expression.Lambda<Func<T, GroupKey>>(Expression.New(keyCtor, keyArgs, OrderByCtor(keyCtor, keyMembers)), x);
        var grouped = Query<T>(p).GroupBy(keySelector);

        var g = Expression.Parameter(typeof(IGrouping<GroupKey, T>), "g");
        var key = Expression.Property(g, nameof(IGrouping<GroupKey, T>.Key));
        var args = typeof(GroupKey).GetProperties().Select(pi => (Expression)Expression.Property(key, pi)).ToList();
        args.AddRange(MeasureExpressions<T>(p, g));
        var rowCtor = typeof(GroupRow).GetConstructors()[0];
        var rowMembers = typeof(GroupRow).GetProperties().Select(pi => (MemberInfo)pi).ToArray();
        var select = Expression.Lambda<Func<IGrouping<GroupKey, T>, GroupRow>>(Expression.New(rowCtor, args, OrderByCtor(rowCtor, rowMembers)), g);
        var query = grouped.Select(select);
        if (!final) return await query.Take(MaxGroups * 20 + 1).ToListAsync(ct).ConfigureAwait(false);
        // Final rows: ordering by a measure and top-N in SQL (review B10-5), otherwise at most MaxGroups + 1 rows (sorted by key in memory).
        var by = p.Definition.Sort?.By;
        var measure = by is not null && by.StartsWith("measure:", StringComparison.Ordinal) && int.TryParse(by[8..], NumberStyles.None, CultureInfo.InvariantCulture, out var mi)
            && mi >= 0 && mi < p.Measures.Count ? mi : p.Definition.Sort is null && p.Definition.Top is not null ? 0 : -1;
        if (measure >= 0)
        {
            var r = Expression.Parameter(typeof(GroupRow), "r");
            var keySel = Expression.Lambda<Func<GroupRow, decimal?>>(Expression.Property(r, "M" + measure.ToString(CultureInfo.InvariantCulture)), r);
            query = p.Definition.Sort?.Descending == false ? query.OrderBy(keySel) : query.OrderByDescending(keySel);
            return await query.Take(p.Definition.Top ?? MaxGroups + 1).ToListAsync(ct).ConfigureAwait(false);
        }
        return await query.Take(MaxGroups + 1).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The members in the constructor's parameter order (EF maps a NewExpression with members like an anonymous type).</summary>
    private static MemberInfo[] OrderByCtor(ConstructorInfo ctor, MemberInfo[] members) =>
        ctor.GetParameters().Select(pa => members.Single(m => string.Equals(m.Name, pa.Name, StringComparison.OrdinalIgnoreCase))).ToArray();

    /// <summary>Six measure expressions over the group (unused ones null).</summary>
    private IEnumerable<Expression> MeasureExpressions<T>(Plan p, ParameterExpression g)
    {
        for (var i = 0; i < MaxMeasures; i++)
        {
            if (i >= p.Measures.Count)
            {
                yield return Expression.Constant(null, typeof(decimal?));
                continue;
            }
            var (fn, f) = p.Measures[i];
            if (fn == "count")
            {
                // count(field) counts the records where the field has a value; count() counts the records.
                Expression count = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(T)], g);
                if (f is not null && (f.Nullable || !f.Value.Body.Type.IsValueType))
                {
                    var c = Expression.Parameter(typeof(T), "c");
                    var body = Replace(f.Value, c);
                    var hasValue = Expression.Lambda(Expression.NotEqual(body, Expression.Constant(null, body.Type)), c);
                    count = Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(T)], g, hasValue);
                }
                yield return Expression.Convert(count, typeof(decimal?));
                continue;
            }
            var x = Expression.Parameter(typeof(T), "m");
            var selector = Expression.Lambda(Expression.Convert(Replace(f!.Value, x), typeof(decimal?)), x);
            yield return fn switch
            {
                "sum" => Expression.Call(typeof(Enumerable), nameof(Enumerable.Sum), [typeof(T)], g, selector),
                "avg" => Expression.Call(typeof(Enumerable), nameof(Enumerable.Average), [typeof(T)], g, selector),
                "min" => Expression.Call(typeof(Enumerable), nameof(Enumerable.Min), [typeof(T), typeof(decimal?)], g, selector),
                _ => Expression.Call(typeof(Enumerable), nameof(Enumerable.Max), [typeof(T), typeof(decimal?)], g, selector),
            };
        }
    }

    /// <summary>Grand totals of the measures over all selected rows (one SQL aggregate).</summary>
    private async Task<IReadOnlyList<decimal?>> TotalsAsync<T>(Plan p, CancellationToken ct) where T : class
    {
        if (p.Measures.Count == 0) return [];
        var x = Expression.Parameter(typeof(T), "x");
        var keySelector = Expression.Lambda<Func<T, int>>(Expression.Constant(1), x);
        var g = Expression.Parameter(typeof(IGrouping<int, T>), "g");
        var empty = Expression.Constant(null, typeof(string));
        var args = new List<Expression>();
        for (var i = 0; i < 3; i++) args.AddRange([empty, Expression.Constant(null, typeof(long?)), Expression.Constant(null, typeof(Guid?))]);
        args.AddRange(MeasureExpressions<T>(p, g));
        var rowCtor = typeof(GroupRow).GetConstructors()[0];
        var rowMembers = typeof(GroupRow).GetProperties().Select(pi => (MemberInfo)pi).ToArray();
        var select = Expression.Lambda<Func<IGrouping<int, T>, GroupRow>>(Expression.New(rowCtor, args, OrderByCtor(rowCtor, rowMembers)), g);
        var row = await Query<T>(p).GroupBy(keySelector).Select(select).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Enumerable.Range(0, p.Measures.Count).Select(i => row is null ? (p.Measures[i].Fn == "count" ? 0m : null) : row.Measure(i)).ToList();
    }

    /// <summary>Text, number and id slot of a grouping: refs by name + id, dates as numbers by unit, flags 1/0, texts as text.</summary>
    private static IEnumerable<Expression> KeySlot(FieldDef f, string? unit, ParameterExpression x)
    {
        var body = Replace(f.Value, x);
        var noText = Expression.Constant(null, typeof(string));
        var noNumber = Expression.Constant(null, typeof(long?));
        var noId = Expression.Constant(null, typeof(Guid?));
        switch (f.Type)
        {
            case FieldTypes.Ref:
                return [body, noNumber, Expression.Convert(Replace(f.Pid!, x), typeof(Guid?))];
            case FieldTypes.Bool:
                return [noText, Expression.Condition(body, Expression.Constant(1L, typeof(long?)), Expression.Constant(0L, typeof(long?))), noId];
            case FieldTypes.Date:
                Expression Part(string name) => Expression.Convert(Expression.Property(body, name), typeof(long));
                Expression n = unit switch
                {
                    "day" => Expression.Add(Expression.Add(Expression.Multiply(Part("Year"), Expression.Constant(10000L)), Expression.Multiply(Part("Month"), Expression.Constant(100L))), Part("Day")),
                    "quarter" => Expression.Add(Expression.Multiply(Part("Year"), Expression.Constant(10L)),
                        Expression.Divide(Expression.Add(Part("Month"), Expression.Constant(2L)), Expression.Constant(3L))),
                    "year" => Part("Year"),
                    _ => Expression.Add(Expression.Multiply(Part("Year"), Expression.Constant(100L)), Part("Month")),
                };
                return [noText, Expression.Convert(n, typeof(long?)), noId];
            default:
                return [body.Type == typeof(string) ? body : Expression.Call(body, typeof(object).GetMethod(nameof(ToString))!), noNumber, noId];
        }
    }

    // ------------------------------------------------------------ detail

    private async Task<ReportResult> DetailAsync<T>(Plan p, CancellationToken ct) where T : class
    {
        var columns = p.Definition.Columns!.Select(c => p.Fields.First(f => f.Key == c)).ToList();
        var x = Expression.Parameter(typeof(T), "x");
        var values = columns.Select(c => (Expression)Expression.Convert(Replace(c.Value, x), typeof(object))).ToList();
        values.Add(Expression.Convert(Replace(p.Source.RowPid, x), typeof(object)));
        var projection = Expression.Lambda<Func<T, object?[]>>(Expression.NewArrayInit(typeof(object), values), x);
        IQueryable<T> q = Query<T>(p);
        var sortField = columns.FirstOrDefault(c => c.Key == p.Definition.Sort?.By) ?? p.Fields.FirstOrDefault(f => f.Key == p.Source.DateField) ?? columns[0];
        var sortBody = Replace(sortField.Value, x);
        var sortLambda = Expression.Lambda(sortBody, x);
        var descending = p.Definition.Sort?.Descending ?? (sortField.Type == FieldTypes.Date);
        var orderBy = Expression.Call(typeof(Queryable), descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy), [typeof(T), sortBody.Type], q.Expression,
            Expression.Quote(sortLambda));
        q = q.Provider.CreateQuery<T>(orderBy);
        var take = Math.Min(p.Definition.Top ?? MaxDetailRows, MaxDetailRows);
        var rows = await q.Select(projection).Take(take + 1).ToListAsync(ct).ConfigureAwait(false);
        var resultRows = rows.Take(take).Select(r => new ResultRow(r.Take(columns.Count).Select(Cell).ToList(),
            new DrillLink(p.Source.RowLink, r[^1] is Guid g ? tokens.Encode(Entity(p.Source), g) : null, null))).ToList();
        // Totals of the numeric columns over everything selected.
        var numeric = columns.Where(c => c.Measurable).ToList();
        var totalPlan = p with { Measures = numeric.Select(c => ("sum", (FieldDef?)c)).Take(MaxMeasures).ToList() };
        var sums = await TotalsAsync<T>(totalPlan, ct).ConfigureAwait(false);
        var totals = columns.Select(c => numeric.IndexOf(c) is var i and >= 0 and < MaxMeasures ? (object?)sums[i] : null).ToList();
        if (totals.Count > 0 && totals[0] is null) totals[0] = L("الإجمالي", "Total");
        return new ReportResult(p.Source.Key, string.Empty, [], columns.Select(c => new ResultColumn(c.Key, Label(c), c.Type == FieldTypes.Ref ? "text" : c.Type)).ToList(),
            resultRows, totals, null, rows.Count > take);
    }

    private object? Cell(object? v) => v switch
    {
        bool b => b ? L("نعم", "Yes") : L("لا", "No"),
        _ => v,
    };

    /// <summary>The entity whose token links a detail row (the row's own type, or the journal entry of a journal line).</summary>
    private static Type Entity(SourceDef s) => s.RowLink == "journal-entry" ? typeof(Domain.Acc.JournalEntry) : s.Row;

    // ------------------------------------------------------------ filters

    private Expression Filter(FieldDef f, ReportFilter r, ParameterExpression x, string at)
    {
        var op = r.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
        var values = r.Values ?? [];
        string V(int i) => i < values.Count && values[i] is { } v ? v : throw Fail.Field(at + ".values", ErrorCodes.ValidationRequired, at + ".values");
        var body = Replace(f.Value, x);
        Exception Bad() => Fail.Field(at + ".operator", ErrorCodes.ValidationFormat, at + ".operator");

        if (op is "empty" or "notempty")
        {
            Expression isEmpty = body.Type == typeof(string)
                ? Expression.Call(typeof(string).GetMethod(nameof(string.IsNullOrEmpty))!, body)
                : body.Type.IsValueType && Nullable.GetUnderlyingType(body.Type) is null ? Expression.Constant(false) : Expression.Equal(body, Expression.Constant(null, body.Type));
            return op == "empty" ? isEmpty : Expression.Not(isEmpty);
        }
        switch (f.Type)
        {
            case FieldTypes.Ref:
            {
                var pid = Expression.Convert(Replace(f.Pid!, x), typeof(Guid?));
                if (op == "contains") return Contains(body, V(0));
                var ids = values.Select(v => tokens.Decode(f.Entity!, v) ?? throw Fail.Field(at + ".values", ErrorCodes.ValidationFormat, at + ".values")).Select(g => (Guid?)g).ToList();
                if (ids.Count == 0) throw Fail.Field(at + ".values", ErrorCodes.ValidationRequired, at + ".values");
                Expression inList = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid?)], Expression.Constant(ids), pid);
                return op switch { "eq" or "in" => inList, "ne" => Expression.Not(inList), _ => throw Bad() };
            }
            case FieldTypes.Bool:
            {
                var b = V(0).Trim().ToLowerInvariant() switch { "true" or "1" or "yes" => true, "false" or "0" or "no" => false, _ => throw Fail.Field(at + ".values", ErrorCodes.ValidationFormat, at + ".values") };
                return op switch { "eq" => Expression.Equal(body, Expression.Constant(b)), "ne" => Expression.NotEqual(body, Expression.Constant(b)), _ => throw Bad() };
            }
            case FieldTypes.Date:
            {
                DateOnly D(int i) => DateOnly.TryParseExact(V(i), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
                    : throw Fail.Field(at + ".values", ErrorCodes.ValidationFormat, at + ".values");
                var e = Expression.Convert(body, typeof(DateOnly?));
                Expression C(int i) => Expression.Constant((DateOnly?)D(i), typeof(DateOnly?));
                return Compare(op, e, C, Bad);
            }
            case FieldTypes.Text:
                return op switch
                {
                    "eq" => Expression.Equal(body, Expression.Constant(V(0), typeof(string))),
                    "ne" => Expression.NotEqual(body, Expression.Constant(V(0), typeof(string))),
                    "contains" => Contains(body, V(0)),
                    "in" => Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)], Expression.Constant(values.ToList()), body),
                    _ => throw Bad(),
                };
            default:
            {
                decimal N(int i) => decimal.TryParse(V(i), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n
                    : throw Fail.Field(at + ".values", ErrorCodes.ValidationFormat, at + ".values");
                var e = Expression.Convert(body, typeof(decimal?));
                Expression C(int i) => Expression.Constant((decimal?)N(i), typeof(decimal?));
                return Compare(op, e, C, Bad);
            }
        }
    }

    private static BinaryExpression Compare(string op, Expression e, Func<int, Expression> c, Func<Exception> bad) => op switch
    {
        "eq" => Expression.Equal(e, c(0)),
        "ne" => Expression.NotEqual(e, c(0)),
        "gt" => Expression.GreaterThan(e, c(0)),
        "ge" => Expression.GreaterThanOrEqual(e, c(0)),
        "lt" => Expression.LessThan(e, c(0)),
        "le" => Expression.LessThanOrEqual(e, c(0)),
        "between" => Expression.AndAlso(Expression.GreaterThanOrEqual(e, c(0)), Expression.LessThanOrEqual(e, c(1))),
        _ => throw bad(),
    };

    private static BinaryExpression Contains(Expression body, string text) =>
        Expression.AndAlso(Expression.NotEqual(body, Expression.Constant(null, typeof(string))),
            Expression.Call(body, typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!, Expression.Constant(text)));

    // ------------------------------------------------------------ expression helpers

    private static Expression Replace(LambdaExpression lambda, ParameterExpression x) => new ParameterSwap(lambda.Parameters[0], x).Visit(lambda.Body);

    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>Permission of the builder itself (PRD 3.4: reports.view covers the report builder and saved reports).</summary>
    public const string Permission = PermissionKeys.ReportsView;
}
