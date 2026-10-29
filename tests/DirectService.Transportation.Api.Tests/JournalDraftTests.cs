using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 item 3 (owner change request 15 Oct 2026): journal entry Reference / المرجع (list, view, statement, search, exports, import)
/// and "Save as draft" (no JV number, not posted; edit, delete, post — number and period lock at posting, numbering stays gap-free).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JournalDraftTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(SignedInUser Owner, Dictionary<string, AccountResponse> A)> CompanyAsync()
    {
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        return (owner, (await owner.Client.GetFromJsonAsync<List<AccountResponse>>(U(owner, "/accounting/accounts")))!.ToDictionary(a => a.Code));
    }

    private static ManualEntryRequest Entry(DateOnly date, string description, string? reference, params JournalLineRequest[] lines) =>
        new(date, description, lines, null, reference);

    private static JournalLineRequest Dr(AccountResponse a, decimal v) => new(a.Id, v, 0, null, null, null);

    private static JournalLineRequest Cr(AccountResponse a, decimal v) => new(a.Id, 0, v, null, null, null);

    private static async Task<T> OkAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Reference_is_stored_listed_searched_shown_in_the_statement_and_exported()
    {
        var (o, a) = await CompanyAsync();
        var entry = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry(Today, "إيجار المكتب", "INV-7781 مورد", Dr(a["5202"], 100), Cr(a["110101"], 100))));
        Assert.Equal("INV-7781 مورد", entry.Reference);
        Assert.Equal("INV-7781 مورد", (await o.Client.GetFromJsonAsync<JournalEntryResponse>(U(o, $"/accounting/journal-entries/{entry.Id}")))!.Reference);
        Assert.Equal("INV-7781 مورد", (await o.Client.GetFromJsonAsync<JournalVoucherResponse>(U(o, $"/accounting/journal-entries/{entry.Id}/voucher")))!.Reference);
        await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry(Today, "اتصالات", null, Dr(a["5203"], 20), Cr(a["110101"], 20))));

        var list = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries"));
        Assert.Contains(list!.Items, e => e.Reference == "INV-7781 مورد");
        var found = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?search=7781"));
        Assert.Equal([entry.Number], found!.Items.Select(e => e.Number));
        var byNumber = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, $"/accounting/journal-entries?search={entry.Number}"));
        Assert.Equal([entry.Number], byNumber!.Items.Select(e => e.Number));

        var statement = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&from={D(Today)}&to={D(Today)}"));
        Assert.Equal("INV-7781 مورد", statement!.Lines.Items.Single().Reference);

        var csv = Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, "/accounting/journal-entries/export?format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("INV-7781 مورد", csv, StringComparison.Ordinal);
        Assert.Contains("Reference", csv, StringComparison.Ordinal);   // English client

        var tooLong = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry(Today, "x", new string('r', 101), Dr(a["5202"], 1), Cr(a["110101"], 1)));
        Assert.Equal("validation.range", await tooLong.ErrorCodeAsync());
    }

    [Fact]
    public async Task Journal_import_reads_the_reference_column()
    {
        var (o, _) = await CompanyAsync();
        var d = D(Today);
        var csv = $"""
            EntryRef,Date,Description,Reference,AccountCode,Debit,Credit
            A,{d},إيجار,REF-55,5202,100,
            A,{d},إيجار,REF-55,110101,,100
            """;
        var r = await o.Client.PostAsync(U(o, "/accounting/journal-entries/import"),
            new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "je.csv" } });
        await r.EnsureOkAsync();
        var list = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries"));
        Assert.Equal("REF-55", list!.Items.Single().Reference);

        var template = await o.Client.GetAsync(U(o, "/accounting/journal-entries/import/template"));
        await template.EnsureOkAsync();
        Assert.True((await template.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task A_draft_takes_no_number_and_stays_out_of_balances_until_posted()
    {
        var (o, a) = await CompanyAsync();
        var first = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry(Today, "أول", null, Dr(a["5203"], 10), Cr(a["110101"], 10))));

        // Incomplete drafts are kept (no account on the second line, unbalanced).
        var draft = await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            new ManualEntryRequest(Today, "مسودة إيجار", [Dr(a["5202"], 250), new JournalLineRequest(null, 0, 200, null, null, null)], null, "R-1")));
        Assert.Equal(2, draft.Lines.Count);
        Assert.Null(draft.Lines[1].Account);

        // Listed first and under the Draft filter, without a number; not in the trial balance or the statement.
        var all = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries"));
        Assert.Equal(("Draft", string.Empty, "R-1"), (all!.Items[0].Status, all.Items[0].Number, all.Items[0].Reference));
        Assert.Equal(2, all.TotalCount);
        var drafts = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft"));
        Assert.Equal([draft.Id], drafts!.Items.Select(e => e.Id));
        var posted = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Posted"));
        Assert.DoesNotContain(posted!.Items, e => e.Status == "Draft");
        var statement = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&from={D(Today)}&to={D(Today)}"));
        Assert.Empty(statement!.Lines.Items);
        var tb = await o.Client.GetFromJsonAsync<TrialBalanceReport>(U(o, $"/accounting/reports/trial-balance?from={D(Today)}&to={D(Today)}"));
        Assert.Equal(10m, tb!.Totals.MovementDebit);

        // Posting refuses the incomplete draft and keeps it.
        var refused = await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(draft.RowVersion));
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal(1, (await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft")))!.TotalCount);

        // Edited, then posted: the next JV number (gap-free), the draft is gone, the entry carries the reference.
        var edited = await OkAsync<JournalDraftResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}"),
            new ManualEntryRequest(Today, "مسودة إيجار", [Dr(a["5202"], 250), Cr(a["110101"], 250)], draft.RowVersion, "R-1")));
        var entry = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(edited.RowVersion)));
        Assert.Equal(NextNumber(first.Number), entry.Number);
        Assert.Equal(("R-1", "Posted"), (entry.Reference, entry.Status));
        Assert.Equal(0, (await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft")))!.TotalCount);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, $"/accounting/journal-drafts/{draft.Id}"))).StatusCode);
        var after = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&from={D(Today)}&to={D(Today)}"));
        Assert.Equal(250m, after!.Lines.Items.Single().Debit);
    }

    [Fact]
    public async Task Posting_a_draft_checks_the_period_lock_then_and_deleting_takes_no_number()
    {
        var (o, a) = await CompanyAsync();
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var first = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry(Today, "أول", null, Dr(a["5203"], 10), Cr(a["110101"], 10))));
        var draft = await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            Entry(lastMonth.AddDays(3), "قيد شهر سابق", null, Dr(a["5202"], 40), Cr(a["110101"], 40))));
        // The period closes after the draft was saved: the lock applies at posting.
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/periods/{lastMonth:yyyy-MM}/close"), new PeriodActionRequest("إقفال", true))).EnsureOkAsync();
        var post = await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(draft.RowVersion));
        Assert.Equal("period.closed", await post.ProblemCodeAsync());
        Assert.Equal(1, (await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft")))!.TotalCount);

        var current = await o.Client.GetFromJsonAsync<JournalDraftResponse>(U(o, $"/accounting/journal-drafts/{draft.Id}"));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/delete"), new RowVersionRequest(current!.RowVersion))).EnsureOkAsync();
        Assert.Equal(0, (await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft")))!.TotalCount);

        // No number was used by the draft or the refused posting.
        var next = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry(Today, "ثان", null, Dr(a["5203"], 5), Cr(a["110101"], 5))));
        Assert.Equal(NextNumber(first.Number), next.Number);
        Assert.Equal(0, await f.DbAsync(db => db.Set<JournalDraft>().CountAsync()));   // soft-deleted: hidden by the filter
    }

    [Fact]
    public async Task Drafts_belong_to_their_company_and_need_acct_post_to_change()
    {
        var (o, a) = await CompanyAsync();
        var draft = await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            Entry(Today, "سري", null, Dr(a["5202"], 1), Cr(a["110101"], 1))));
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/accounting/journal-drafts/{draft.Id}"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(U(other, $"/accounting/journal-drafts/{draft.Id}/post"),
            new RowVersionRequest(draft.RowVersion))).StatusCode);
        var empty = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"), new ManualEntryRequest(null, null, [], null));
        Assert.Equal("validation.required", await empty.ErrorCodeAsync());
    }

    private static string NextNumber(string number)
    {
        var digits = new string(number.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        var next = (int.Parse(digits, CultureInfo.InvariantCulture) + 1).ToString(new string('0', digits.Length), CultureInfo.InvariantCulture);
        return number[..^digits.Length] + next;
    }
}
