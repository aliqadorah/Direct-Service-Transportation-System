using DirectService.Transportation.Contracts.Identity;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// What the shell shows for the signed-in user: profile (language, date format), companies with their roles and permissions, the
/// current company, density (per device, DT-69), unread notifications and whether the setup checklist is still open. Filled from
/// <c>GET /api/v1/me</c> and the company endpoints (step 8); components re-render on <see cref="Changed"/>.
/// </summary>
public sealed class ShellState
{
    public MeResponse? Me { get; private set; }

    public CompanyAccess? Company { get; private set; }

    public string Density { get; private set; } = "compact";

    public int UnreadNotifications { get; private set; }

    public bool SetupIncomplete { get; private set; }

    public string? PlanText { get; private set; }

    public IReadOnlyCollection<string> Permissions => Company?.Permissions ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    public string DateFormat => Me?.DateFormat ?? Ui.Fmt.DefaultDateFormat;

    public event Action? Changed;

    public void SetMe(MeResponse? me)
    {
        Me = me;
        if (Company is not null && me is not null) Company = me.Companies.FirstOrDefault(c => c.Id == Company.Id);
        Changed?.Invoke();
    }

    /// <summary>Selects the company of the URL; false when the user has no access to it (the page shows "not found").</summary>
    public bool SelectCompany(string? token)
    {
        var c = Me?.Companies.FirstOrDefault(x => x.Id == token);
        if (c is null) return false;
        if (!ReferenceEquals(c, Company)) { Company = c; Changed?.Invoke(); }
        return true;
    }

    public void SetDensity(string density)
    {
        Density = density == "comfortable" ? "comfortable" : "compact";
        Changed?.Invoke();
    }

    public void SetNotifications(int unread)
    {
        UnreadNotifications = Math.Max(0, unread);
        Changed?.Invoke();
    }

    public void SetSetup(bool incomplete, string? planText)
    {
        SetupIncomplete = incomplete;
        PlanText = planText;
        Changed?.Invoke();
    }

    public bool Can(string permission) => Permissions.Contains(permission);
}
