using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using static DirectService.Transportation.Api.Tests.PartBSupport;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 part B, feature 2 (owner change request 15 Oct 2026): journal entry templates — save (name, description, Reference, lines with
/// accounts, cost centres, line descriptions, optional amounts), list, use to fill a new entry that is then saved as a draft or posted as
/// usual, rename / edit / delete, per company, history, permissions. Nothing is posted by a template.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JournalTemplateTests(ApiFactory f)
{
    [Fact]
    public async Task Template_is_saved_used_for_a_draft_and_a_posted_entry_renamed_and_deleted()
    {
        var (o, a) = await CompanyAsync(f);
        var cc = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("HQ", "Head office", null)));
        var request = new JournalTemplateRequest("إيجار شهري", "إيجار المكتب", "عقد 12",
        [
            new JournalTemplateLineRequest(a["5202"].Id, 500, 0, cc.Id, "إيجار الشهر"),
            new JournalTemplateLineRequest(a["110101"].Id, 0, 500, null, null),
        ]);
        var t = await OkAsync<JournalTemplateResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"), request));
        Assert.Equal(("إيجار شهري", "إيجار المكتب", "عقد 12"), (t.Name, t.Description, t.Reference));
        Assert.Equal(["5202", "110101"], t.Lines.Select(l => l.Account!.Code));
        Assert.Equal((500m, cc.Id, "إيجار الشهر"), (t.Lines[0].Debit, t.Lines[0].CostCentre!.Id, t.Lines[0].Note));
        Assert.Empty((await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries")))!.Items);   // nothing posted

        var list = (await o.Client.GetFromJsonAsync<List<JournalTemplateSummary>>(U(o, "/accounting/journal-templates")))!;
        Assert.Equal((2, true), (list.Single().Lines, list.Single().HasAmounts));

        // "Use template": the screen fills a new entry from the template; it is then saved as a draft or posted as usual.
        var used = (await o.Client.GetFromJsonAsync<JournalTemplateResponse>(U(o, $"/accounting/journal-templates/{t.Id}")))!;
        var entry = new ManualEntryRequest(Today, used.Description, used.Lines.Select(l => new JournalLineRequest(l.Account!.Id, l.Debit, l.Credit, l.CostCentre?.Id, null, l.Note)).ToList(),
            null, used.Reference);
        var draft = await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"), entry));
        Assert.Equal("عقد 12", draft.Reference);
        var posted = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), entry));
        Assert.Equal(("إيجار المكتب", "عقد 12"), (posted.Description, posted.Reference));
        Assert.Equal(cc.Id, posted.Lines.Single(l => l.Debit == 500).CostCentre!.Id);

        // A template without amounts.
        var bare = await OkAsync<JournalTemplateResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"),
            new JournalTemplateRequest("Bare", null, null, [new JournalTemplateLineRequest(a["5203"].Id, 0, 0, null, null), new JournalTemplateLineRequest(a["110101"].Id, 0, 0, null, null)])));
        Assert.All(bare.Lines, l => Assert.Equal((0m, 0m), (l.Debit, l.Credit)));
        Assert.False((await o.Client.GetFromJsonAsync<List<JournalTemplateSummary>>(U(o, "/accounting/journal-templates")))!.Single(x => x.Name == "Bare").HasAmounts);

        // Rename (and edit) by saving; the old row version is refused afterwards; the name is unique.
        var renamed = await OkAsync<JournalTemplateResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-templates/{t.Id}"), request with { Name = "Rent", RowVersion = t.RowVersion }));
        Assert.Equal("Rent", renamed.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-templates/{t.Id}"), request with { Name = "Rent 2", RowVersion = t.RowVersion })).StatusCode);
        Assert.Equal("template.nameTaken", await (await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-templates/{bare.Id}"), request with { Name = "Rent", RowVersion = bare.RowVersion })).ErrorCodeAsync());
        Assert.Equal("template.nameTaken", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"), request with { Name = "Rent" })).ErrorCodeAsync());

        var history = await o.Client.GetFromJsonAsync<List<HistoryEntry>>(U(o, $"/accounting/journal-templates/{t.Id}/history"));
        Assert.Contains(history!, h => h.Action == "Update" && h.Changes.Any(c => c.NewValue == "Rent"));

        Assert.Equal(HttpStatusCode.NoContent, (await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-templates/{t.Id}/delete"), new RowVersionRequest(renamed.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, $"/accounting/journal-templates/{t.Id}"))).StatusCode);
        Assert.Equal(["Bare"], (await o.Client.GetFromJsonAsync<List<JournalTemplateSummary>>(U(o, "/accounting/journal-templates")))!.Select(x => x.Name));
        // The posted entry made from it is unchanged.
        Assert.Equal(HttpStatusCode.OK, (await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{posted.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Template_lines_are_checked()
    {
        var (o, a) = await CompanyAsync(f);
        async Task<string> Code(JournalTemplateRequest r) => await (await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"), r)).ErrorCodeAsync();
        var line = new JournalTemplateLineRequest(a["5202"].Id, 0, 0, null, null);
        Assert.Equal("validation.required", await Code(new JournalTemplateRequest(null, null, null, [line])));
        Assert.Equal("validation.required", await Code(new JournalTemplateRequest("No lines", null, null, [])));
        Assert.Equal("validation.required", await Code(new JournalTemplateRequest("No account", null, null, [line with { AccountId = null }])));
        Assert.Equal("validation.range", await Code(new JournalTemplateRequest("Both", null, null, [line with { Debit = 1, Credit = 1 }])));
        Assert.Equal("validation.range", await Code(new JournalTemplateRequest("Negative", null, null, [line with { Debit = -1 }])));
        Assert.Equal("validation.range", await Code(new JournalTemplateRequest("Long ref", null, new string('r', 101), [line])));
        Assert.Equal("validation.format", await Code(new JournalTemplateRequest("Bad centre", null, null, [line with { CostCentreId = "nope" }])));
    }

    [Fact]
    public async Task Templates_belong_to_their_company_and_need_the_posting_permission()
    {
        var (o, a) = await CompanyAsync(f);
        var t = await OkAsync<JournalTemplateResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"),
            new JournalTemplateRequest("Mine", null, null, [new JournalTemplateLineRequest(a["5202"].Id, 0, 0, null, null)])));

        var (other, _) = await CompanyAsync(f);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/accounting/journal-templates/{t.Id}"))).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<List<JournalTemplateSummary>>(U(other, "/accounting/journal-templates")))!);

        var viewer = await MemberAsync(f, o, PermissionKeys.AcctView, PermissionKeys.JeView);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(U(o, "/accounting/journal-templates"))).StatusCode);
        var poster = await MemberAsync(f, o, PermissionKeys.AcctPost);
        Assert.Equal(["Mine"], (await poster.GetFromJsonAsync<List<JournalTemplateSummary>>(U(o, "/accounting/journal-templates")))!.Select(x => x.Name));
        // Owner decision 18 Oct 2026: the permission that creates and edits entries (and saves drafts) also saves and uses templates.
        await (await poster.PostAsJsonAsync(U(o, "/accounting/journal-templates"),
            new JournalTemplateRequest("Theirs", null, null, [new JournalTemplateLineRequest(a["5202"].Id, 0, 0, null, null)]))).EnsureOkAsync();
        await (await poster.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            new ManualEntryRequest(Today, "From a template", [new JournalLineRequest(a["5202"].Id, 10, 0, null, null, null)], null))).EnsureOkAsync();
    }
}
