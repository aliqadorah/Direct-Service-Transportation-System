using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// Pick lists of the trip screens (FR-167: active records only): customers, materials, loading and unloading sites and partners are
/// loaded once per company; drivers are searched on the server by name or plate and show truck, capacity and fleet.
/// </summary>
public sealed class TripLookups(Api api, ShellState shell)
{
    private string? _company;
    private Task? _loading;

    public IReadOnlyList<Option<string?>> Customers { get; private set; } = [];
    public IReadOnlyList<Option<string?>> Materials { get; private set; } = [];
    public IReadOnlyList<Option<string?>> LoadingSites { get; private set; } = [];
    public IReadOnlyList<Option<string?>> UnloadingSites { get; private set; } = [];
    public IReadOnlyList<Option<string?>> Partners { get; private set; } = [];

    /// <summary>Drivers last returned by a search, by id (truck, capacity, fleet class, partner, document alerts).</summary>
    public Dictionary<string, DriverOption> Drivers { get; } = new(StringComparer.Ordinal);

    public Task EnsureAsync()
    {
        if (_company != shell.Company?.Id) { _company = shell.Company?.Id; _loading = null; Drivers.Clear(); }
        return _loading ??= LoadOnceAsync();
    }

    /// <summary>A failed load is tried again by the next screen (review M3 of step 8).</summary>
    private async Task LoadOnceAsync()
    {
        try { await LoadAsync().ConfigureAwait(false); }
        catch
        {
            _loading = null;
            throw;
        }
    }

    /// <summary>Reloads after master data changed in another screen.</summary>
    public void Invalidate() => _loading = null;

    private async Task LoadAsync()
    {
        static IReadOnlyList<Option<string?>> Opt(IReadOnlyList<MasterOption> o) => o.Select(x => new Option<string?>(x.Id, x.Name)).ToList();
        Customers = Opt(await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("customers/options")).ConfigureAwait(false));
        Materials = Opt(await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("materials/options")).ConfigureAwait(false));
        LoadingSites = Opt(await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("loading-sites/options")).ConfigureAwait(false));
        UnloadingSites = Opt(await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("unloading-sites/options")).ConfigureAwait(false));
        Partners = Opt(await api.GetAsync<IReadOnlyList<MasterOption>>(api.C("partners/options")).ConfigureAwait(false));
    }

    /// <summary>Driver search for a combo box: "name" with "plate · capacity t" as the secondary text.</summary>
    public async Task<IReadOnlyList<Option<string?>>> SearchDriversAsync(string text, CancellationToken ct)
    {
        var list = await api.GetAsync<IReadOnlyList<DriverOption>>(api.C("drivers/options?q=" + Uri.EscapeDataString(text ?? "")), ct).ConfigureAwait(false);
        foreach (var d in list) Drivers[d.Id] = d;
        return list.Select(d => new Option<string?>(d.Id, d.Name, $"{d.PlateNumber} · {Fmt.Qty(d.CapacityTons, 0)} t")).ToList();
    }

    public Option<string?>? DriverOption(string? id, string? name = null, string? plate = null) =>
        id is null ? null : Drivers.TryGetValue(id, out var d) ? new Option<string?>(d.Id, d.Name, $"{d.PlateNumber} · {Fmt.Qty(d.CapacityTons, 0)} t")
            : name is null ? null : new Option<string?>(id, name, plate);
}
