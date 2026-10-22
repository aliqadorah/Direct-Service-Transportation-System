using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Accounting;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// DT-240 / DT-241: the messages of real refusals reach the screen with the field's label, never its internal name ("entryIds",
/// "lines[0].accountId").
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class FieldMessageTests(ApiFactory f)
{
    [GeneratedRegex(@"\b[a-z]+[A-Z][A-Za-z0-9]*\b|\b[A-Za-z]+\[\d+\]|\b[a-z]+\.[a-z][A-Za-z]*\b")]
    private static partial Regex RawName();

    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    /// <summary>The detail and every field message of a problem answer.</summary>
    private static async Task<List<string>> MessagesAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var p = await r.Content.ReadFromJsonAsync<JsonElement>();
        var list = new List<string> { p.GetProperty("detail").GetString()! };
        if (p.TryGetProperty("errors", out var errors)) list.AddRange(errors.EnumerateArray().Select(e => e.GetProperty("message").GetString()!));
        return list;
    }

    [Fact]
    public async Task Maintenance_preview_without_entries_names_the_selection_not_entryIds()
    {
        var w = await ApArTests.WorldAsync(f);
        var o = w.Owner;
        var messages = await MessagesAsync(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-entries/preview"),
            new MaintenanceRequest(null, null, [], null)));

        Assert.All(messages, m => Assert.DoesNotMatch(RawName(), m));
        Assert.Contains(messages, m => m.Contains("القيود المختارة", StringComparison.Ordinal) || m.Contains("Selected entries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_journal_line_without_an_account_is_named_by_its_line_number()
    {
        var w = await ApArTests.WorldAsync(f);
        var o = w.Owner;
        var cash = w.Accounts["110101"];
        var messages = await MessagesAsync(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(
            DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1), "Fuel", [new JournalLineRequest(cash.Id, 10m, 0, null, null, null), new JournalLineRequest(null, 0, 10m, null, null, null)], null)));

        Assert.All(messages, m => Assert.DoesNotMatch(RawName(), m));
        Assert.Contains(messages, m => m.Contains("السطر 2 · الحساب", StringComparison.Ordinal) || m.Contains("Line 2 · Account", StringComparison.Ordinal));
    }
}
