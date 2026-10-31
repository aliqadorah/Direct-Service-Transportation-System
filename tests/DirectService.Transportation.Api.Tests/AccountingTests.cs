using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B2 accounting core and posting engine through the API: chart template (D-10) and tree rules (FR-107, FR-108, FR-093),
/// opening balances (FR-110), mapping (FR-092…095), manual entries (FR-111…115, D-05), periods (FR-132…134),
/// journal export (FR-104), imports (FR-109, FR-114), cash/bank/cost centres (FR-129, FR-131) and permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountingTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Signs up an Owner, applies the transport template and returns the accounts by code.</summary>
    private async Task<(SignedInUser Owner, Dictionary<string, AccountResponse> Accounts)> TemplateCompanyAsync()
    {
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        return (owner, await AccountsAsync(owner));
    }

    private static async Task<Dictionary<string, AccountResponse>> AccountsAsync(SignedInUser owner) =>
        (await owner.Client.GetFromJsonAsync<List<AccountResponse>>(U(owner, "/accounting/accounts")))!.ToDictionary(a => a.Code);

    private static ManualEntryRequest Entry(DateOnly date, string description, params JournalLineRequest[] lines) => new(date, description, lines, null);

    private static JournalLineRequest Dr(AccountResponse a, decimal amount, string? centre = null, PartyRequest? party = null) =>
        new(a.Id, amount, 0, centre, party, null);

    private static JournalLineRequest Cr(AccountResponse a, decimal amount, PartyRequest? party = null) => new(a.Id, 0, amount, null, party, null);

    private static async Task<JournalEntryResponse> PostEntryAsync(SignedInUser o, ManualEntryRequest r)
    {
        var res = await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), r);
        await res.EnsureOkAsync();
        return (await res.Content.ReadFromJsonAsync<JournalEntryResponse>())!;
    }

    // ------------------------------------------------------------------ template and chart rules

    [Fact]
    public async Task The_transport_template_creates_the_approved_chart_cash_box_and_mapping_D10()
    {
        var owner = await f.SignUpOwnerAsync();
        var before = await owner.Client.GetFromJsonAsync<AccountingStatusResponse>(U(owner, "/accounting/status"));
        Assert.False(before!.PostingReady);
        Assert.Equal(13, before.MissingPostingKeys.Count);

        var r = await owner.Client.PostAsJsonAsync(U(owner, "/accounting/accounts/template"), new { });
        await r.EnsureOkAsync();
        // Revision 6: + account 4105 "Supplier deductions income" mapped to the optional key ACC.SupplierDeductions.
        // DT-256: + 5207 "VAT expenses (non-recoverable)"; 1105, 2104 and 5207 are tax accounts.
        Assert.Equal(new ChartTemplateResult(49, 1, 14), await r.Content.ReadFromJsonAsync<ChartTemplateResult>());

        var accounts = await AccountsAsync(owner);
        Assert.Equal(49, accounts.Count);
        Assert.Equal(["1105", "2104", "5207"], accounts.Values.Where(a => a.IsTaxAccount).Select(a => a.Code).Order(StringComparer.Ordinal));
        Assert.False(accounts["1"].IsPostable);
        Assert.True(accounts["110301"].IsPostable);
        Assert.Equal(("Customer", true), (accounts["110301"].ControlPartyType, accounts["110301"].IsControlAccount));
        Assert.Equal(("1103", 4), (accounts["110301"].ParentCode, accounts["110301"].Level));
        Assert.Equal("Asset", accounts["1203"].AccountType);

        var keys = await owner.Client.GetFromJsonAsync<List<PostingKeyResponse>>(U(owner, "/accounting/posting-keys"));
        Assert.All(keys!, k => Assert.Equal("Mapped", k.Status));
        Assert.Equal("4104", keys!.Single(k => k.Key == "TRN.DiscountAllowed").Account!.Code);
        Assert.Equal("4105", keys!.Single(k => k.Key == "ACC.SupplierDeductions").Account!.Code);
        var cash = await owner.Client.GetFromJsonAsync<List<CashBoxResponse>>(U(owner, "/accounting/cash-boxes"));
        Assert.Equal("110101", Assert.Single(cash!).Account.Code);

        var after = await owner.Client.GetFromJsonAsync<AccountingStatusResponse>(U(owner, "/accounting/status"));
        Assert.True(after!.PostingReady);
        Assert.False(after.OpeningDifferenceWarning);

        var again = await owner.Client.PostAsJsonAsync(U(owner, "/accounting/accounts/template"), new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("chart.notEmpty", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Chart_tree_rules_FR107_FR108_FR093()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;

        var child = await c.PostAsJsonAsync(U(owner, "/accounting/accounts"),
            new AccountRequest("110102", "صندوق فرعي", "Petty cash", null, a["1101"].Id, true, false, null, null));
        await child.EnsureOkAsync();
        Assert.Equal("Asset", (await child.Content.ReadFromJsonAsync<AccountResponse>())!.AccountType); // inherits the parent's type

        var underLinked = await c.PostAsJsonAsync(U(owner, "/accounting/accounts"),
            new AccountRequest("11010101", "فرعي", null, null, a["110101"].Id, true, false, null, null));
        Assert.Equal("account.parentInUse", await underLinked.ProblemCodeAsync()); // 110101 is linked to the main cash box

        var mismatch = await c.PostAsJsonAsync(U(owner, "/accounting/accounts"),
            new AccountRequest("1107", "خطأ", null, "Expense", a["11"].Id, true, false, null, null));
        Assert.Equal("account.typeMismatch", await mismatch.ProblemCodeAsync());

        var duplicate = await c.PostAsJsonAsync(U(owner, "/accounting/accounts"),
            new AccountRequest("4101", "مكرر", null, "Revenue", a["4"].Id, true, false, null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // A new child of a postable, unused account makes the parent non-postable (FR-108)
        await (await c.PostAsJsonAsync(U(owner, "/accounting/accounts"),
            new AccountRequest("520601", "عمولات بنك", null, null, a["5206"].Id, true, false, null, null))).EnsureOkAsync();
        Assert.False((await AccountsAsync(owner))["5206"].IsPostable);
        var postableParent = await c.PutAsJsonAsync(U(owner, $"/accounting/accounts/{a["5206"].Id}"),
            new AccountRequest("5206", a["5206"].NameAr, null, "Expense", null, true, false, null, null));
        Assert.Equal("account.hasChildren", await postableParent.ProblemCodeAsync());

        var mapped = await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["4101"].Id}/deactivate"), new StatusChangeRequest(null));
        Assert.Equal("account.linked", await mapped.ProblemCodeAsync()); // FR-093
        await (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5299"].Id}/deactivate"), new StatusChangeRequest(null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5299"].Id}/activate"), new StatusChangeRequest(null))).EnsureOkAsync();

        Assert.Equal("account.typeMismatch", await (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5103"].Id}/move"),
            new MoveAccountRequest(a["11"].Id, null))).ProblemCodeAsync());
        Assert.Equal("account.cycle", await (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["1"].Id}/move"),
            new MoveAccountRequest(a["1101"].Id, null))).ProblemCodeAsync());
        var moved = await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5103"].Id}/move"), new MoveAccountRequest(a["52"].Id, null));
        await moved.EnsureOkAsync();
        Assert.Equal("52", (await moved.Content.ReadFromJsonAsync<AccountResponse>())!.ParentCode);

        var inUse = await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["4101"].Id}/delete"), new DeleteRequest("cleanup"));
        Assert.Equal("record.inUse", await inUse.ProblemCodeAsync()); // BR-040 with the referencing types
        Assert.Contains("posting keys", (await inUse.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5299"].Id}/delete"), new DeleteRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["5299"].Id}/delete"), new DeleteRequest("not used"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(U(owner, $"/accounting/accounts/{a["5299"].Id}"))).StatusCode);
        Assert.True(await f.DbAsync(db => db.Set<Account>().IgnoreQueryFilters().AnyAsync(x => x.Code == "5299" && x.IsDeleted && x.DeleteReason == "not used")));
    }

    [Fact]
    public async Task Posting_key_mapping_accepts_only_active_postable_accounts_FR092_FR093()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var parent = await owner.Client.PutAsJsonAsync(U(owner, "/accounting/posting-keys/TRN.TransportRevenue"), new PostingKeyRequest(a["4"].Id));
        Assert.Equal("account.notPostable", await parent.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PutAsJsonAsync(U(owner, "/accounting/posting-keys/TRN.Unknown"), new PostingKeyRequest(a["4102"].Id))).StatusCode);
        var ok = await owner.Client.PutAsJsonAsync(U(owner, "/accounting/posting-keys/TRN.TransportRevenue"), new PostingKeyRequest(a["4102"].Id));
        await ok.EnsureOkAsync();
        Assert.Equal("4102", (await ok.Content.ReadFromJsonAsync<PostingKeyResponse>())!.Account!.Code);
        // FR-095: the change is audited old → new
        var audited = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .AnyAsync(l => l.EntityType == "ACC.PostingKeyMapping" && l.Action == "MappingChange"));
        Assert.True(audited);
    }

    // ------------------------------------------------------------------ manual journal

    [Fact]
    public async Task Manual_entries_post_edit_by_reversal_and_void_FR111_FR113_D05()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;
        var first = await PostEntryAsync(owner, Entry(Today, "إيجار المكتب", Dr(a["5202"], 100), Cr(a["110101"], 100)));
        Assert.Equal(("JV-00001", "Posted", true), (first.Number, first.Status, first.IsManual));
        Assert.Equal(2, first.Lines.Count);

        var unbalanced = await c.PostAsJsonAsync(U(owner, "/accounting/journal-entries"), Entry(Today, "x", Dr(a["5202"], 100), Cr(a["110101"], 99.999m)));
        Assert.Equal("journal.unbalanced", await unbalanced.ProblemCodeAsync());
        var noParty = await c.PostAsJsonAsync(U(owner, "/accounting/journal-entries"), Entry(Today, "x", Dr(a["5202"], 10), Cr(a["2101"], 10)));
        Assert.Equal("journal.partyRequired", await noParty.ProblemCodeAsync()); // FR-106
        var parent = await c.PostAsJsonAsync(U(owner, "/accounting/journal-entries"), Entry(Today, "x", Dr(a["52"], 10), Cr(a["110101"], 10)));
        Assert.Equal("account.notPostable", await parent.ProblemCodeAsync());

        // Edit = reverse + re-post (D-05): the original is Reversed and linked, the corrected entry has a new number.
        var edit = await c.PutAsJsonAsync(U(owner, $"/accounting/journal-entries/{first.Id}"),
            new ManualEntryRequest(Today, "إيجار المكتب (مصحح)", [Dr(a["5202"], 120), Cr(a["110101"], 120)], first.RowVersion));
        await edit.EnsureOkAsync();
        var corrected = (await edit.Content.ReadFromJsonAsync<JournalEntryResponse>())!;
        Assert.Equal("JV-00003", corrected.Number);
        var original = (await c.GetFromJsonAsync<JournalEntryResponse>(U(owner, $"/accounting/journal-entries/{first.Id}")))!;
        Assert.Equal("Reversed", original.Status);
        Assert.Equal("JV-00002", original.ReversedByEntry!.Number);
        var stale = await c.PutAsJsonAsync(U(owner, $"/accounting/journal-entries/{first.Id}"),
            new ManualEntryRequest(Today, "again", [Dr(a["5202"], 1), Cr(a["110101"], 1)], first.RowVersion));
        Assert.Equal("journal.notPosted", await stale.ProblemCodeAsync());
        var reversalOfFirst = (await c.GetFromJsonAsync<JournalEntryResponse>(U(owner, $"/accounting/journal-entries/{original.ReversedByEntry.Id}")))!;
        Assert.Equal("journal.isReversal", await (await c.PostAsJsonAsync(U(owner, $"/accounting/journal-entries/{reversalOfFirst.Id}/void"),
            new VoidRequest("x", null))).ProblemCodeAsync()); // a reversal cannot be voided back (CR-B2-01)

        // Void by reversal with a reason (FR-113): number kept, status Void.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(U(owner, $"/accounting/journal-entries/{corrected.Id}/void"), new VoidRequest(null, null))).StatusCode);
        var voided = await c.PostAsJsonAsync(U(owner, $"/accounting/journal-entries/{corrected.Id}/void"), new VoidRequest("قيد مكرر", corrected.RowVersion));
        await voided.EnsureOkAsync();
        var v = (await voided.Content.ReadFromJsonAsync<JournalEntryResponse>())!;
        Assert.Equal(("JV-00003", "Void", "قيد مكرر", "JV-00004"), (v.Number, v.Status, v.VoidReason, v.ReversedByEntry!.Number));

        // Lists and filters (FR-115) — the automatic-entries list shows no manual entries (FR-102).
        var all = await c.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, "/accounting/journal-entries?manual=true"));
        Assert.Equal(4, all!.TotalCount);
        var voids = await c.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, "/accounting/journal-entries?status=Void"));
        Assert.Equal("JV-00003", Assert.Single(voids!.Items).Number);
        var byAccount = await c.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, $"/accounting/journal-entries?accountId={a["5202"].Id}&number=JV-0000"));
        Assert.Equal(4, byAccount!.TotalCount);
        var automatic = await c.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, "/ledger/entries"));
        Assert.Equal(0, automatic!.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(U(owner, $"/ledger/entries/{first.Id}"))).StatusCode);

        // Voucher (FR-103, BR-024)
        var voucher = await c.GetFromJsonAsync<JournalVoucherResponse>(U(owner, $"/accounting/journal-entries/{first.Id}/voucher"));
        Assert.Equal("فقط مائة دينار لا غير", voucher!.AmountInWordsAr);
        Assert.Equal("One hundred Jordanian dinars only", voucher.AmountInWordsEn);

        var xlsx = await c.GetAsync(U(owner, "/accounting/journal-entries/export"));
        await xlsx.EnsureOkAsync();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        // FR-115: CSV and the printout (HTML here; PDF is the same page through the PDF engine) with the filters applied.
        var csv = await c.GetAsync(U(owner, "/accounting/journal-entries/export?format=csv&sourceType=Opening"));
        await csv.EnsureOkAsync();
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var printout = await (await c.GetAsync(U(owner, $"/accounting/journal-entries/export?format=html&number={first.Number}"))).Content.ReadAsStringAsync();
        Assert.Contains(first.Number, printout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cost_centres_cash_boxes_and_bank_accounts_FR129_FR131()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;
        var cc = await c.PostAsJsonAsync(U(owner, "/accounting/cost-centres"), new CostCentreRequest("CC-01", "أسطول الشمال", null));
        await cc.EnsureOkAsync();
        var centre = (await cc.Content.ReadFromJsonAsync<CostCentreResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(U(owner, "/accounting/cost-centres"), new CostCentreRequest("cc-01 ", "x", null))).StatusCode);
        await PostEntryAsync(owner, Entry(Today, "وقود", Dr(a["5103"], 50, centre.Id), Cr(a["110101"], 50)));
        Assert.Equal("record.inUse", await (await c.PostAsJsonAsync(U(owner, $"/accounting/cost-centres/{centre.Id}/delete"), new DeleteRequest("x"))).ProblemCodeAsync());
        await (await c.PostAsJsonAsync(U(owner, $"/accounting/cost-centres/{centre.Id}/archive"), new StatusChangeRequest(null))).EnsureOkAsync();
        var archived = await c.PostAsJsonAsync(U(owner, "/accounting/journal-entries"), Entry(Today, "وقود", Dr(a["5103"], 5, centre.Id), Cr(a["110101"], 5)));
        Assert.Equal("record.archived", await archived.ProblemCodeAsync());

        var badIban = await c.PostAsJsonAsync(U(owner, "/accounting/bank-accounts"),
            new BankAccountRequest("الحساب الجاري", "بنك القاهرة عمان", "0131000302", "JO94CBJO001", null, a["110201"].Id, null));
        Assert.Equal(HttpStatusCode.BadRequest, badIban.StatusCode);
        var bank = await c.PostAsJsonAsync(U(owner, "/accounting/bank-accounts"),
            new BankAccountRequest("الحساب الجاري", "بنك القاهرة عمان", "0131000302", "jo94 cbjo 0010 0000 0000 0131 0003 02", null, a["110201"].Id, null));
        await bank.EnsureOkAsync();
        var b = (await bank.Content.ReadFromJsonAsync<BankAccountResponse>())!;
        Assert.Equal(("JO94CBJO0010000000000131000302", "JOD"), (b.Iban, b.CurrencyCode));
        var taken = await c.PostAsJsonAsync(U(owner, "/accounting/cash-boxes"), new CashBoxRequest("صندوق 2", a["110201"].Id, null));
        Assert.Equal("account.linkTaken", await taken.ProblemCodeAsync());
        var revenue = await c.PostAsJsonAsync(U(owner, "/accounting/cash-boxes"), new CashBoxRequest("صندوق 2", a["4101"].Id, null));
        Assert.Equal("account.notAsset", await revenue.ProblemCodeAsync());
        Assert.Equal("account.linked", await (await c.PostAsJsonAsync(U(owner, $"/accounting/accounts/{a["110201"].Id}/deactivate"),
            new StatusChangeRequest(null))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync(U(owner, $"/accounting/bank-accounts/{b.Id}/delete"), new DeleteRequest("test"))).StatusCode);
    }

    // ------------------------------------------------------------------ opening balances

    [Fact]
    public async Task Opening_balances_post_one_entry_with_the_setup_difference_rule_FR110()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;
        var put = await c.PutAsJsonAsync(U(owner, "/accounting/opening-balances"), new OpeningBalancesRequest(
        [
            new OpeningBalanceLine(a["110101"].Id, 800, 0),
            new OpeningBalanceLine(a["3101"].Id, 0, 1000),
        ]));
        await put.EnsureOkAsync();
        var state = (await put.Content.ReadFromJsonAsync<OpeningBalancesResponse>())!;
        Assert.Equal((800m, 1000m, -200m, true), (state.TotalDebit, state.TotalCredit, state.Difference, state.SetupOpen));
        var parentOpening = await c.PutAsJsonAsync(U(owner, "/accounting/opening-balances"), new OpeningBalancesRequest([new OpeningBalanceLine(a["11"].Id, 5, 0)]));
        Assert.Equal("account.notPostable", await parentOpening.ProblemCodeAsync());

        // During setup the difference goes to the opening-difference account (G-04) with a warning.
        var posted = await c.PostAsJsonAsync(U(owner, "/accounting/opening-balances/post"), new PostOpeningRequest(Today));
        await posted.EnsureOkAsync();
        var afterPost = (await posted.Content.ReadFromJsonAsync<OpeningBalancesResponse>())!;
        Assert.Equal(200m, afterPost.DifferenceAccountBalance);
        var entry = (await c.GetFromJsonAsync<JournalEntryResponse>(U(owner, $"/accounting/journal-entries/{afterPost.PostedEntry!.Id}")))!;
        Assert.Equal(("ACC", "Opening", false, 1000m), (entry.SourceModule, entry.SourceType, entry.IsManual, entry.TotalDebit));
        Assert.Contains(entry.Lines, l => l.Account.Code == "3103" && l.Debit == 200);
        Assert.True((await c.GetFromJsonAsync<AccountingStatusResponse>(U(owner, "/accounting/status")))!.OpeningDifferenceWarning);
        Assert.Equal("journal.notManual", await (await c.PutAsJsonAsync(U(owner, $"/accounting/journal-entries/{entry.Id}"),
            Entry(Today, "x", Dr(a["5202"], 1), Cr(a["110101"], 1)))).ProblemCodeAsync()); // FR-112

        // Balancing it (customer receivables opening) and re-posting replaces the entry by reversal; the warning clears.
        await (await c.PutAsJsonAsync(U(owner, "/accounting/opening-balances"), new OpeningBalancesRequest([new OpeningBalanceLine(a["110301"].Id, 200, 0)]))).EnsureOkAsync();
        var reposted = (await (await c.PostAsJsonAsync(U(owner, "/accounting/opening-balances/post"), new PostOpeningRequest(null))).Content
            .ReadFromJsonAsync<OpeningBalancesResponse>())!;
        Assert.Equal(0m, reposted.DifferenceAccountBalance);
        Assert.NotEqual(afterPost.PostedEntry.Number, reposted.PostedEntry!.Number);
        Assert.Equal("Reversed", (await c.GetFromJsonAsync<JournalEntryResponse>(U(owner, $"/accounting/journal-entries/{afterPost.PostedEntry.Id}")))!.Status);
        Assert.False((await c.GetFromJsonAsync<AccountingStatusResponse>(U(owner, "/accounting/status")))!.OpeningDifferenceWarning);

        // The receivables opening names no customer: the pre-close check lists the control account (FR-134).
        var check = await c.GetFromJsonAsync<PreCloseCheckResponse>(U(owner, $"/accounting/periods/{Month(Today)}/pre-close-check"));
        Assert.Equal("110301", Assert.Single(check!.UnreconciledControlAccounts).Account.Code);
        Assert.False(check.Clean);

        // After migration is marked complete, unbalanced openings are refused (FR-110, FR-171).
        await (await c.PostAsJsonAsync(U(owner, "/settings/migration-complete"), new MigrationCompleteRequest(true))).EnsureOkAsync();
        await (await c.PutAsJsonAsync(U(owner, "/accounting/opening-balances"), new OpeningBalancesRequest([new OpeningBalanceLine(a["3101"].Id, 0, 1100)]))).EnsureOkAsync();
        var refused = await c.PostAsJsonAsync(U(owner, "/accounting/opening-balances/post"), new PostOpeningRequest(null));
        Assert.Equal("opening.unbalanced", await refused.ProblemCodeAsync());
    }

    // ------------------------------------------------------------------ periods

    [Fact]
    public async Task A_closed_period_blocks_posting_and_voiding_until_reopened_FR132_FR133()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var entry = await PostEntryAsync(owner, Entry(lastMonth.AddDays(4), "مصاريف", Dr(a["5203"], 30), Cr(a["110101"], 30)));
        var month = Month(lastMonth);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(U(owner, $"/accounting/periods/{month}/close"), new PeriodActionRequest(null, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(U(owner, "/accounting/periods/2999-01/close"), new PeriodActionRequest("x", true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(U(owner, "/accounting/periods/2026-13/close"), new PeriodActionRequest("x", true))).StatusCode);
        var close = await c.PostAsJsonAsync(U(owner, $"/accounting/periods/{month}/close"), new PeriodActionRequest("إقفال الشهر", false));
        await close.EnsureOkAsync();
        Assert.Equal("Closed", (await close.Content.ReadFromJsonAsync<PeriodResponse>())!.Status);
        var year = await c.GetFromJsonAsync<List<PeriodResponse>>(U(owner, $"/accounting/periods?year={lastMonth.Year}"));
        Assert.Equal(12, year!.Count);
        Assert.Equal("Closed", year.Single(p => p.PeriodMonth == month).Status);

        var post = await c.PostAsJsonAsync(U(owner, "/accounting/journal-entries"), Entry(lastMonth.AddDays(10), "x", Dr(a["5203"], 1), Cr(a["110101"], 1)));
        Assert.Equal("period.closed", await post.ProblemCodeAsync());
        var voidIt = await c.PostAsJsonAsync(U(owner, $"/accounting/journal-entries/{entry.Id}/void"), new VoidRequest("x", null));
        Assert.Equal("period.closed", await voidIt.ProblemCodeAsync());
        Assert.Equal("period.alreadyClosed", await (await c.PostAsJsonAsync(U(owner, $"/accounting/periods/{month}/close"), new PeriodActionRequest("again", true))).ProblemCodeAsync());

        var reopen = await c.PostAsJsonAsync(U(owner, $"/accounting/periods/{month}/reopen"), new PeriodActionRequest("تصحيح", false));
        await reopen.EnsureOkAsync();
        Assert.Equal("Open", (await reopen.Content.ReadFromJsonAsync<PeriodResponse>())!.Status);
        await (await c.PostAsJsonAsync(U(owner, $"/accounting/journal-entries/{entry.Id}/void"), new VoidRequest("x", null))).EnsureOkAsync();
        var audit = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .Where(l => l.EntityType == "ACC.Period").Select(l => l.Action).ToListAsync());
        Assert.Contains("Close", audit);
        Assert.Contains("Reopen", audit);
        // A period has been closed: setup is over, so the opening-difference key is no longer required (FR-110).
        Assert.False((await c.GetFromJsonAsync<OpeningBalancesResponse>(U(owner, "/accounting/opening-balances")))!.SetupOpen);
    }

    // ------------------------------------------------------------------ journal export

    [Fact]
    public async Task Journal_export_creates_numbered_logged_batches_FR104()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var c = owner.Client;
        await PostEntryAsync(owner, Entry(Today, "إيجار", Dr(a["5202"], 100), Cr(a["110101"], 100)));
        await PostEntryAsync(owner, Entry(Today, "=HYPERLINK(\"x\")", Dr(a["5203"], 25.5m), Cr(a["110101"], 25.5m)));

        var bad = await c.PostAsJsonAsync(U(owner, "/ledger/exports"), new JournalExportRequest(Month(Today), Month(Today), "XML"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var csv = await c.PostAsJsonAsync(U(owner, "/ledger/exports"), new JournalExportRequest(Month(Today), Month(Today), "csv"));
        await csv.EnsureOkAsync();
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Contains("JEX-00001", text, StringComparison.Ordinal);
        Assert.Contains("JV-00002", text, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", text, StringComparison.Ordinal); // CSV injection neutralised
        var json = await c.PostAsJsonAsync(U(owner, "/ledger/exports"), new JournalExportRequest(Month(Today), Month(Today), "JSON"));
        await json.EnsureOkAsync();
        using var doc = JsonDocument.Parse(await json.Content.ReadAsStringAsync());
        Assert.Equal("JEX-00002", doc.RootElement.GetProperty("batchNo").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Equal(125.5m, doc.RootElement.GetProperty("totalDebit").GetDecimal());

        var batches = await c.GetFromJsonAsync<List<JournalExportBatchResponse>>(U(owner, "/ledger/exports"));
        Assert.Equal(2, batches!.Count);
        Assert.All(batches, b => Assert.Equal((2, 125.5m, 125.5m), (b.EntryCount, b.TotalDebit, b.TotalCredit)));
        var entries = await c.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, "/accounting/journal-entries"));
        var detail = await c.GetFromJsonAsync<JournalEntryResponse>(U(owner, $"/accounting/journal-entries/{entries!.Items[0].Id}"));
        Assert.Equal("JEX-00002", detail!.ExportBatchNo); // entries point to their latest batch
    }

    // ------------------------------------------------------------------ imports

    [Fact]
    public async Task Chart_import_previews_rejections_and_imports_strictly_FR109()
    {
        var owner = await f.SignUpOwnerAsync();
        var sheets = f.Services.GetRequiredService<ISpreadsheetService>();
        string[] headers = ["رمز الحساب", "الاسم بالعربية", "English name", "نوع الحساب", "رمز الحساب الأب", "رصيد افتتاحي مدين", "رصيد افتتاحي دائن"];
        IReadOnlyList<object?>[] good =
        [
            ["1", "الأصول", "Assets", "أصول", null, null, null],
            ["11", "الأصول المتداولة", null, "Asset", "1", null, null],
            ["1101", "الصندوق", "Cash", "Asset", "11", 500m, null],
            ["3", "حقوق الملكية", null, "Equity", null, null, null],
            ["3101", "رأس المال", null, "حقوق الملكية", "3", null, 500m],
        ];
        IReadOnlyList<object?>[] bad =
        [
            .. good,
            ["11", "مكرر", null, "Asset", "1", null, null],
            ["1201", "بلا أب", null, "Asset", "99", null, null],
            ["5", "نوع خطأ", null, "Foo", null, null, null],
            ["3102", "نوع لا يطابق", null, "Asset", "3", null, null],
        ];

        var preview = await UploadAsync<ImportPreview>(owner, "/accounting/accounts/import/preview", sheets.WriteXlsx("Chart", headers, bad), "chart.xlsx");
        Assert.Equal((9, 4), (preview.TotalRows, preview.RejectedRows));
        Assert.Equal(["11", "1201", "5", "3102"], preview.Rows.Where(r => r.Problems.Count > 0).Select(r => r.Key));
        var refused = await UploadAsync(owner, "/accounting/accounts/import", sheets.WriteXlsx("Chart", headers, bad), "chart.xlsx");
        Assert.Equal("import.hasErrors", await refused.ProblemCodeAsync());
        Assert.Empty(await AccountsAsync(owner));

        var imported = await UploadAsync<ImportResult>(owner, "/accounting/accounts/import", sheets.WriteXlsx("Chart", headers, good), "chart.xlsx");
        Assert.Equal(5, imported.ImportedRows);
        var accounts = await AccountsAsync(owner);
        Assert.Equal((true, 500m), (accounts["1101"].IsPostable, accounts["1101"].OpeningDebit));
        Assert.False(accounts["11"].IsPostable);
        Assert.Equal("Equity", accounts["3101"].AccountType);

        var formula = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("Code,NameAr,Type\n=1+1,x,Asset\n")), "file", "c.csv" } };
        Assert.Equal("import.formula", await (await owner.Client.PostAsync(U(owner, "/accounting/accounts/import/preview"), formula)).ProblemCodeAsync());
        var export = await owner.Client.GetAsync(U(owner, "/accounting/accounts/export"));
        await export.EnsureOkAsync();
        var back = sheets.Read(await export.Content.ReadAsStreamAsync(), "chart.xlsx", export.Content.Headers.ContentLength ?? 0);
        Assert.Equal(5, back.Rows.Count);
    }

    [Fact]
    public async Task Journal_import_rejects_unbalanced_unknown_and_closed_rows_FR114()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var d = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var csv = $"""
            EntryRef,Date,Description,AccountCode,Debit,Credit,CostCentre,PartyType,Party,Note
            A,{d},إيجار,5202,100,,,,,
            A,{d},إيجار,110101,,100,,,,
            B,{d},غير متوازن,5203,50,,,,,
            B,{d},غير متوازن,110101,,40,,,,
            C,{d},حساب مجهول,9999,10,,,,,
            C,{d},حساب مجهول,110101,,10,,,,
            D,{d},مورد,5299,10,,,,,
            D,{d},مورد,2101,,10,,,,
            """;
        var preview = await UploadAsync<ImportPreview>(owner, "/accounting/journal-entries/import/preview", Encoding.UTF8.GetBytes(csv), "je.csv");
        Assert.Equal((8, 4), (preview.TotalRows, preview.RejectedRows)); // B ×2 unbalanced, C unknown account, D control without party
        Assert.Equal(["B", "B", "C", "D"], preview.Rows.Where(r => r.Problems.Count > 0).Select(r => r.Key));

        var good = $"""
            EntryRef,Date,Description,AccountCode,Debit,Credit
            A,{d},إيجار,5202,100,
            A,{d},إيجار,110101,,100
            B,{d},اتصالات,5203,"1,250.500",
            B,{d},اتصالات,110101,,1250.5
            """;
        var result = await UploadAsync<ImportResult>(owner, "/accounting/journal-entries/import", Encoding.UTF8.GetBytes(good), "je.csv");
        Assert.Equal(4, result.ImportedRows);
        var list = await owner.Client.GetFromJsonAsync<PagedResult<JournalEntrySummary>>(U(owner, "/accounting/journal-entries"));
        Assert.Equal(2, list!.TotalCount);
        Assert.Contains(list.Items, e => e.TotalDebit == 1250.5m);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInUser o, string path, byte[] content, string fileName) =>
        await o.Client.PostAsync(U(o, path), new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } });

    private static async Task<T> UploadAsync<T>(SignedInUser o, string path, byte[] content, string fileName)
    {
        var r = await UploadAsync(o, path, content, fileName);
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    // ------------------------------------------------------------------ permissions

    [Fact]
    public async Task Accounting_permissions_follow_the_role_presets()
    {
        var (owner, a) = await TemplateCompanyAsync();
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Dispatcher);
        var accountant = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Accountant);
        string P(string path) => U(owner, path);

        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync(P("/accounting/accounts"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(P("/accounting/journal-entries"),
            Entry(Today, "x", Dr(a["5202"], 1), Cr(a["110101"], 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(P("/accounting/accounts"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dispatcher.Client.GetAsync(P("/accounting/status"))).StatusCode); // banner: every member
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.GetAsync(P("/accounting/posting-keys"))).StatusCode); // settings.manage
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.PostAsJsonAsync(P("/accounting/accounts/template"), new { })).StatusCode); // acct.setup
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.PostAsJsonAsync(P($"/accounting/periods/{Month(Today)}/close"),
            new PeriodActionRequest("x", true))).StatusCode);
        await (await accountant.Client.PostAsJsonAsync(P("/accounting/journal-entries"), Entry(Today, "x", Dr(a["5202"], 1), Cr(a["110101"], 1)))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.OK, (await accountant.Client.GetAsync(P("/ledger/pending-postings"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(P("/ledger/exports"),
            new JournalExportRequest(Month(Today), Month(Today), "CSV"))).StatusCode); // je.export
    }
}
