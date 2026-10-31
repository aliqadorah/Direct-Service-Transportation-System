using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;
using DirectService.Shared.UI.Services;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// Pick lists of the cash documents (receipts, advances, settlements, partner and AP/AR payments): active cash boxes and bank accounts
/// (<c>GET /accounting/money-accounts/options</c>), loaded once per company. Cash uses a cash box; bank transfer and CliQ use a bank account.
/// </summary>
public sealed class MoneyAccounts(Api api, ShellState shell)
{
    private string? _company;
    private Task? _loading;

    public IReadOnlyList<Option<string?>> CashBoxes { get; private set; } = [];
    public IReadOnlyList<Option<string?>> BankAccounts { get; private set; } = [];

    /// <summary>Today in the company's time zone (default date of cash documents).</summary>
    public DateOnly? Today { get; private set; }

    public Task EnsureAsync()
    {
        if (_company != shell.Company?.Id)
        {
            _company = shell.Company?.Id;
            _loading = null;
            (CashBoxes, BankAccounts, Today) = ([], [], null);   // never the previous company's accounts (DT-187)
        }
        return _loading ??= LoadAsync();
    }

    /// <summary>Reloads after cash boxes or bank accounts changed.</summary>
    public void Invalidate() => _loading = null;

    private async Task LoadAsync()
    {
        var company = _company;
        MoneyAccountOptions o;
        try { o = await api.GetAsync<MoneyAccountOptions>(api.C("accounting/money-accounts/options")).ConfigureAwait(false); }
        catch
        {
            if (company == _company) _loading = null;   // a failed load is tried again by the next form (not the new company's load, DT-187)
            throw;
        }
        if (company != _company) return;   // the company changed meanwhile
        Today = o.Today;
        CashBoxes = o.CashBoxes.Select(x => new Option<string?>(x.Id, x.Name)).ToList();
        BankAccounts = o.BankAccounts.Select(x => new Option<string?>(x.Id, x.Name)).ToList();
    }

    /// <summary>The accounts that fit a payment method: cash boxes for Cash, bank accounts otherwise.</summary>
    public IReadOnlyList<Option<string?>> For(string? method) => method == CashCodes.Cash ? CashBoxes : BankAccounts;

    /// <summary>The payment methods of the cash documents (7.4).</summary>
    public static IReadOnlyList<Option<string?>> Methods(DirectService.Shared.UI.Localization.Loc l) =>
        [new(CashCodes.Cash, l["pay.Cash"]), new(CashCodes.Bank, l["pay.Bank"]), new(CashCodes.CliQ, l["pay.CliQ"])];
}
