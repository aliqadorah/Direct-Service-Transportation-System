using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Ledger;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner request 25 Oct 2026 (DT-254…DT-258): a reference on every journal line (default = the entry's Reference; searched; on the account
/// statement) and a tax code per line — the amount is before tax, the tax (rounded to the currency) is posted as an automatic line on the same
/// side to the tax account the user chose, and the entry must balance with it. Drafts, templates, import, edit, void and reversal carry
/// both; tax codes in use cannot be deleted or change their rate; "Add the default tax accounts" for existing charts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JournalTaxTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Company(SignedInUser Owner, Dictionary<string, AccountResponse> A, Dictionary<string, TaxCodeRef> Codes);

    private async Task<Company> CompanyAsync()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var accounts = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(a => a.Code);
        var codes = (await o.Client.GetFromJsonAsync<List<TaxCodeRef>>(U(o, "/accounting/tax-codes/options")))!.ToDictionary(t => t.Code);
        return new Company(o, accounts, codes);
    }

    private static JournalLineRequest Dr(AccountResponse a, decimal v, TaxCodeRef? code = null, AccountResponse? taxAccount = null, string? reference = null) =>
        new(a.Id, v, 0, null, null, null, reference, code?.Id, taxAccount?.Id);

    private static JournalLineRequest Cr(AccountResponse a, decimal v, TaxCodeRef? code = null, AccountResponse? taxAccount = null, string? reference = null) =>
        new(a.Id, 0, v, null, null, null, reference, code?.Id, taxAccount?.Id);

    private static ManualEntryRequest Entry(string description, string? reference, params JournalLineRequest[] lines) => new(Today, description, lines, null, reference);

    private static async Task<T> OkAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<JournalEntryResponse> PostAsync(SignedInUser o, ManualEntryRequest r) =>
        await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), r));

    /// <summary>The whole problem body as text (detail and every field message), to check what the user reads.</summary>
    private static async Task<string> ProblemTextAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    // ------------------------------------------------------------ calculation and posting

    [Fact]
    public async Task A_debit_line_with_VAT_16_posts_its_tax_on_the_debit_side_to_the_chosen_tax_account()
    {
        var (o, a, codes) = await CompanyAsync();
        Assert.Equal(["VAT_JO_0", "VAT_JO_1", "VAT_JO_2", "VAT_JO_4", "VAT_JO_5", "VAT_JO_7", "VAT_JO_8", "VAT_JO_10", "VAT_JO_16"], codes.Keys);   // seeded, in order
        var e = await PostAsync(o, Entry("قرطاسية", "INV-2210", Dr(a["5299"], 100, codes["VAT_JO_16"], a["1105"]), Cr(a["110101"], 116, reference: "PAY-77")));

        Assert.Equal(3, e.Lines.Count);
        Assert.Equal((116m, 116m), (e.TotalDebit, e.TotalCredit));
        var source = e.Lines[0];
        Assert.Equal((100m, 0m, 16m, 16m, "1105", "VAT_JO_16", "INV-2210"), (source.Debit, source.Credit, source.TaxRate, source.TaxAmount, source.TaxAccount!.Code, source.TaxCode!.Code, source.Reference));
        Assert.Null(source.TaxForLineNo);
        Assert.Equal("PAY-77", e.Lines[1].Reference);
        Assert.Null(e.Lines[1].TaxCode);
        var tax = e.Lines[2];
        Assert.Equal((3, "1105", 16m, 0m, 1, "INV-2210", "VAT_JO_16"), (tax.LineNo, tax.Account.Code, tax.Debit, tax.Credit, tax.TaxForLineNo, tax.Reference, tax.TaxCode!.Code));
        Assert.Contains("VAT 16% on line 1", tax.Note, StringComparison.Ordinal);
        Assert.Contains("على السطر 1", tax.Note, StringComparison.Ordinal);
        Assert.Null(tax.TaxAmount);
    }

    [Fact]
    public async Task A_credit_line_posts_its_tax_on_the_credit_side_and_the_tax_line_keeps_the_cost_centre()
    {
        var (o, a, codes) = await CompanyAsync();
        var centre = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CC-T", "Trucks", null)));
        var e = await PostAsync(o, Entry("Other revenue", null, Dr(a["110101"], 1160),
            new JournalLineRequest(a["4102"].Id, 0, 1000, centre.Id, null, null, null, codes["VAT_JO_16"].Id, a["2104"].Id)));
        var tax = Assert.Single(e.Lines, l => l.TaxForLineNo is not null);
        Assert.Equal(("2104", 0m, 160m, 2, centre.Id), (tax.Account.Code, tax.Debit, tax.Credit, tax.TaxForLineNo, tax.CostCentre!.Id));
        Assert.Equal((1160m, 1160m), (e.TotalDebit, e.TotalCredit));
    }

    [Fact]
    public async Task The_entry_must_balance_including_the_tax_lines_and_the_message_says_so()
    {
        var (o, a, codes) = await CompanyAsync();
        var r = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry("No tax on the other side", null, Dr(a["5299"], 100, codes["VAT_JO_16"], a["1105"]), Cr(a["110101"], 100)));
        Assert.Equal("journal.unbalancedWithTax", await r.ProblemCodeAsync());
        var text = await ProblemTextAsync(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            Entry("No tax on the other side", null, Dr(a["5299"], 100, codes["VAT_JO_16"], a["1105"]), Cr(a["110101"], 100))));
        Assert.Contains("116.000", text, StringComparison.Ordinal);
        Assert.Contains("16.000", text, StringComparison.Ordinal);
        Assert.Contains("same side", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_zero_rate_code_needs_no_tax_account_and_makes_no_tax_line()
    {
        var (o, a, codes) = await CompanyAsync();
        var e = await PostAsync(o, Entry("Exempt", null, Dr(a["5299"], 100, codes["VAT_JO_0"]), Cr(a["110101"], 100)));
        Assert.Equal(2, e.Lines.Count);
        Assert.Equal(("VAT_JO_0", 0m, 0m), (e.Lines[0].TaxCode!.Code, e.Lines[0].TaxRate, e.Lines[0].TaxAmount));
        Assert.Null(e.Lines[0].TaxAccount);
    }

    [Fact]
    public async Task A_taxed_line_needs_a_tax_account_the_user_chose_among_the_tax_accounts()
    {
        var (o, a, codes) = await CompanyAsync();
        var missing = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry("x", null, Dr(a["5299"], 100, codes["VAT_JO_16"]), Cr(a["110101"], 116)));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var body = await missing.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("errors")[0];
        Assert.Equal(("lines[0].taxAccountId", "journal.taxAccountRequired"), (error.GetProperty("field").GetString(), error.GetProperty("code").GetString()));
        Assert.Contains("Line 1: choose the tax account for tax code VAT_JO_16", body.ToString(), StringComparison.Ordinal);

        var notTax = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry("x", null, Dr(a["5299"], 100, codes["VAT_JO_16"], a["5202"]), Cr(a["110101"], 116)));
        Assert.Equal("journal.taxAccountInvalid", await notTax.ErrorCodeAsync());

        // Only accounts marked as tax accounts are offered, and a control account cannot be one.
        var options = await o.Client.GetFromJsonAsync<List<AccountOption>>(U(o, "/accounting/accounts/options"));
        Assert.Equal(["1105", "2104", "5207"], options!.Where(x => x.IsTaxAccount).Select(x => x.Account.Code).Order(StringComparer.Ordinal));
        var control = a["110302"];
        var marked = await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{control.Id}"), new AccountRequest(control.Code, control.NameAr, control.NameEn, control.AccountType,
            control.ParentId, control.IsPostable, control.IsControlAccount, control.ControlPartyType, control.RowVersion, IsTaxAccount: true));
        Assert.Equal("account.taxControl", await marked.ErrorCodeAsync());
    }

    [Theory]
    [InlineData("33.333", "VAT_JO_7", "2.333")]   // 2.33331
    [InlineData("0.125", "VAT_JO_10", "0.013")]   // 0.0125 → half-up
    [InlineData("10.005", "VAT_JO_5", "0.500")]   // 0.50025
    [InlineData("1234.567", "VAT_JO_16", "197.531")]   // 197.53072
    public async Task Tax_is_rounded_half_up_to_the_currency_decimals(string amountText, string code, string taxText)
    {
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var tax = decimal.Parse(taxText, CultureInfo.InvariantCulture);
        var (o, a, codes) = await CompanyAsync();
        var e = await PostAsync(o, Entry("Rounding", null, Dr(a["5299"], amount, codes[code], a["1105"]), Cr(a["110101"], amount + tax)));
        Assert.Equal(tax, e.Lines[0].TaxAmount);
        Assert.Equal(tax, e.Lines.Single(l => l.TaxForLineNo == 1).Debit);
    }

    // ------------------------------------------------------------ tax codes

    [Fact]
    public async Task A_tax_code_used_on_a_posted_entry_cannot_be_deleted_or_change_its_rate_but_can_be_deactivated()
    {
        var (o, a, _) = await CompanyAsync();
        var created = await OkAsync<TaxCodeResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/tax-codes"), new TaxCodeRequest("VAT_TEST_3", 3, "ضريبة 3%", "VAT 3%", null, null)));
        Assert.Equal("Conflict", (await o.Client.PostAsJsonAsync(U(o, "/accounting/tax-codes"), new TaxCodeRequest("VAT_TEST_3", 4, "x", null, null, null))).StatusCode.ToString());
        Assert.Equal("validation.range", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/tax-codes"), new TaxCodeRequest("BAD", 101, "x", null, null, null))).ErrorCodeAsync());
        var code = new TaxCodeRef(created.Id, created.Code, created.Rate, created.NameAr, created.NameEn);

        // Unused: the rate can change.
        created = await OkAsync<TaxCodeResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/tax-codes/{created.Id}"), new TaxCodeRequest("VAT_TEST_3", 3.5m, "ضريبة 3.5%", "VAT 3.5%", 5, created.RowVersion)));
        Assert.Equal((3.5m, 5, false), (created.Rate, created.SortOrder, created.InUse));

        await PostAsync(o, Entry("Used", null, Dr(a["5299"], 100, code, a["1105"]), Cr(a["110101"], 103.5m)));
        var list = await o.Client.GetFromJsonAsync<List<TaxCodeResponse>>(U(o, "/accounting/tax-codes"));
        created = list!.Single(t => t.Id == created.Id);
        Assert.True(created.InUse);

        var rate = await o.Client.PutAsJsonAsync(U(o, $"/accounting/tax-codes/{created.Id}"), new TaxCodeRequest("VAT_TEST_3", 4, created.NameAr, created.NameEn, 5, created.RowVersion));
        Assert.Equal("taxCode.inUse", await rate.ErrorCodeAsync());
        Assert.Contains("Deactivate it and create a new code", await rate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var delete = await o.Client.PostAsJsonAsync(U(o, $"/accounting/tax-codes/{created.Id}/delete"), new DeleteRequest("not needed"));
        Assert.Equal("taxCode.inUse", await delete.ErrorCodeAsync());

        // The descriptions may still change; deactivated, it is no longer offered or posted, and the posted entry keeps it.
        created = await OkAsync<TaxCodeResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/tax-codes/{created.Id}"), new TaxCodeRequest("VAT_TEST_3", 3.5m, "ضريبة 3.5% قديمة", "VAT 3.5% (old)", 5, created.RowVersion)));
        created = await OkAsync<TaxCodeResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/tax-codes/{created.Id}/deactivate"), new StatusChangeRequest(created.RowVersion)));
        Assert.Equal("Archived", created.Status);
        Assert.DoesNotContain(await o.Client.GetFromJsonAsync<List<TaxCodeRef>>(U(o, "/accounting/tax-codes/options")) ?? [], t => t.Id == created.Id);
        var inactive = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry("x", null, Dr(a["5299"], 100, code, a["1105"]), Cr(a["110101"], 103.5m)));
        Assert.Equal("journal.taxCodeInactive", await inactive.ErrorCodeAsync());

        // An unused code is deleted with a reason.
        var spare = await OkAsync<TaxCodeResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/tax-codes"), new TaxCodeRequest("SPARE", 9, "احتياطي", null, null, null)));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/tax-codes/{spare.Id}/delete"), new DeleteRequest("typo"))).EnsureOkAsync();
        Assert.DoesNotContain(await o.Client.GetFromJsonAsync<List<TaxCodeResponse>>(U(o, "/accounting/tax-codes")) ?? [], t => t.Id == spare.Id);
    }

    // ------------------------------------------------------------ line reference

    [Fact]
    public async Task A_line_without_its_own_reference_takes_the_entrys_and_line_references_are_searched_and_shown_on_the_statement()
    {
        var (o, a, _) = await CompanyAsync();
        var e = await PostAsync(o, Entry("Rent", "HDR-551", Dr(a["5202"], 50), Cr(a["110101"], 50, reference: "CHQ-90817")));
        Assert.Equal(["HDR-551", "CHQ-90817"], e.Lines.Select(l => l.Reference));

        var found = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?search=90817"));
        Assert.Equal([e.Number], found!.Items.Select(x => x.Number));
        var search = await o.Client.GetFromJsonAsync<SearchResponse>(U(o, "/search?q=CHQ-90817"));
        var hit = Assert.Single(search!.Hits, h => h.Type == "journal-entry");
        Assert.Contains("CHQ-90817", hit.Detail, StringComparison.Ordinal);

        var statement = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["110101"].Id}&from={D(Today)}&to={D(Today)}"));
        Assert.Equal("CHQ-90817", statement!.Lines.Items.Single(l => l.Entry.Number == e.Number).Reference);
        var filtered = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&from={D(Today)}&to={D(Today)}&reference=HDR-551"));
        Assert.Equal("HDR-551", Assert.Single(filtered!.Lines.Items).Reference);
        var none = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&from={D(Today)}&to={D(Today)}&reference=CHQ-90817"));
        Assert.Empty(none!.Lines.Items);
        var several = await o.Client.GetFromJsonAsync<AccountStatementsReport>(U(o, $"/accounting/reports/account-statements?accounts={a["110101"].Id}&from={D(Today)}&to={D(Today)}&reference=90817"));
        Assert.Equal("CHQ-90817", Assert.Single(several!.Sections.Single().Lines).Reference);

        var tooLong = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry("x", null, Dr(a["5202"], 1, reference: new string('r', 101)), Cr(a["110101"], 1)));
        Assert.Equal("validation.range", await tooLong.ErrorCodeAsync());
    }

    // ------------------------------------------------------------ edit, void, reversal

    [Fact]
    public async Task Void_and_edit_reverse_the_tax_lines_with_the_references_and_the_edit_posts_the_tax_again()
    {
        var (o, a, codes) = await CompanyAsync();
        var e = await PostAsync(o, Entry("Fuel", "F-1", Dr(a["5103"], 200, codes["VAT_JO_16"], a["1105"]), Cr(a["110101"], 232, reference: "BOX")));

        // Edit = reverse + re-post: the corrected entry has its new tax line.
        var edited = await OkAsync<JournalEntryResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-entries/{e.Id}"),
            new ManualEntryRequest(Today, "Fuel", [Dr(a["5103"], 300, codes["VAT_JO_16"], a["1105"]), Cr(a["110101"], 348, reference: "BOX")], e.RowVersion, "F-1")));
        Assert.Equal(48m, edited.Lines.Single(l => l.TaxForLineNo == 1).Debit);
        var original = await o.Client.GetFromJsonAsync<JournalEntryResponse>(U(o, $"/accounting/journal-entries/{e.Id}"));
        var reversal = await o.Client.GetFromJsonAsync<JournalEntryResponse>(U(o, $"/accounting/journal-entries/{original!.ReversedByEntry!.Id}"));
        Assert.Equal(3, reversal!.Lines.Count);
        var mirroredTax = reversal.Lines.Single(l => l.TaxForLineNo is not null);
        Assert.Equal(("1105", 0m, 32m, 1, "F-1"), (mirroredTax.Account.Code, mirroredTax.Debit, mirroredTax.Credit, mirroredTax.TaxForLineNo, mirroredTax.Reference));
        Assert.Equal((0m, 200m, 32m, "VAT_JO_16"), (reversal.Lines[0].Debit, reversal.Lines[0].Credit, reversal.Lines[0].TaxAmount, reversal.Lines[0].TaxCode!.Code));
        Assert.Equal("BOX", reversal.Lines[1].Reference);

        // Void: the mirror entry nets the input VAT to zero.
        var voided = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-entries/{edited.Id}/void"), new VoidRequest("wrong supplier", edited.RowVersion)));
        Assert.Equal("Void", voided.Status);
        var statement = await o.Client.GetFromJsonAsync<LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["1105"].Id}&from={D(Today)}&to={D(Today)}"));
        Assert.Equal(0m, statement!.Closing);
        Assert.Equal(4, statement.Lines.Items.Count);
    }

    // ------------------------------------------------------------ drafts, templates, import

    [Fact]
    public async Task A_draft_keeps_line_references_and_tax_shows_totals_with_tax_and_posts_the_tax_line()
    {
        var (o, a, codes) = await CompanyAsync();
        var draft = await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            Entry("Draft with tax", "D-1", Dr(a["5299"], 100, codes["VAT_JO_16"], a["1105"], "LR-1"), Cr(a["110101"], 116))));
        Assert.Equal((116m, 116m), (draft.TotalDebit, draft.TotalCredit));
        Assert.Equal(("LR-1", "VAT_JO_16", "1105"), (draft.Lines[0].Reference, draft.Lines[0].TaxCode!.Code, draft.Lines[0].TaxAccount!.Code));
        var listed = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?status=Draft&search=LR-1"));
        Assert.Single(listed!.Items);

        var posted = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(draft.RowVersion)));
        Assert.Equal(3, posted.Lines.Count);
        Assert.Equal(("LR-1", 16m), (posted.Lines[2].Reference, posted.Lines[2].Debit));
    }

    [Fact]
    public async Task A_template_keeps_line_references_tax_codes_and_tax_accounts()
    {
        var (o, a, codes) = await CompanyAsync();
        var t = await OkAsync<JournalTemplateResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-templates"), new JournalTemplateRequest("Supplies", "Supplies", "S-REF",
            [new JournalTemplateLineRequest(a["5299"].Id, 100, 0, null, null, "LINE-A", codes["VAT_JO_16"].Id, a["1105"].Id), new JournalTemplateLineRequest(a["110101"].Id, 0, 116, null, null)])));
        var read = await o.Client.GetFromJsonAsync<JournalTemplateResponse>(U(o, $"/accounting/journal-templates/{t.Id}"));
        Assert.Equal(("LINE-A", "VAT_JO_16", "1105"), (read!.Lines[0].Reference, read.Lines[0].TaxCode!.Code, read.Lines[0].TaxAccount!.Code));
        Assert.Null(read.Lines[1].TaxCode);
    }

    private static async Task<HttpResponseMessage> ImportAsync(SignedInUser o, string csv, bool preview) =>
        await o.Client.PostAsync(U(o, preview ? "/accounting/journal-entries/import/preview" : "/accounting/journal-entries/import"),
            new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "je.csv" } });

    [Fact]
    public async Task Journal_import_reads_line_reference_tax_code_and_tax_account_and_explains_row_errors_with_field_labels()
    {
        var (o, _, _) = await CompanyAsync();
        var d = D(Today);
        var good = $"""
            EntryRef,Date,Description,Reference,AccountCode,Debit,Credit,LineReference,TaxCode,TaxAccount
            A,{d},Supplies,IMP-1,5299,100,,BILL-7,VAT_JO_16,1105
            A,{d},Supplies,IMP-1,110101,,116,,,
            """;
        await (await ImportAsync(o, good, preview: false)).EnsureOkAsync();
        var list = await o.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(o, "/accounting/journal-entries?search=BILL-7"));
        var entry = await o.Client.GetFromJsonAsync<JournalEntryResponse>(U(o, $"/accounting/journal-entries/{Assert.Single(list!.Items).Id}"));
        Assert.Equal(["BILL-7", "IMP-1", "BILL-7"], entry!.Lines.Select(l => l.Reference));
        Assert.Equal(("1105", 16m, 1), (entry.Lines[2].Account.Code, entry.Lines[2].Debit, entry.Lines[2].TaxForLineNo));

        var bad = $"""
            EntryRef,Date,Description,AccountCode,Debit,Credit,TaxCode,TaxAccount
            B,{d},x,5299,100,,NOPE,1105
            B,{d},x,110101,,100,,
            C,{d},y,5299,100,,VAT_JO_16,
            C,{d},y,110101,,116,,
            E,{d},z,5299,100,,VAT_JO_16,1105
            E,{d},z,110101,,100,,
            """;
        var preview = await OkAsync<ImportPreview>(await ImportAsync(o, bad, preview: true));
        string Problems(int row) => string.Join(" | ", preview.Rows.Single(r => r.Row == row).Problems);
        Assert.Contains("Unknown Tax code: «NOPE»", Problems(2), StringComparison.Ordinal);
        Assert.Contains("choose the tax account for tax code VAT_JO_16", Problems(4), StringComparison.Ordinal);
        Assert.Contains("does not balance including tax", Problems(6), StringComparison.Ordinal);
        Assert.Contains("116.000", Problems(6), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ default tax accounts

    [Fact]
    public async Task Add_the_default_tax_accounts_marks_the_template_ones_and_adds_the_missing_one_with_the_next_free_code()
    {
        var (o, a, _) = await CompanyAsync();
        // Nothing to do on a chart made from the new template.
        Assert.Equal((0, 0), await DefaultsAsync(o));

        // An older chart: no tax flags, no 5207, and 5207 taken by an unrelated account.
        foreach (var code in new[] { "1105", "2104" })
        {
            var x = a[code];
            await (await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{x.Id}"), new AccountRequest(x.Code, x.NameAr, x.NameEn, x.AccountType, x.ParentId, x.IsPostable,
                x.IsControlAccount, x.ControlPartyType, x.RowVersion, IsTaxAccount: false))).EnsureOkAsync();
        }
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/accounts/{a["5207"].Id}/delete"), new DeleteRequest("older chart"))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts"), new AccountRequest("5207", "تدريب", "Training", "Expense", a["52"].Id, true, false, null, null))).EnsureOkAsync();

        var r = await OkAsync<DefaultTaxAccountsResult>(await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/default-tax-accounts"), new { }));
        Assert.Equal((1, 2), (r.Added, r.Marked));
        var chart = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(x => x.Code);
        Assert.Equal(["1105", "2104", "5208"], chart.Values.Where(x => x.IsTaxAccount).Select(x => x.Code).Order(StringComparer.Ordinal));
        Assert.Equal(("52", "VAT expenses (non-recoverable)", "Expense", true), (chart["5208"].ParentCode, chart["5208"].NameEn, chart["5208"].AccountType, chart["5208"].IsPostable));
        Assert.False(chart["5207"].IsTaxAccount);   // the unrelated account keeps its meaning
        Assert.Equal((0, 0), await DefaultsAsync(o));
    }

    private static async Task<(int Added, int Marked)> DefaultsAsync(SignedInUser o)
    {
        var r = await OkAsync<DefaultTaxAccountsResult>(await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/default-tax-accounts"), new { }));
        return (r.Added, r.Marked);
    }
}
