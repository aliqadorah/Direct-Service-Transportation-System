using System.Globalization;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Identity;

namespace DirectService.Transportation.Api.Tests;

/// <summary>Shared steps of the revision 6 part B tests (budgets, journal templates, accounting dashboard).</summary>
internal static class PartBSupport
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    public static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<T> OkAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>A new tenant whose company has the transport chart template; accounts by code.</summary>
    public static async Task<(SignedInUser Owner, Dictionary<string, AccountResponse> A)> CompanyAsync(ApiFactory f)
    {
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        return (owner, (await owner.Client.GetFromJsonAsync<List<AccountResponse>>(U(owner, "/accounting/accounts")))!.ToDictionary(a => a.Code));
    }

    /// <summary>Posts a manual entry (debit one account, credit another).</summary>
    public static async Task<JournalEntryResponse> PostAsync(SignedInUser o, DateOnly date, AccountResponse debit, AccountResponse credit, decimal amount,
        string? costCentre = null, string description = "Test entry") =>
        await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            new ManualEntryRequest(date, description, [new JournalLineRequest(debit.Id, amount, 0, costCentre, null, null), new JournalLineRequest(credit.Id, 0, amount, costCentre, null, null)], null)));

    /// <summary>
    /// A user of the owner's company with exactly <paramref name="permissions"/> (a custom role), created with a username and password and
    /// signed in (no second factor for a custom role).
    /// </summary>
    public static async Task<HttpClient> MemberAsync(ApiFactory f, SignedInUser owner, params string[] permissions)
    {
        var role = await OkAsync<RoleResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/roles"), new RoleRequest("R " + Guid.NewGuid().ToString("N")[..8], permissions, null)));
        var userName = "m." + Guid.NewGuid().ToString("N")[..10];
        await (await owner.Client.PostAsJsonAsync(U(owner, "/users"), new CreateUserRequest("Member", userName, null, ApiHelpers.Password, role.Id, false))).EnsureOkAsync();
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName));
        Assert.Equal("done", (await OkAsync<SignInResponse>(r)).Next);
        await client.UseAntiforgeryAsync();
        return client;
    }
}
