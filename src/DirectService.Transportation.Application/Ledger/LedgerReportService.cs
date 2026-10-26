using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Budgets;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Ledger;

/// <summary>
/// Financial reports from the posted ledger (7.6.3): general ledger and account statement (FR-116, FR-120), trial balance at a tree level
/// (FR-117), profit and loss with a cost-centre filter (FR-118), balance sheet with the current result in equity (FR-119), cost-centre
/// report (FR-131), cash/bank statements and balances (FR-130, FR-139), sub-ledger reconciliation (FR-105, RPT-23). Every entry that is
/// not soft-deleted counts — posted, reversed and void entries with their reversals — so voids and edits net to zero; dates are entry
/// dates; balances are debit − credit unless a statement shows them the natural way for the account type. Lines carry links to their
/// entry and source document (FR-121). A cost-centre filter may be <c>none</c>: lines without a cost centre (DT-267).
/// </summary>
public sealed class LedgerReportService(IAppDbContext db, MasterDataSupport support, ChartOfAccountsService chart, PartyResolver parties, PostingSources sources,
    CashSupport cash, IRequestContext ctx)
{
    public const int MaxPageSize = 1000;

    /// <summary>The cost-centre filter value for lines without a cost centre (DT-267: the "No cost centre" column of the profit and loss).</summary>
    public const string NoCostCentre = "none";

    private const long NoCentreId = 0;

    // ============================================================ shared

    /// <summary>The period, defaulting to the current month up to today (company time); from ≤ to.</summary>
    internal async Task<(DateOnly From, DateOnly To)> PeriodAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var t = to ?? today;
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (f > t) throw Fail.Field("from", ErrorCodes.ValidationRange, new FieldRef("from"));
        return (f, t);
    }

    internal sealed record Node(Account Account, int Depth, List<long> Children);

    /// <summary>The chart as a tree: depth (1 = top), children by code.</summary>
    internal async Task<Dictionary<long, Node>> TreeAsync(CancellationToken ct)
    {
        var accounts = await db.Set<Account>().AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);
        var nodes = accounts.ToDictionary(a => a.Id, a => new Node(a, 0, []));
        foreach (var a in accounts.Where(a => a.ParentId is long p && nodes.ContainsKey(p))) nodes[a.ParentId!.Value].Children.Add(a.Id);
        int Depth(long id)
        {
            var depth = 1;
            var seen = new HashSet<long>();
            for (var p = nodes[id].Account.ParentId; p is long pid && nodes.ContainsKey(pid) && seen.Add(pid); p = nodes[pid].Account.ParentId) depth++;
            return depth;
        }
        return accounts.ToDictionary(a => a.Id, a => nodes[a.Id] with { Depth = Depth(a.Id) });
    }

    /// <summary>An account and all its descendants.</summary>
    private static List<long> Subtree(Dictionary<long, Node> tree, long id)
    {
        var result = new List<long>();
        var stack = new Stack<long>([id]);
        while (stack.Count > 0)
        {
            var x = stack.Pop();
            result.Add(x);
            foreach (var c in tree[x].Children) stack.Push(c);
        }
        return result;
    }

    private sealed record Sums(long AccountId, decimal OpenDebit, decimal OpenCredit, decimal MoveDebit, decimal MoveCredit);

    /// <summary>Debits and credits per account before <paramref name="from"/> and from it to <paramref name="to"/>, optionally for one cost centre.</summary>
    private async Task<Dictionary<long, Sums>> SumsAsync(DateOnly? from, DateOnly to, long? costCentreId, CancellationToken ct)
    {
        var q = db.Set<EntryLine>().AsNoTracking().Where(l => l.JournalEntry.EntryDate <= to);
        q = ByCentre(q, costCentreId);
        var f = from ?? DateOnly.MinValue;
        return await q.GroupBy(l => l.AccountId).Select(g => new Sums(g.Key,
                g.Sum(l => l.JournalEntry.EntryDate < f ? l.Debit : 0), g.Sum(l => l.JournalEntry.EntryDate < f ? l.Credit : 0),
                g.Sum(l => l.JournalEntry.EntryDate >= f ? l.Debit : 0), g.Sum(l => l.JournalEntry.EntryDate >= f ? l.Credit : 0)))
            .ToDictionaryAsync(s => s.AccountId, ct).ConfigureAwait(false);
    }

    /// <summary>Lines of one cost centre, or without one (<see cref="NoCentreId"/>); all lines when null.</summary>
    private static IQueryable<EntryLine> ByCentre(IQueryable<EntryLine> q, long? centre) => centre switch
    {
        null => q,
        NoCentreId => q.Where(l => l.CostCentreId == null),
        long cc => q.Where(l => l.CostCentreId == cc),
    };

    private async Task<long?> CostCentreAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (token.Trim() == NoCostCentre) return NoCentreId;
        var g = support.Decode<CostCentre>(token) ?? throw Fail.Field("costCentre", ErrorCodes.ValidationFormat, new FieldRef("costCentre"));
        return await db.Set<CostCentre>().AsNoTracking().Where(c => c.PublicId == g).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw Fail.Field("costCentre", ErrorCodes.ValidationFormat, new FieldRef("costCentre"));
    }

    private async Task<Ref?> CostCentreRefAsync(long? id, CancellationToken ct) => id == NoCentreId ? new Ref(NoCostCentre, NoCentreName)
        : id is long cc
        ? await db.Set<CostCentre>().AsNoTracking().Where(c => c.Id == cc).Select(c => new { c.PublicId, c.Code, c.Name }).SingleAsync(ct).ConfigureAwait(false) is var c
            ? new Ref(support.Token<CostCentre>(c.PublicId), c.Code + " " + c.Name) : null
        : null;

    /// <summary>The opening-balance difference still to clear (FR-110): balance of the mapped difference account, or null when zero or unmapped.</summary>
    internal async Task<decimal?> OpeningDifferenceAsync(DateOnly asOf, CancellationToken ct)
    {
        var accountId = await db.Set<PostingKeyMapping>().AsNoTracking().Where(m => m.PostingKey == PostingKeys.OpeningBalanceDifference)
            .Select(m => (long?)m.AccountId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (accountId is not long id) return null;
        var balance = await db.Set<EntryLine>().AsNoTracking().Where(l => l.AccountId == id && l.JournalEntry.EntryDate <= asOf).SumAsync(l => l.Debit - l.Credit, ct).ConfigureAwait(false);
        return balance == 0 ? null : balance;
    }

    // ============================================================ general ledger / account statement (FR-116, FR-120)

    public async Task<LedgerReport> LedgerAsync(LedgerQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var account = await support.FindAsync<Account>(q.Account ?? throw Fail.Field("account", ErrorCodes.ValidationRequired, new FieldRef("account")), ct).ConfigureAwait(false);
        return await LedgerOfAsync(account, q, ct).ConfigureAwait(false);
    }

    private sealed record LineRow(long Id, DateOnly Date, Guid EntryPid, string Number, string Description, string Module, string? SourceType, long? SourceId, long AccountId,
        string? PartyType, long? PartyId, Guid? CcPid, string? CcCode, string? CcName, string? Note, decimal Debit, decimal Credit, string? Reference);

    internal async Task<LedgerReport> LedgerOfAsync(Account account, LedgerQuery q, CancellationToken ct, int? all = null)
    {
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var ids = Subtree(tree, account.Id);
        var costCentreId = await CostCentreAsync(q.CostCentre, ct).ConfigureAwait(false);
        PostingParty? party = null;
        if (!string.IsNullOrWhiteSpace(q.PartyType) || !string.IsNullOrWhiteSpace(q.Party))
        {
            if (q.PartyType is null || !PartyResolver.Types.Contains(q.PartyType)) throw Fail.Field("partyType", ErrorCodes.ValidationFormat, new FieldRef("partyType"));
            var pid = await parties.IdOfAsync(q.PartyType, q.Party, ct).ConfigureAwait(false) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, new FieldRef("party"));
            party = new PostingParty(PartyResolver.ModuleOf(q.PartyType), q.PartyType, pid);
        }

        var lines = db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= to);
        lines = ByCentre(lines, costCentreId);
        if (party is not null) lines = lines.Where(l => l.PartyModule == party.Module && l.PartyType == party.Type && l.PartyId == party.Id);
        lines = ByReference(lines, q.Reference);
        var opening = await lines.Where(l => l.JournalEntry.EntryDate < from).SumAsync(l => l.Debit - l.Credit, ct).ConfigureAwait(false);
        var period = lines.Where(l => l.JournalEntry.EntryDate >= from);
        var totals = await period.GroupBy(_ => 1).Select(g => new { D = g.Sum(l => l.Debit), C = g.Sum(l => l.Credit), N = g.Count() }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var ordered = period.OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntryId).ThenBy(l => l.LineNo);
        var page = all is null ? Math.Max(1, q.Page) : 1;
        var size = all ?? Math.Clamp(q.PageSize, 1, MaxPageSize);
        var skip = (page - 1) * size;
        var before = skip == 0 ? 0 : await ordered.Take(skip).SumAsync(l => l.Debit - l.Credit, ct).ConfigureAwait(false);
        var rows = await Project(ordered.Skip(skip).Take(size)).ToListAsync(ct).ConfigureAwait(false);
        var described = await DescribeAsync(rows, tree, ct).ConfigureAwait(false);
        var running = opening + before;
        var items = rows.Select(r =>
        {
            running += r.Debit - r.Credit;
            return described(r, running);
        }).ToList();
        var partyRef = party is null ? null : (await parties.DescribeAsync([(party.Type, (long?)party.Id)], ct).ConfigureAwait(false)).GetValueOrDefault((party.Type, party.Id));
        var d = totals?.D ?? 0;
        var c = totals?.C ?? 0;
        return new LedgerReport(chart.Ref(account), account.AccountType, partyRef, await CostCentreRefAsync(costCentreId, ct).ConfigureAwait(false), from, to, opening, d, c,
            opening + d - c, new PagedResult<LedgerLine>(items, page, size, totals?.N ?? 0));
    }

    private static IQueryable<LineRow> Project(IQueryable<EntryLine> q) => q.Select(l => new LineRow(l.Id, l.JournalEntry.EntryDate, l.JournalEntry.PublicId, l.JournalEntry.Number,
        l.JournalEntry.Description, l.JournalEntry.SourceModule, l.JournalEntry.SourceType, l.JournalEntry.SourceId, l.AccountId, l.PartyType, l.PartyId,
        l.CostCentre != null ? l.CostCentre.PublicId : null, l.CostCentre != null ? l.CostCentre.Code : null, l.CostCentre != null ? l.CostCentre.Name : null, l.Note,
        l.Debit, l.Credit, l.Reference ?? l.JournalEntry.Reference));

    /// <summary>DT-254: only the lines whose line reference or entry reference contains the text (like the cost-centre filter, also for the opening).</summary>
    private static IQueryable<EntryLine> ByReference(IQueryable<EntryLine> lines, string? reference) =>
        Common.TextNormalizer.CleanText(reference) is { } r
            ? lines.Where(l => (l.Reference != null && l.Reference.Contains(r)) || (l.JournalEntry.Reference != null && l.JournalEntry.Reference.Contains(r)))
            : lines;

    /// <summary>Entry, source document, account, party and cost-centre references for a page of lines.</summary>
    private async Task<Func<LineRow, decimal, LedgerLine>> DescribeAsync(IReadOnlyList<LineRow> rows, Dictionary<long, Node> tree, CancellationToken ct)
    {
        var partyNames = await parties.DescribeAsync(rows.Select(r => (r.PartyType, r.PartyId)), ct).ConfigureAwait(false);
        var docs = await sources.DescribeAsync(rows.Select(r => (r.Module, r.SourceType, r.SourceId)), ct).ConfigureAwait(false);
        return (r, balance) => new LedgerLine(r.Date, new EntryLinkRef(support.Token<JournalEntry>(r.EntryPid), r.Number), r.Description,
            r.SourceType is not null && r.SourceId is long sid && docs.TryGetValue((r.Module, r.SourceType, sid), out var doc) ? doc
                : r.SourceType is null ? null : new SourceDocumentRef(r.Module, r.SourceType, null, null),
            chart.Ref(tree[r.AccountId].Account),
            r.PartyType is not null && r.PartyId is long pid && partyNames.TryGetValue((r.PartyType, pid), out var p) ? p : null,
            r.CcPid is Guid g ? new Ref(support.Token<CostCentre>(g), r.CcCode + " " + r.CcName) : null, r.Note, r.Debit, r.Credit, balance, r.Reference);
    }

    /// <summary>All lines of a ledger query (exports), at most 20,000.</summary>
    public async Task<LedgerReport> LedgerAllAsync(LedgerQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var account = await support.FindAsync<Account>(q.Account ?? throw Fail.Field("account", ErrorCodes.ValidationRequired, new FieldRef("account")), ct).ConfigureAwait(false);
        return await AllOfAsync(account, q, ct).ConfigureAwait(false);
    }

    // ============================================================ account statement of several accounts (revision 6)

    /// <summary>The most lines of a statement of several accounts (screen and exports).</summary>
    public const int MaxStatementLines = 20_000;

    /// <summary>
    /// Account statement of several accounts (revision 6, replaces the general ledger screen): chosen accounts (a group covers its sub-accounts;
    /// overlapping choices are refused so the grand total counts every line once), a code range or every account (accounts without
    /// sub-accounts); each with opening, movements and running balance and a subtotal; a grand total; optional cost centre and party.
    /// </summary>
    public async Task<AccountStatementsReport> StatementsAsync(AccountStatementsQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var costCentreId = await CostCentreAsync(q.CostCentre, ct).ConfigureAwait(false);
        PostingParty? party = null;
        if (!string.IsNullOrWhiteSpace(q.PartyType) || !string.IsNullOrWhiteSpace(q.Party))
        {
            if (q.PartyType is null || !PartyResolver.Types.Contains(q.PartyType)) throw Fail.Field("partyType", ErrorCodes.ValidationFormat, new FieldRef("partyType"));
            var pid = await parties.IdOfAsync(q.PartyType, q.Party, ct).ConfigureAwait(false) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, new FieldRef("party"));
            party = new PostingParty(PartyResolver.ModuleOf(q.PartyType), q.PartyType, pid);
        }

        // The accounts of the statement: chosen ones (as chosen), or the leaves of a code range / of the whole chart.
        List<long> chosen;
        var explicitChoice = !string.IsNullOrWhiteSpace(q.Accounts);
        if (explicitChoice)
        {
            chosen = [];
            foreach (var token in q.Accounts!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal))
            {
                var g = support.Decode<Account>(token) ?? throw Fail.Field("accounts", ErrorCodes.ValidationFormat, new FieldRef("accounts"));
                var node = tree.Values.FirstOrDefault(n => n.Account.PublicId == g) ?? throw Fail.Field("accounts", ErrorCodes.ValidationFormat, new FieldRef("accounts"));
                chosen.Add(node.Account.Id);
            }
            if (chosen.Count > 500) throw Fail.Field("accounts", ErrorCodes.ValidationRange, new FieldRef("accounts"));
            foreach (var a in chosen)
            {
                for (var p = tree[a].Account.ParentId; p is long pid && tree.ContainsKey(pid); p = tree[pid].Account.ParentId)
                    if (chosen.Contains(pid)) throw Fail.RuleOn("accounts", ErrorCodes.StatementOverlap, tree[a].Account.Code, tree[pid].Account.Code);
            }
        }
        else if (q.AllAccounts == true || !string.IsNullOrWhiteSpace(q.CodeFrom) || !string.IsNullOrWhiteSpace(q.CodeTo))
        {
            var lo = q.AllAccounts == true ? null : Common.TextNormalizer.CleanCode(q.CodeFrom);
            var hi = q.AllAccounts == true ? null : Common.TextNormalizer.CleanCode(q.CodeTo);
            if (lo is not null && hi is not null && string.CompareOrdinal(lo, hi) > 0) throw Fail.Field("codeFrom", ErrorCodes.ValidationRange, new FieldRef("codeFrom"));
            chosen = tree.Values.Where(n => n.Children.Count == 0
                    && (lo is null || string.CompareOrdinal(n.Account.Code, lo) >= 0)
                    && (hi is null || string.CompareOrdinal(n.Account.Code, hi) <= 0 || n.Account.Code.StartsWith(hi, StringComparison.Ordinal)))
                .Select(n => n.Account.Id).ToList();
        }
        else throw Fail.Rule(ErrorCodes.StatementNoAccounts);

        var sections = chosen.OrderBy(id => tree[id].Account.Code, StringComparer.Ordinal).Select(id => (Id: id, Ids: Subtree(tree, id).ToHashSet())).ToList();
        var all = sections.SelectMany(s => s.Ids).Distinct().ToList();
        var lines = db.Set<EntryLine>().AsNoTracking().Where(l => all.Contains(l.AccountId) && l.JournalEntry.EntryDate <= to);
        lines = ByCentre(lines, costCentreId);
        if (party is not null) lines = lines.Where(l => l.PartyModule == party.Module && l.PartyType == party.Type && l.PartyId == party.Id);
        lines = ByReference(lines, q.Reference);
        var openings = await lines.Where(l => l.JournalEntry.EntryDate < from).GroupBy(l => l.AccountId).Select(g => new { g.Key, B = g.Sum(l => l.Debit - l.Credit) })
            .ToDictionaryAsync(x => x.Key, x => x.B, ct).ConfigureAwait(false);
        var rows = await Project(lines.Where(l => l.JournalEntry.EntryDate >= from)
                .OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntryId).ThenBy(l => l.LineNo).Take(MaxStatementLines + 1))
            .ToListAsync(ct).ConfigureAwait(false);
        if (rows.Count > MaxStatementLines) throw Fail.Rule(ErrorCodes.StatementTooLarge, MaxStatementLines);
        var described = await DescribeAsync(rows, tree, ct).ConfigureAwait(false);

        var hideEmpty = !explicitChoice && q.HideEmpty != false;
        var result = new List<AccountStatementSection>();
        foreach (var (id, ids) in sections)
        {
            var opening = ids.Sum(i => openings.GetValueOrDefault(i));
            var own = rows.Where(r => ids.Contains(r.AccountId)).ToList();
            if (hideEmpty && opening == 0 && own.Count == 0) continue;
            var running = opening;
            var items = own.Select(r =>
            {
                running += r.Debit - r.Credit;
                return described(r, running);
            }).ToList();
            var d = own.Sum(r => r.Debit);
            var c = own.Sum(r => r.Credit);
            result.Add(new AccountStatementSection(chart.Ref(tree[id].Account), tree[id].Account.AccountType, opening, d, c, opening + d - c, items));
        }
        var partyRef = party is null ? null : (await parties.DescribeAsync([(party.Type, (long?)party.Id)], ct).ConfigureAwait(false)).GetValueOrDefault((party.Type, party.Id));
        return new AccountStatementsReport(from, to, partyRef, await CostCentreRefAsync(costCentreId, ct).ConfigureAwait(false), result,
            result.Sum(s => s.Opening), result.Sum(s => s.TotalDebit), result.Sum(s => s.TotalCredit), result.Sum(s => s.Closing));
    }

    // ============================================================ trial balance (FR-117)

    public async Task<TrialBalanceReport> TrialBalanceAsync(TrialBalanceQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var maxLevel = tree.Count == 0 ? 1 : tree.Values.Max(n => n.Depth);
        var level = q.Level ?? maxLevel;
        if (level < 1 || level > 20) throw Fail.Field("level", ErrorCodes.ValidationRange, new FieldRef("level"));
        var sums = await SumsAsync(from, to, null, ct).ConfigureAwait(false);

        // Each account with movements is shown at its ancestor of the chosen level (or as itself when shallower).
        long Display(long id)
        {
            var x = id;
            while (tree[x].Depth > level && tree[x].Account.ParentId is long p && tree.ContainsKey(p)) x = p;
            return x;
        }
        var rows = new Dictionary<long, (decimal OD, decimal OC, decimal MD, decimal MC)>();
        foreach (var s in sums.Values.Where(s => tree.ContainsKey(s.AccountId)))
        {
            var key = Display(s.AccountId);
            var cur = rows.GetValueOrDefault(key);
            rows[key] = (cur.OD + s.OpenDebit, cur.OC + s.OpenCredit, cur.MD + s.MoveDebit, cur.MC + s.MoveCredit);
        }
        if (q.HideZero == false)
        {
            foreach (var n in tree.Values.Where(n => n.Depth == level || (n.Depth < level && n.Children.Count == 0))) rows.TryAdd(n.Account.Id, default);
        }
        var result = rows.Select(kv =>
            {
                var n = tree[kv.Key];
                var open = kv.Value.OD - kv.Value.OC;
                var close = open + kv.Value.MD - kv.Value.MC;
                return new TrialBalanceRow(chart.Ref(n.Account), n.Account.AccountType, n.Depth, n.Children.Count == 0, Math.Max(open, 0), Math.Max(-open, 0),
                    kv.Value.MD, kv.Value.MC, Math.Max(close, 0), Math.Max(-close, 0));
            })
            .Where(r => q.HideZero == false || r.OpeningDebit != 0 || r.OpeningCredit != 0 || r.MovementDebit != 0 || r.MovementCredit != 0)
            .OrderBy(r => r.Account.Code, StringComparer.Ordinal).ToList();
        var totals = new TrialBalanceTotals(result.Sum(r => r.OpeningDebit), result.Sum(r => r.OpeningCredit), result.Sum(r => r.MovementDebit),
            result.Sum(r => r.MovementCredit), result.Sum(r => r.ClosingDebit), result.Sum(r => r.ClosingCredit));
        var balanced = totals.OpeningDebit == totals.OpeningCredit && totals.MovementDebit == totals.MovementCredit && totals.ClosingDebit == totals.ClosingCredit;
        if (q.Tree == true) result = TrialBalanceTree(tree, result);
        return new TrialBalanceReport(from, to, level, maxLevel, result, totals, balanced, await OpeningDifferenceAsync(to, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The trial balance as a tree (revision 6): the rows of the chosen level with every parent above them, each parent the column-by-column sum
    /// of its children, in chart order (depth first, children by code). The level rows keep their amounts, so the totals are unchanged.
    /// </summary>
    private List<TrialBalanceRow> TrialBalanceTree(Dictionary<long, Node> tree, List<TrialBalanceRow> levelRows)
    {
        var byCode = tree.Values.ToDictionary(n => n.Account.Code, n => n.Account.Id, StringComparer.Ordinal);
        var sums = new Dictionary<long, decimal[]>();
        foreach (var r in levelRows)
        {
            var id = byCode[r.Account.Code];
            decimal[] values = [r.OpeningDebit, r.OpeningCredit, r.MovementDebit, r.MovementCredit, r.ClosingDebit, r.ClosingCredit];
            for (long? x = id; x is long cur && tree.ContainsKey(cur); x = tree[cur].Account.ParentId)
            {
                if (!sums.TryGetValue(cur, out var acc)) sums[cur] = acc = new decimal[6];
                for (var i = 0; i < 6; i++) acc[i] += values[i];
            }
        }
        var levelById = levelRows.ToDictionary(r => byCode[r.Account.Code]);
        var output = new List<TrialBalanceRow>();
        void Walk(long id)
        {
            var n = tree[id];
            var children = n.Children.Where(sums.ContainsKey).OrderBy(c => tree[c].Account.Code, StringComparer.Ordinal).ToList();
            var parent = n.Account.ParentId is long p && sums.ContainsKey(p) ? chart.Ref(tree[p].Account).Id : null;
            // A row of the chosen level holds its whole subtree (rows of the level never contain one another).
            if (levelById.TryGetValue(id, out var own))
            {
                output.Add(own with { ParentId = parent, IsGroup = false });
                return;
            }
            var s = sums[id];
            output.Add(new TrialBalanceRow(chart.Ref(n.Account), n.Account.AccountType, n.Depth, n.Children.Count == 0, s[0], s[1], s[2], s[3], s[4], s[5], parent, true));
            foreach (var c in children) Walk(c);
        }
        foreach (var root in sums.Keys.Where(id => tree[id].Account.ParentId is not long p || !sums.ContainsKey(p)).OrderBy(id => tree[id].Account.Code, StringComparer.Ordinal))
            Walk(root);
        return output;
    }

    // ============================================================ statements (FR-118, FR-119)

    /// <summary>Rows of the given types in tree order with the totals of their descendants; rows where both amounts are zero are left out.</summary>
    private List<StatementRow> Statement(Dictionary<long, Node> tree, HashSet<string> types, Func<long, decimal> amount, Func<long, decimal>? compare)
    {
        var result = new List<StatementRow>();
        (decimal A, decimal C) Walk(long id, List<StatementRow> into)
        {
            var n = tree[id];
            var own = new List<StatementRow>();
            decimal a = amount(id), c = compare?.Invoke(id) ?? 0;
            foreach (var child in n.Children)
            {
                var (ca, cc) = Walk(child, own);
                a += ca;
                c += cc;
            }
            if (a != 0 || c != 0)
            {
                into.Add(new StatementRow(chart.Ref(n.Account), n.Account.AccountType, n.Depth, n.Children.Count == 0, a, compare is null ? null : c));
                into.AddRange(own);
            }
            return (a, c);
        }
        foreach (var root in tree.Values.Where(n => (n.Account.ParentId is null || !tree.ContainsKey(n.Account.ParentId.Value)) && types.Contains(n.Account.AccountType))
                     .OrderBy(n => n.Account.Code, StringComparer.Ordinal))
            Walk(root.Account.Id, result);
        return result;
    }

    private static readonly HashSet<string> RevenueTypes = ["Revenue"];
    private static readonly HashSet<string> ExpenseTypes = ["Expense"];

    public async Task<ProfitLossReport> ProfitLossAsync(ProfitLossQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var cc = await CostCentreAsync(q.CostCentre, ct).ConfigureAwait(false);
        var sums = await SumsAsync(from, to, cc, ct).ConfigureAwait(false);
        Dictionary<long, Sums>? compare = null;
        (DateOnly From, DateOnly To)? cmp = null;
        if (q.CompareFrom is not null || q.CompareTo is not null)
        {
            if (q.CompareFrom is null || q.CompareTo is null) throw Fail.Field("compareFrom", ErrorCodes.ValidationRequired, new FieldRef("compareFrom"));
            cmp = await PeriodAsync(q.CompareFrom, q.CompareTo, ct).ConfigureAwait(false);
            compare = await SumsAsync(cmp.Value.From, cmp.Value.To, cc, ct).ConfigureAwait(false);
        }
        decimal Rev(Dictionary<long, Sums> s, long id) => s.TryGetValue(id, out var x) ? x.MoveCredit - x.MoveDebit : 0;
        decimal Exp(Dictionary<long, Sums> s, long id) => s.TryGetValue(id, out var x) ? x.MoveDebit - x.MoveCredit : 0;
        var revenue = Statement(tree, RevenueTypes, id => Rev(sums, id), compare is null ? null : id => Rev(compare, id));
        var expenses = Statement(tree, ExpenseTypes, id => Exp(sums, id), compare is null ? null : id => Exp(compare, id));
        var tr = TopTotal(revenue, r => r.Amount);
        var te = TopTotal(expenses, r => r.Amount);
        decimal? ctr = compare is null ? null : TopTotal(revenue, r => r.CompareAmount ?? 0);
        decimal? cte = compare is null ? null : TopTotal(expenses, r => r.CompareAmount ?? 0);
        var mode = string.IsNullOrWhiteSpace(q.ColumnsBy) ? "none" : q.ColumnsBy.Trim();
        if (!ColumnModes.Contains(mode)) throw Fail.Field("columnsBy", ErrorCodes.ValidationFormat, new FieldRef("columnsBy"));
        var (columns, sections, gross, net) = await SectionsAsync(tree, from, to, cc, mode, q.HideZero ?? true, compare is null ? null : id => Rev(compare, id),
            compare is null ? null : id => Exp(compare, id), ct).ConfigureAwait(false);
        return new ProfitLossReport(from, to, await CostCentreRefAsync(cc, ct).ConfigureAwait(false), cmp?.From, cmp?.To, revenue, expenses, tr, te, tr - te, ctr, cte,
            compare is null ? null : ctr - cte, mode, columns, sections, gross, net);
    }

    /// <summary>The "Columns by" choices of the profit and loss (DT-266).</summary>
    public static readonly IReadOnlyList<string> ColumnModes = ["none", "costCentre", "month", "quarter", "year"];

    /// <summary>Most columns one profit and loss shows (e.g. ten years of months).</summary>
    public const int MaxProfitLossColumns = 120;

    private string NoCentreName => ctx.Language == "en" ? "No cost centre" : "بلا مركز تكلفة";

    private sealed record PlMove(long AccountId, long? CostCentreId, int Year, int Month, decimal Debit, decimal Credit);

    /// <summary>
    /// The section of an account (DT-264): its own, else the nearest ancestor's, else the default of its type (revenue = sales, expense =
    /// operating expenses).
    /// </summary>
    internal static string? SectionOf(Dictionary<long, Node> tree, long id)
    {
        var type = tree[id].Account.AccountType;
        var guard = 0;
        for (long? x = id; x is long cur && tree.TryGetValue(cur, out var n) && guard++ < 64; x = n.Account.ParentId)
        {
            if (n.Account.ProfitLossGroup is { } g && ProfitLossGroups.For(type).Contains(g)) return g;
        }
        return ProfitLossGroups.Default(type);
    }

    /// <summary>
    /// The profit and loss by section (DT-264, DT-266): the columns (one; the cost centres with amounts and "no cost centre"; or the months,
    /// quarters or years of the period, the first and last cut to the period), every section's accounts in tree order — a group totals its
    /// sub-accounts of that section; with <paramref name="hideZero"/> accounts without amounts are left out — the section totals, gross profit
    /// (sales − cost of sales) and net profit (gross − operating + other income − other expenses). The total of a row is the sum of its columns.
    /// </summary>
    private async Task<(List<ProfitLossColumn>, List<ProfitLossSection>, ProfitLossFigure, ProfitLossFigure)> SectionsAsync(Dictionary<long, Node> tree, DateOnly from,
        DateOnly to, long? centre, string mode, bool hideZero, Func<long, decimal>? compareRevenue, Func<long, decimal>? compareExpense, CancellationToken ct)
    {
        var byCentre = mode == "costCentre";
        var q = ByCentre(db.Set<EntryLine>().AsNoTracking().Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to
            && (l.Account.AccountType == "Revenue" || l.Account.AccountType == "Expense")), centre);
        var moves = await q.GroupBy(l => new { l.AccountId, CostCentreId = byCentre ? l.CostCentreId : null, l.JournalEntry.EntryDate.Year, l.JournalEntry.EntryDate.Month })
            .Select(g => new PlMove(g.Key.AccountId, g.Key.CostCentreId, g.Key.Year, g.Key.Month, g.Sum(l => l.Debit), g.Sum(l => l.Credit)))
            .ToListAsync(ct).ConfigureAwait(false);

        var columns = new List<ProfitLossColumn>();
        Func<PlMove, int> columnOf;
        var en = ctx.Language == "en";
        if (byCentre)
        {
            var ids = moves.Where(m => m.CostCentreId is not null).Select(m => m.CostCentreId!.Value).Distinct().ToList();
            if (centre is long only && only != NoCentreId && !ids.Contains(only)) ids.Add(only);
            var centres = await db.Set<CostCentre>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.PublicId, c.Code, c.Name }).ToListAsync(ct).ConfigureAwait(false);
            var index = new Dictionary<long, int>();
            foreach (var c in centres.OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                index[c.Id] = columns.Count;
                var token = support.Token<CostCentre>(c.PublicId);
                columns.Add(new ProfitLossColumn(token, c.Code + " " + c.Name, from, to, token));
            }
            // Lines without a cost centre have their own column, unless the report is for one cost centre.
            var none = columns.Count;
            if (centre is not long one || one == NoCentreId) columns.Add(new ProfitLossColumn(NoCostCentre, NoCentreName, from, to, NoCostCentre));
            columnOf = m => m.CostCentreId is long c ? index[c] : none;
        }
        else if (mode == "none")
        {
            columns.Add(new ProfitLossColumn("total", en ? "Amount" : "المبلغ", from, to, null));
            columnOf = _ => 0;
        }
        else
        {
            var months = mode switch { "quarter" => 3, "year" => 12, _ => 1 };
            var start = new DateOnly(from.Year, (from.Month - 1) / months * months + 1, 1);
            var index = new Dictionary<(int, int), int>();
            for (var s = start; s <= to; s = s.AddMonths(months))
            {
                if (columns.Count == MaxProfitLossColumns) throw Fail.Field("to", ErrorCodes.ValidationRange, new FieldRef("to"));
                var e = s.AddMonths(months).AddDays(-1);
                for (var m = 0; m < months; m++) index[(s.AddMonths(m).Year, s.AddMonths(m).Month)] = columns.Count;
                var year = s.Year.ToString(CultureInfo.InvariantCulture);
                var quarter = (s.Month - 1) / 3 + 1;
                var name = mode switch
                {
                    "year" => year,
                    "quarter" => (en ? $"Q{quarter} " : $"الربع {quarter} ") + year,
                    _ => (en ? BudgetImportService.MonthNames[s.Month - 1].En[..3] : BudgetImportService.MonthNames[s.Month - 1].Ar) + " " + year,
                };
                var key = mode switch { "year" => year, "quarter" => $"{year}-Q{quarter}", _ => year + "-" + s.Month.ToString("00", CultureInfo.InvariantCulture) };
                columns.Add(new ProfitLossColumn(key, name, s < from ? from : s, e > to ? to : e, null));
            }
            columnOf = m => index[(m.Year, m.Month)];
        }

        var k = columns.Count;
        var amounts = new Dictionary<long, decimal[]>();
        foreach (var m in moves)
        {
            if (!tree.TryGetValue(m.AccountId, out var n)) continue;
            if (!amounts.TryGetValue(m.AccountId, out var a)) amounts[m.AccountId] = a = new decimal[k];
            a[columnOf(m)] += n.Account.AccountType == "Revenue" ? m.Credit - m.Debit : m.Debit - m.Credit;
        }
        var section = tree.Keys.ToDictionary(id => id, id => SectionOf(tree, id));
        var withCompare = compareRevenue is not null;
        decimal Compare(long id) => tree[id].Account.AccountType switch
        {
            "Revenue" => compareRevenue?.Invoke(id) ?? 0,
            "Expense" => compareExpense?.Invoke(id) ?? 0,
            _ => 0,
        };
        var roots = tree.Values.Where(n => n.Account.AccountType is "Revenue" or "Expense"
                && (n.Account.ParentId is not long p || !tree.TryGetValue(p, out var parent) || parent.Account.AccountType != n.Account.AccountType))
            .OrderBy(n => n.Account.AccountType == "Revenue" ? 0 : 1).ThenBy(n => n.Account.Code, StringComparer.Ordinal).Select(n => n.Account.Id).ToList();

        var result = new List<ProfitLossSection>();
        var totals = new Dictionary<string, ProfitLossFigure>();
        foreach (var group in ProfitLossGroups.Order)
        {
            var rows = new List<ProfitLossRow>();
            (decimal[] A, decimal C, bool Member) Walk(long id, string? parentToken, List<ProfitLossRow> into)
            {
                var n = tree[id];
                var mine = section[id] == group;
                var a = mine && amounts.TryGetValue(id, out var x) ? (decimal[])x.Clone() : new decimal[k];
                var c = mine && withCompare ? Compare(id) : 0;
                var member = mine;
                var own = new List<ProfitLossRow>();
                var token = chart.Ref(n.Account).Id;
                foreach (var child in n.Children.OrderBy(ch => tree[ch].Account.Code, StringComparer.Ordinal))
                {
                    var (ca, cc, cm) = Walk(child, token, own);
                    for (var i = 0; i < k; i++) a[i] += ca[i];
                    c += cc;
                    member |= cm;
                }
                // Kept when it has an amount or rows below it (amounts of sub-accounts may cancel out), or when zero rows are shown.
                if (a.Any(v => v != 0) || c != 0 || own.Count > 0 || (!hideZero && member))
                {
                    into.Add(new ProfitLossRow(chart.Ref(n.Account), n.Account.AccountType, n.Depth, n.Children.Count == 0, parentToken, a, a.Sum(), withCompare ? c : null));
                    into.AddRange(own);
                }
                return (a, c, member);
            }
            foreach (var root in roots) Walk(root, null, rows);
            var sum = new decimal[k];
            foreach (var (_, a) in amounts.Where(x => section[x.Key] == group))
            {
                for (var i = 0; i < k; i++) sum[i] += a[i];
            }
            decimal? compareSum = withCompare ? tree.Keys.Where(id => section[id] == group).Sum(Compare) : null;
            var figure = new ProfitLossFigure(sum, sum.Sum(), compareSum);
            totals[group] = figure;
            result.Add(new ProfitLossSection(group, rows, figure));
        }
        ProfitLossFigure Combine(params (string Group, int Sign)[] parts)
        {
            var a = new decimal[k];
            decimal c = 0;
            foreach (var (g, sign) in parts)
            {
                for (var i = 0; i < k; i++) a[i] += sign * totals[g].Amounts[i];
                c += sign * (totals[g].Compare ?? 0);
            }
            return new ProfitLossFigure(a, a.Sum(), withCompare ? c : null);
        }
        var gross = Combine((ProfitLossGroups.Sales, 1), (ProfitLossGroups.CostOfSales, -1));
        var net = Combine((ProfitLossGroups.Sales, 1), (ProfitLossGroups.CostOfSales, -1), (ProfitLossGroups.Operating, -1), (ProfitLossGroups.OtherIncome, 1),
            (ProfitLossGroups.OtherExpense, -1));
        return (columns, result, gross, net);
    }

    /// <summary>Sum of the top rows of a statement (rows whose level is the smallest in their branch: the roots).</summary>
    private static decimal TopTotal(List<StatementRow> rows, Func<StatementRow, decimal> value)
    {
        decimal total = 0;
        // Roots are the rows whose ancestors are not in the list; the walk adds a root before its descendants, so a row is a root when its
        // level is not greater than the level of the last root seen.
        var lastRootLevel = int.MaxValue;
        foreach (var r in rows)
        {
            if (r.Level <= lastRootLevel)
            {
                total += value(r);
                lastRootLevel = r.Level;
            }
        }
        return total;
    }

    private static readonly HashSet<string> AssetTypes = ["Asset"];
    private static readonly HashSet<string> LiabilityTypes = ["Liability"];
    private static readonly HashSet<string> EquityTypes = ["Equity"];

    public async Task<BalanceSheetReport> BalanceSheetAsync(BalanceSheetQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var asOf = q.AsOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        // Opening = before 1 January of the as-of year (prior years), movement = 1 January … as-of date (current year, DT-103).
        var sums = await SumsAsync(new DateOnly(asOf.Year, 1, 1), asOf, null, ct).ConfigureAwait(false);
        decimal Debit(long id) => sums.TryGetValue(id, out var x) ? x.OpenDebit + x.MoveDebit - x.OpenCredit - x.MoveCredit : 0;
        var assets = Statement(tree, AssetTypes, Debit, null);
        var liabilities = Statement(tree, LiabilityTypes, id => -Debit(id), null);
        var equity = Statement(tree, EquityTypes, id => -Debit(id), null);
        decimal current = 0, prior = 0;
        foreach (var s in sums.Values.Where(s => tree.TryGetValue(s.AccountId, out var n) && n.Account.AccountType is "Revenue" or "Expense"))
        {
            prior += s.OpenCredit - s.OpenDebit;
            current += s.MoveCredit - s.MoveDebit;
        }
        var ta = TopTotal(assets, r => r.Amount);
        var tl = TopTotal(liabilities, r => r.Amount);
        var te = TopTotal(equity, r => r.Amount) + current + prior;
        return new BalanceSheetReport(asOf, assets, liabilities, equity, current, prior, ta, tl, te, tl + te, ta == tl + te, await OpeningDifferenceAsync(asOf, ct).ConfigureAwait(false));
    }

    // ============================================================ cost centres (FR-131)

    public async Task<CostCentreReport> CostCentresAsync(PeriodQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var rows = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && (l.Account.AccountType == "Revenue" || l.Account.AccountType == "Expense"))
            .GroupBy(l => l.CostCentreId)
            .Select(g => new
            {
                g.Key,
                Revenue = g.Sum(l => l.Account.AccountType == "Revenue" ? l.Credit - l.Debit : 0),
                Expenses = g.Sum(l => l.Account.AccountType == "Expense" ? l.Debit - l.Credit : 0),
            }).ToListAsync(ct).ConfigureAwait(false);
        var centres = await db.Set<CostCentre>().AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        var result = centres.Select(c => (c, rows.FirstOrDefault(r => r.Key == c.Id)))
            .Where(x => x.Item2 is not null || x.c.Status == AccountingCodes.Active)
            .Select(x => new CostCentreRow(new Ref(support.Token<CostCentre>(x.c.PublicId), x.c.Code + " " + x.c.Name), x.Item2?.Revenue ?? 0, x.Item2?.Expenses ?? 0,
                (x.Item2?.Revenue ?? 0) - (x.Item2?.Expenses ?? 0)))
            .ToList();
        var none = rows.FirstOrDefault(r => r.Key is null);
        if (none is not null) result.Add(new CostCentreRow(null, none.Revenue, none.Expenses, none.Revenue - none.Expenses));
        var totals = new CostCentreRow(null, result.Sum(r => r.Revenue), result.Sum(r => r.Expenses), result.Sum(r => r.Net));
        return new CostCentreReport(from, to, result, totals);
    }

    // ============================================================ cash and banks (FR-130, FR-139)

    public async Task<CashBankStatement> CashBankStatementAsync(CashBankStatementQuery q, CancellationToken ct, bool all = false)
    {
        ArgumentNullException.ThrowIfNull(q);
        MoneyAccountRef money;
        long accountId;
        if (!string.IsNullOrWhiteSpace(q.CashBox) == !string.IsNullOrWhiteSpace(q.BankAccount)) throw Fail.Field("cashBox", ErrorCodes.ValidationRequired, new FieldRef("cashBox"));
        if (!string.IsNullOrWhiteSpace(q.CashBox))
        {
            var box = await support.FindAsync<CashBox>(q.CashBox, ct).ConfigureAwait(false);
            money = cash.Account(box.PublicId, box.Name, null, null);
            accountId = box.AccountId;
        }
        else
        {
            var bank = await support.FindAsync<BankAccount>(q.BankAccount, ct).ConfigureAwait(false);
            money = cash.Account(null, null, bank.PublicId, bank.Name);
            accountId = bank.AccountId;
        }
        var account = await db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
        var lq = new LedgerQuery(null, q.From, q.To, null, null, null, q.Page, q.PageSize);
        var gl = all ? await AllOfAsync(account, lq, ct).ConfigureAwait(false) : await LedgerOfAsync(account, lq, ct).ConfigureAwait(false);
        return new CashBankStatement(money, gl.Account, gl.From, gl.To, gl.Opening, gl.TotalDebit, gl.TotalCredit, gl.Closing, gl.Lines);
    }

    private async Task<LedgerReport> AllOfAsync(Account account, LedgerQuery q, CancellationToken ct)
    {
        // One ordered query, running balance computed on the way (review L7).
        return await LedgerOfAsync(account, q, ct, SheetData.MaxRows).ConfigureAwait(false);
    }

    /// <summary>Balance of every cash box and bank account as of a date (default today), archived ones included.</summary>
    public async Task<IReadOnlyList<CashBankBalance>> CashBankBalancesAsync(DateOnly? asOf, CancellationToken ct)
    {
        var date = asOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var boxes = await db.Set<CashBox>().AsNoTracking().Include(b => b.Account).OrderBy(b => b.Name).ToListAsync(ct).ConfigureAwait(false);
        var banks = await db.Set<BankAccount>().AsNoTracking().Include(b => b.Account).OrderBy(b => b.Name).ToListAsync(ct).ConfigureAwait(false);
        var ids = boxes.Select(b => b.AccountId).Concat(banks.Select(b => b.AccountId)).ToList();
        var balances = await db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= date)
            .GroupBy(l => l.AccountId).Select(g => new { g.Key, B = g.Sum(l => l.Debit - l.Credit) }).ToDictionaryAsync(x => x.Key, x => x.B, ct).ConfigureAwait(false);
        return boxes.Select(b => new CashBankBalance(cash.Account(b.PublicId, b.Name, null, null), chart.Ref(b.Account), b.Status, balances.GetValueOrDefault(b.AccountId)))
            .Concat(banks.Select(b => new CashBankBalance(cash.Account(null, null, b.PublicId, b.Name), chart.Ref(b.Account), b.Status, balances.GetValueOrDefault(b.AccountId))))
            .ToList();
    }

    // ============================================================ sub-ledger reconciliation (FR-105, RPT-23)

    public const int MaxUnmatchedPerAccount = 200;

    public async Task<ReconciliationReport> ReconciliationAsync(ReconciliationQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var asOf = q.AsOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var controls = await db.Set<Account>().AsNoTracking().Where(a => a.IsControlAccount).OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);
        var ids = controls.Select(a => a.Id).ToList();
        var groups = await db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= asOf)
            .GroupBy(l => new { l.AccountId, l.PartyModule, l.PartyType, l.PartyId })
            .Select(g => new { g.Key.AccountId, g.Key.PartyModule, g.Key.PartyType, g.Key.PartyId, B = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var apArIds = groups.Where(g => g.PartyType == AccountingCodes.PartyApAr && g.PartyId is not null).Select(g => g.PartyId!.Value).Distinct().ToList();
        var roles = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(p => apArIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartyRole, ct).ConfigureAwait(false);
        // DT-262: what documents posted (automatic entries, not the opening entry) and the manual / opening entries on each control account.
        var posted = await db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= asOf
                && !l.JournalEntry.IsManual && l.JournalEntry.SourceType != AccountingCodes.SourceOpening)
            .GroupBy(l => l.AccountId).Select(g => new { g.Key, B = g.Sum(l => l.Debit - l.Credit) }).ToDictionaryAsync(x => x.Key, x => x.B, ct).ConfigureAwait(false);
        var gapLines = await db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= asOf
                && (l.JournalEntry.IsManual || l.JournalEntry.SourceType == AccountingCodes.SourceOpening))
            .GroupBy(l => new { l.AccountId, l.JournalEntryId }).Select(g => new GapLine(g.Key.AccountId, g.Key.JournalEntryId, g.Sum(l => l.Debit - l.Credit)))
            .ToListAsync(ct).ConfigureAwait(false);
        var gapEntryIds = gapLines.Select(g => g.JournalEntryId).Distinct().ToList();
        var entries = await db.Set<JournalEntry>().AsNoTracking().Where(e => gapEntryIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        bool Matches(Account a, string? module, string? type, long? id) =>
            module is not null && type is not null && id is long pid && PostingEngine.PartyMatches(a.ControlPartyType, new PostingParty(module, type, pid), roles);

        var result = new List<ReconciliationRow>();
        foreach (var a in controls)
        {
            var mine = groups.Where(g => g.AccountId == a.Id).ToList();
            var ledger = mine.Sum(g => g.B);
            var matched = mine.Where(g => Matches(a, g.PartyModule, g.PartyType, g.PartyId)).ToList();
            var partySum = matched.Sum(g => g.B);
            var unmatched = new List<UnmatchedLine>();
            if (ledger != partySum || mine.Count > matched.Count)
            {
                // Candidates in SQL by type and, for accounting parties, by the ids of the wrong role (review L1); confirmed in memory.
                var allowed = AllowedTypes(a.ControlPartyType);
                var wrongRole = mine.Where(g => g.PartyType == AccountingCodes.PartyApAr && g.PartyId is long pid && !Matches(a, g.PartyModule, g.PartyType, pid))
                    .Select(g => g.PartyId).ToList();
                var raw = await db.Set<EntryLine>().AsNoTracking().Where(l => l.AccountId == a.Id && l.JournalEntry.EntryDate <= asOf)
                    .Where(l => l.PartyId == null || l.PartyType == null || l.PartyModule == null || !allowed.Contains(l.PartyType)
                        || (l.PartyType == AccountingCodes.PartyApAr && wrongRole.Contains(l.PartyId)))
                    .OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntryId).Take(MaxUnmatchedPerAccount * 4)
                    .Select(l => new { l.PartyModule, l.PartyType, l.PartyId, l.JournalEntry.EntryDate, l.JournalEntry.PublicId, l.JournalEntry.Number, l.JournalEntry.Description,
                        l.JournalEntry.SourceModule, l.JournalEntry.SourceType, l.JournalEntry.SourceId, l.Debit, l.Credit })
                    .ToListAsync(ct).ConfigureAwait(false);
                var bad = raw.Where(l => !Matches(a, l.PartyModule, l.PartyType, l.PartyId)).Take(MaxUnmatchedPerAccount).ToList();
                var docs = await sources.DescribeAsync(bad.Select(l => (l.SourceModule, l.SourceType, l.SourceId)), ct).ConfigureAwait(false);
                unmatched = bad.Select(l => new UnmatchedLine(l.EntryDate, new EntryLinkRef(support.Token<JournalEntry>(l.PublicId), l.Number), l.Description,
                    l.SourceType is not null && l.SourceId is long sid && docs.TryGetValue((l.SourceModule, l.SourceType, sid), out var d) ? d : null,
                    l.Debit, l.Credit, l.PartyId is null ? "noParty" : "wrongPartyType")).ToList();
            }
            // DT-262: documents figures for every control account — rebuilt from the documents, or (where they cannot rebuild it) what documents posted.
            var rebuilt = await DocumentBalanceAsync(a, asOf, ct).ConfigureAwait(false);
            var documents = rebuilt ?? posted.GetValueOrDefault(a.Id);
            var mineGap = gapLines.Where(g => g.AccountId == a.Id).ToList();
            var manual = mineGap.Where(g => entries[g.JournalEntryId].IsManual).Sum(g => g.B);
            var opening = mineGap.Where(g => !entries[g.JournalEntryId].IsManual).Sum(g => g.B);
            var gapList = mineGap.Select(g => (Line: g, Entry: entries[g.JournalEntryId]))
                .Where(x => x.Line.B != 0 && x.Entry.Status == AccountingCodes.Posted && x.Entry.ReversesEntryId is null)
                .OrderBy(x => x.Entry.EntryDate).ThenBy(x => x.Entry.Number, StringComparer.Ordinal).Take(MaxUnmatchedPerAccount)
                .Select(x => new DocumentGapEntry(x.Entry.EntryDate, new EntryLinkRef(support.Token<JournalEntry>(x.Entry.PublicId), x.Entry.Number), x.Entry.Description,
                    x.Entry.IsManual ? AccountingCodes.SourceManual : AccountingCodes.SourceOpening, x.Line.B)).ToList();
            var documentDifference = ledger - documents;
            result.Add(new ReconciliationRow(chart.Ref(a), a.ControlPartyType ?? string.Empty, ledger, partySum, ledger - partySum,
                matched.Where(g => g.B != 0).Select(g => (g.PartyType, g.PartyId)).Distinct().Count(), unmatched, documents, documentDifference,
                rebuilt is not null, manual, opening, documentDifference - manual - opening, gapList));
        }
        var pending = await db.Set<PendingPosting>().AsNoTracking().CountAsync(p => p.ResolvedAt == null, ct).ConfigureAwait(false);
        var partiesMatch = result.All(r => r.Difference == 0);
        var level = !partiesMatch ? ReconciliationLevels.Differences
            : result.Any(r => r.DocumentDifference is not null and not 0) ? ReconciliationLevels.Documents : ReconciliationLevels.Ok;
        return new ReconciliationReport(asOf, result, partiesMatch, pending, level);
    }

    private sealed record GapLine(long AccountId, long JournalEntryId, decimal B);

    private static string[] AllowedTypes(string? controlType) => controlType switch
    {
        "Customer" => [AccountingCodes.PartyCustomer, AccountingCodes.PartyApAr],
        "Supplier" => [AccountingCodes.PartyApAr],
        "Driver" => [AccountingCodes.PartyDriver],
        "Partner" => [AccountingCodes.PartyPartner],
        _ => [],
    };

    /// <summary>
    /// The sub-ledger from the documents as of the date (review M2, DT-104), in the ledger's sign (debit − credit): AP/AR documents of the parties
    /// on this control account (receivables +, bills −; total − payments − returns dated up to the date); on the trade-receivables account also
    /// approved transport invoices (total − their receipts and credit notes) less on-account receipts; partner payable −(partner cost − payments);
    /// driver advances + advances not settled by then. Null for accounts the documents cannot rebuild (driver payable depends on the accrual
    /// setting at each trip's save).
    /// </summary>
    private async Task<decimal?> DocumentBalanceAsync(Account a, DateOnly asOf, CancellationToken ct)
    {
        var keys = await db.Set<PostingKeyMapping>().AsNoTracking().Where(m => m.AccountId == a.Id).Select(m => m.PostingKey).ToListAsync(ct).ConfigureAwait(false);
        decimal? total = null;
        var parties = db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(p => p.ControlAccountId == a.Id).Select(p => p.Id);
        if (await parties.AnyAsync(ct).ConfigureAwait(false))
        {
            var docs = await db.Set<ApArDocument>().AsNoTracking().Where(d => parties.Contains(d.PartyId) && d.Status == "Posted" && d.DocumentDate <= asOf)
                .Select(d => new
                {
                    d.DocumentKind, d.Total,
                    Paid = db.Set<ApArPayment>().Where(p => p.DocumentId == d.Id && p.Status == "Active" && p.PaymentDate <= asOf).Sum(p => (decimal?)p.Amount) ?? 0,
                    Returned = db.Set<ApArReturn>().Where(x => x.DocumentId == d.Id && x.Status == "Posted" && x.ReturnDate <= asOf).Sum(x => (decimal?)x.Total) ?? 0,
                }).ToListAsync(ct).ConfigureAwait(false);
            total = docs.Sum(d => (d.DocumentKind == "Receivable" ? 1 : -1) * (d.Total - d.Paid - d.Returned));
        }
        if (keys.Contains(PostingKeys.TradeReceivables))
        {
            var invoices = await db.Set<Domain.Trn.Invoice>().AsNoTracking().Where(i => (i.Status == "Approved" || i.Status == "Closed") && i.InvoiceDate <= asOf)
                .Select(i => i.Total
                    - (db.Set<Domain.Trn.ReceiptAllocation>().Where(a => a.InvoiceId == i.Id && a.Receipt.Status == "Active" && a.Receipt.ReceiptDate <= asOf).Sum(a => (decimal?)a.Amount) ?? 0)
                    - (db.Set<Domain.Trn.CreditNote>().Where(n => n.InvoiceId == i.Id && n.Status == "Posted" && n.CreditDate <= asOf).Sum(n => (decimal?)n.Total) ?? 0))
                .SumAsync(ct).ConfigureAwait(false);
            var unapplied = await db.Set<Domain.Trn.Receipt>().AsNoTracking().Where(r => r.Status == "Active" && r.ReceiptDate <= asOf)
                .SumAsync(r => r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0), ct).ConfigureAwait(false);
            total = (total ?? 0) + invoices - unapplied;
        }
        if (keys.Contains(PostingKeys.PartnerPayable))
        {
            var cost = await db.Set<Domain.Trn.Trip>().AsNoTracking().Where(t => t.Status == "Active" && t.PartnerId != null && t.TripDate <= asOf).SumAsync(t => t.PartnerCost, ct)
                .ConfigureAwait(false);
            var paid = await db.Set<Domain.Trn.PartnerPayment>().AsNoTracking().Where(p => p.Status == "Active" && p.PaymentDate <= asOf).SumAsync(p => p.Amount, ct).ConfigureAwait(false);
            total = (total ?? 0) - (cost - paid);
        }
        if (keys.Contains(PostingKeys.DriverAdvances))
        {
            total = (total ?? 0) + await db.Set<Domain.Trn.Advance>().AsNoTracking()
                .Where(x => x.Status == "Active" && x.AdvanceDate <= asOf && (x.SettlementId == null || x.Settlement!.Status != "Active" || x.Settlement.SettlementDate > asOf))
                .SumAsync(x => x.Amount, ct).ConfigureAwait(false);
        }
        return total;
    }
}
