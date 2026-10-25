using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// Pick lists of the accounting screens, loaded once per company: active postable accounts (with type and control-party type, FR-106),
/// active cost centres (FR-131), accounting parties by role (FR-122) and the active tax codes of journal lines (DT-257).
/// </summary>
public sealed class AccountingLookups(Api api, ShellState shell, Loc loc)
{
    private string? _company;
    private Task? _loading;
    private readonly Dictionary<string, IReadOnlyList<ApArPartyOption>> _parties = new(StringComparer.Ordinal);

    public IReadOnlyList<AccountOption> Accounts { get; private set; } = [];
    public IReadOnlyList<Option<string?>> CostCentres { get; private set; } = [];
    public IReadOnlyList<TaxCodeRef> TaxCodes { get; private set; } = [];

    public Task EnsureAsync()
    {
        if (_company != shell.Company?.Id)
        {
            _company = shell.Company?.Id;
            _loading = null;
            _parties.Clear();
            (Accounts, CostCentres, TaxCodes) = ([], [], []);   // never the previous company's lists (DT-187)
        }
        return _loading ??= LoadAsync();
    }

    /// <summary>Reloads after the chart or the cost centres changed.</summary>
    public void Invalidate()
    {
        _loading = null;
        _parties.Clear();
    }

    private async Task LoadAsync()
    {
        var company = _company;
        IReadOnlyList<AccountOption> accounts;
        try { accounts = await api.GetAsync<IReadOnlyList<AccountOption>>(api.C("accounting/accounts/options")).ConfigureAwait(false); }
        catch
        {
            if (company == _company) _loading = null;   // tried again by the next screen (not the new company's load, DT-187)
            throw;
        }
        IReadOnlyList<Option<string?>> centres;
        try
        {
            var cc = await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("accounting/cost-centres/options")).ConfigureAwait(false);
            centres = cc.Select(c => new Option<string?>(c.Id, c.Name)).ToList();
        }
        catch (ApiException) { centres = []; }
        IReadOnlyList<TaxCodeRef> taxCodes;
        try { taxCodes = await api.GetAsync<IReadOnlyList<TaxCodeRef>>(api.C("accounting/tax-codes/options")).ConfigureAwait(false); }
        catch (ApiException) { taxCodes = []; }
        if (company != _company) return;   // the company changed meanwhile
        Accounts = accounts;
        CostCentres = centres;
        TaxCodes = taxCodes;
    }

    /// <summary>"code · name" in the reader's language.</summary>
    public string Name(AccountRef a) => $"{a.Code} · {loc.Pick(a.NameAr, a.NameEn)}";

    /// <summary>Accounts as combo options, optionally only some types (e.g. Revenue for receivables, Expense/Asset for bills).</summary>
    public IReadOnlyList<Option<string?>> AccountOptions(params string[] types) =>
        Accounts.Where(a => types.Length == 0 || types.Contains(a.AccountType))
            .Select(a => new Option<string?>(a.Account.Id, Name(a.Account), a.IsControlAccount ? loc["acc.controlOf"] + " " + loc["party." + a.ControlPartyType] : null)).ToList();

    public AccountOption? Account(string? id) => id is null ? null : Accounts.FirstOrDefault(a => a.Account.Id == id);

    /// <summary>The accounts marked as tax accounts (DT-256): the only choices for a journal line's tax account.</summary>
    public IReadOnlyList<Option<string?>> TaxAccountOptions() =>
        Accounts.Where(a => a.IsTaxAccount).Select(a => new Option<string?>(a.Account.Id, Name(a.Account))).ToList();

    /// <summary>Tax codes as options: "code · description (rate %)".</summary>
    public IReadOnlyList<Option<string?>> TaxCodeOptions() => TaxCodes.Select(t => new Option<string?>(t.Id, TaxName(t))).ToList();

    public string TaxName(TaxCodeRef t) => loc.Pick(t.NameAr, t.NameEn);

    public TaxCodeRef? TaxCode(string? id) => id is null ? null : TaxCodes.FirstOrDefault(t => t.Id == id);

    /// <summary>Accounting parties of a role (Supplier / Customer), transport customers included for receivables (DT-101).</summary>
    public async Task<IReadOnlyList<ApArPartyOption>> PartiesAsync(string role)
    {
        if (_parties.TryGetValue(role, out var list)) return list;
        var company = _company;
        list = await api.GetAsync<IReadOnlyList<ApArPartyOption>>(api.C("apar/parties/options?role=" + Uri.EscapeDataString(role))).ConfigureAwait(false);
        if (company == _company) _parties[role] = list;
        return list;
    }
}
