using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;

namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>One posting account of the budget grid (DT-265): its twelve amounts (empty = none), selection, and a version that re-renders its row.</summary>
public sealed class BudgetGridRowModel(BudgetGridAccount account)
{
    public BudgetGridAccount Account { get; } = account;
    public decimal?[] Months { get; } = new decimal?[12];
    /// <summary>Text typed in a cell that is not an amount (shown as typed and marked; the amount keeps its last valid value).</summary>
    public string?[] Invalid { get; } = new string?[12];
    public bool Selected { get; set; }
    /// <summary>Amounts were saved for this account when the budget was loaded ("Existing" filter).</summary>
    public bool Saved { get; set; }
    /// <summary>A group above it has its own line from earlier (kept as it is): no amount on this account for the whole company (no double counting).</summary>
    public bool Locked { get; set; }
    public int Version { get; private set; }
    public decimal Total => Months.Sum(v => v ?? 0);
    public bool HasAmounts => Months.Any(v => v is not null and not 0);

    public void Changed() => Version++;
}

/// <summary>A cell typed in: the row, the month (0 = January) and the text.</summary>
public sealed record BudgetCellChange(BudgetGridRowModel Row, int Month, string? Text);

/// <summary>A line of the grid as shown: a group heading (with the subtotals of its sub-accounts) or a posting account.</summary>
public sealed record BudgetGridItem(BudgetGridAccount Account, BudgetGridRowModel? Row, decimal[]? Subtotals);

/// <summary>
/// The budget grid's rules (DT-265): which accounts the filters show, the group headings above them in tree order, and the helpers on the
/// selected rows. Filters only choose the rows shown; they never change the amounts (hidden rows keep theirs and are saved).
/// </summary>
public static class BudgetGrid
{
    public const string TypePl = "pl";
    public const string TypeRevenue = "Revenue";
    public const string TypeExpense = "Expense";
    public const string TypeBalance = "bs";
    public const string TypeExisting = "existing";
    public const string TypeAll = "all";

    public static readonly IReadOnlyList<string> Types = [TypePl, TypeRevenue, TypeExpense, TypeBalance, TypeExisting, TypeAll];

    /// <summary>Whether a posting account passes the filters: type, the accounts used with the chosen cost centre (null = any) and the search text.</summary>
    public static bool Shows(BudgetGridRowModel row, string type, IReadOnlySet<string>? centreAccounts, string? search)
    {
        var a = row.Account;
        if (!a.IsActive && !row.HasAmounts && !row.Saved) return false;
        var byType = type switch
        {
            TypeRevenue => a.AccountType == "Revenue",
            TypeExpense => a.AccountType == "Expense",
            TypeBalance => a.AccountType is not ("Revenue" or "Expense"),
            TypeExisting => row.Saved || row.HasAmounts,
            TypeAll => true,
            _ => a.AccountType is "Revenue" or "Expense",
        };
        if (!byType) return false;
        if (centreAccounts is not null && !centreAccounts.Contains(a.Account.Id)) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        var s = search.Trim();
        return a.Account.Code.StartsWith(s, StringComparison.OrdinalIgnoreCase) || a.Account.NameAr.Contains(s, StringComparison.OrdinalIgnoreCase)
            || (a.Account.NameEn?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>The accounts in tree order (children by code under their parent).</summary>
    public static IReadOnlyList<BudgetGridAccount> TreeOrder(IReadOnlyList<BudgetGridAccount> accounts)
    {
        var ids = accounts.Select(a => a.Account.Id).ToHashSet(StringComparer.Ordinal);
        var children = accounts.Where(a => a.ParentId is { } p && ids.Contains(p)).GroupBy(a => a.ParentId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Account.Code, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var result = new List<BudgetGridAccount>(accounts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Walk(BudgetGridAccount a)
        {
            if (!seen.Add(a.Account.Id)) return;
            result.Add(a);
            foreach (var c in children.GetValueOrDefault(a.Account.Id) ?? []) Walk(c);
        }
        foreach (var root in accounts.Where(a => a.ParentId is not { } p || !ids.Contains(p)).OrderBy(a => a.Account.Code, StringComparer.Ordinal)) Walk(root);
        return result;
    }

    /// <summary>
    /// The lines shown: every posting account that passes the filters, with the group headings above it; a heading's subtotals are the
    /// amounts of all its sub-accounts in the budget (also those the filters hide).
    /// </summary>
    public static List<BudgetGridItem> View(IReadOnlyList<BudgetGridAccount> ordered, IReadOnlyDictionary<string, BudgetGridRowModel> rows, Func<BudgetGridRowModel, bool> shows)
    {
        var parentOf = ordered.ToDictionary(a => a.Account.Id, a => a.ParentId, StringComparer.Ordinal);
        var visibleGroups = new HashSet<string>(StringComparer.Ordinal);
        var sums = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        foreach (var row in rows.Values)
        {
            var shown = shows(row);
            var guard = 0;
            for (var p = row.Account.ParentId; p is not null && parentOf.ContainsKey(p) && guard++ < 64; p = parentOf[p])
            {
                if (shown) visibleGroups.Add(p);
                if (!row.HasAmounts) continue;
                if (!sums.TryGetValue(p, out var s)) sums[p] = s = new decimal[12];
                for (var m = 0; m < 12; m++) s[m] += row.Months[m] ?? 0;
            }
        }
        var items = new List<BudgetGridItem>();
        foreach (var a in ordered)
        {
            if (rows.TryGetValue(a.Account.Id, out var row))
            {
                if (shows(row)) items.Add(new BudgetGridItem(a, row, null));
            }
            else if (visibleGroups.Contains(a.Account.Id)) items.Add(new BudgetGridItem(a, null, sums.GetValueOrDefault(a.Account.Id) ?? new decimal[12]));
        }
        return items;
    }

    /// <summary>A yearly amount spread evenly over the twelve months, rounded to the currency decimals; December takes the rounding difference.</summary>
    public static decimal[] Distribute(decimal yearly, int decimals = 3)
    {
        var month = Math.Round(yearly / 12m, decimals, MidpointRounding.AwayFromZero);
        var months = Enumerable.Repeat(month, 12).ToArray();
        months[11] = yearly - month * 11;
        return months;
    }

    /// <summary>Copies the amount of month <paramref name="from"/> (0 = January) to every following month.</summary>
    public static void Fill(decimal?[] months, int from)
    {
        ArgumentNullException.ThrowIfNull(months);
        for (var m = from + 1; m < 12; m++) months[m] = months[from];
    }
}
