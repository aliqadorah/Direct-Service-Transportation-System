using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
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
/// entry and source document (FR-121).
/// </summary>
public sealed class LedgerReportService(IAppDbContext db, MasterDataSupport support, ChartOfAccountsService chart, PartyResolver parties, PostingSources sources,
    CashSupport cash)
{
    public const int MaxPageSize = 1000;

    // ============================================================ shared

    /// <summary>The period, defaulting to the current month up to today (company time); from ≤ to.</summary>
    internal async Task<(DateOnly From, DateOnly To)> PeriodAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var t = to ?? today;
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (f > t) throw Fail.Field("from", ErrorCodes.ValidationRange, "from");
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
        if (costCentreId is long cc) q = q.Where(l => l.CostCentreId == cc);
        var f = from ?? DateOnly.MinValue;
        return await q.GroupBy(l => l.AccountId).Select(g => new Sums(g.Key,
                g.Sum(l => l.JournalEntry.EntryDate < f ? l.Debit : 0), g.Sum(l => l.JournalEntry.EntryDate < f ? l.Credit : 0),
                g.Sum(l => l.JournalEntry.EntryDate >= f ? l.Debit : 0), g.Sum(l => l.JournalEntry.EntryDate >= f ? l.Credit : 0)))
            .ToDictionaryAsync(s => s.AccountId, ct).ConfigureAwait(false);
    }

    private async Task<long?> CostCentreAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = support.Decode<CostCentre>(token) ?? throw Fail.Field("costCentre", ErrorCodes.ValidationFormat, "costCentre");
        return await db.Set<CostCentre>().AsNoTracking().Where(c => c.PublicId == g).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw Fail.Field("costCentre", ErrorCodes.ValidationFormat, "costCentre");
    }

    private async Task<Ref?> CostCentreRefAsync(long? id, CancellationToken ct) => id is long cc
        ? await db.Set<CostCentre>().AsNoTracking().Where(c => c.Id == cc).Select(c => new { c.PublicId, c.Code, c.Name }).SingleAsync(ct).ConfigureAwait(false) is var c
            ? new Ref(support.Token<CostCentre>(c.PublicId), c.Code + " " + c.Name) : null
        : null;

    /// <summary>The opening-balance difference still to clear (FR-110): balance of the mapped difference account, or null when zero or unmapped.</summary>
    private async Task<decimal?> OpeningDifferenceAsync(DateOnly asOf, CancellationToken ct)
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
        var account = await support.FindAsync<Account>(q.Account ?? throw Fail.Field("account", ErrorCodes.ValidationRequired, "account"), ct).ConfigureAwait(false);
        return await LedgerOfAsync(account, q, ct).ConfigureAwait(false);
    }

    private sealed record LineRow(long Id, DateOnly Date, Guid EntryPid, string Number, string Description, string Module, string? SourceType, long? SourceId, long AccountId,
        string? PartyType, long? PartyId, Guid? CcPid, string? CcCode, string? CcName, string? Note, decimal Debit, decimal Credit);

    internal async Task<LedgerReport> LedgerOfAsync(Account account, LedgerQuery q, CancellationToken ct, int? all = null)
    {
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var ids = Subtree(tree, account.Id);
        var costCentreId = await CostCentreAsync(q.CostCentre, ct).ConfigureAwait(false);
        PostingParty? party = null;
        if (!string.IsNullOrWhiteSpace(q.PartyType) || !string.IsNullOrWhiteSpace(q.Party))
        {
            if (q.PartyType is null || !PartyResolver.Types.Contains(q.PartyType)) throw Fail.Field("partyType", ErrorCodes.ValidationFormat, "partyType");
            var pid = await parties.IdOfAsync(q.PartyType, q.Party, ct).ConfigureAwait(false) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, "party");
            party = new PostingParty(PartyResolver.ModuleOf(q.PartyType), q.PartyType, pid);
        }

        var lines = db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.AccountId) && l.JournalEntry.EntryDate <= to);
        if (costCentreId is long cc) lines = lines.Where(l => l.CostCentreId == cc);
        if (party is not null) lines = lines.Where(l => l.PartyModule == party.Module && l.PartyType == party.Type && l.PartyId == party.Id);
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
        l.Debit, l.Credit));

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
            r.CcPid is Guid g ? new Ref(support.Token<CostCentre>(g), r.CcCode + " " + r.CcName) : null, r.Note, r.Debit, r.Credit, balance);
    }

    /// <summary>All lines of a ledger query (exports), at most 20,000.</summary>
    public async Task<LedgerReport> LedgerAllAsync(LedgerQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var account = await support.FindAsync<Account>(q.Account ?? throw Fail.Field("account", ErrorCodes.ValidationRequired, "account"), ct).ConfigureAwait(false);
        return await AllOfAsync(account, q, ct).ConfigureAwait(false);
    }

    // ============================================================ trial balance (FR-117)

    public async Task<TrialBalanceReport> TrialBalanceAsync(TrialBalanceQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (from, to) = await PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        var tree = await TreeAsync(ct).ConfigureAwait(false);
        var maxLevel = tree.Count == 0 ? 1 : tree.Values.Max(n => n.Depth);
        var level = q.Level ?? maxLevel;
        if (level < 1 || level > 20) throw Fail.Field("level", ErrorCodes.ValidationRange, "level");
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
        return new TrialBalanceReport(from, to, level, maxLevel, result, totals, balanced, await OpeningDifferenceAsync(to, ct).ConfigureAwait(false));
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
            if (q.CompareFrom is null || q.CompareTo is null) throw Fail.Field("compareFrom", ErrorCodes.ValidationRequired, "compareFrom");
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
        return new ProfitLossReport(from, to, await CostCentreRefAsync(cc, ct).ConfigureAwait(false), cmp?.From, cmp?.To, revenue, expenses, tr, te, tr - te, ctr, cte,
            compare is null ? null : ctr - cte);
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
        if (!string.IsNullOrWhiteSpace(q.CashBox) == !string.IsNullOrWhiteSpace(q.BankAccount)) throw Fail.Field("cashBox", ErrorCodes.ValidationRequired, "cashBox");
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
            var documents = await DocumentBalanceAsync(a, asOf, ct).ConfigureAwait(false);
            result.Add(new ReconciliationRow(chart.Ref(a), a.ControlPartyType ?? string.Empty, ledger, partySum, ledger - partySum,
                matched.Where(g => g.B != 0).Select(g => (g.PartyType, g.PartyId)).Distinct().Count(), unmatched, documents, documents is decimal fromDocuments ? ledger - fromDocuments : null));
        }
        var pending = await db.Set<PendingPosting>().AsNoTracking().CountAsync(p => p.ResolvedAt == null, ct).ConfigureAwait(false);
        return new ReconciliationReport(asOf, result, result.All(r => r.Difference == 0 && r.DocumentDifference is null or 0), pending);
    }

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
                    - (db.Set<Domain.Trn.Receipt>().Where(r => r.InvoiceId == i.Id && r.Status == "Active" && r.ReceiptDate <= asOf).Sum(r => (decimal?)r.Amount) ?? 0)
                    - (db.Set<Domain.Trn.CreditNote>().Where(n => n.InvoiceId == i.Id && n.Status == "Posted" && n.CreditDate <= asOf).Sum(n => (decimal?)n.Total) ?? 0))
                .SumAsync(ct).ConfigureAwait(false);
            var unapplied = await db.Set<Domain.Trn.Receipt>().AsNoTracking().Where(r => r.InvoiceId == null && r.Status == "Active" && r.ReceiptDate <= asOf)
                .SumAsync(r => r.Amount, ct).ConfigureAwait(false);
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
