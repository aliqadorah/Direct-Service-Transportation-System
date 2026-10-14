using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages.Settings;
using DirectService.Transportation.Web.Client.Ui;
using HistoryEntry = DirectService.Transportation.Contracts.MasterData.HistoryEntry;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Readable history and audit log (owner feedback 14 Oct 2026, DT-188): the History panel shows the API's sentence with the documents as
/// links (only screens the reader may open), labelled changes in the reading direction and a journal entry's lines; the audit log shows the
/// readable columns, and the technical details only to administrators who ask for them.
/// </summary>
public sealed class AuditViewsTests() : PageTestBase(PermissionKeys.TripsView, PermissionKeys.InvView, PermissionKeys.SettingsManage, PermissionKeys.UsersManage)
{
    private static readonly HistoryEntry Added = new(new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc), "علي علي", "AddedToInvoice", null, [],
        "علي علي: إضافة الرحلة R-1001 إلى مسودة الفاتورة INV-00001", "إضافة إلى فاتورة", null, [new HistoryLink("Invoice", "inv1", "INV-00001")]);

    private static readonly HistoryEntry Edited = new(new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc), "علي علي", "Update", "تصحيح الوزن",
        [new FieldChange("الوزن المنزّل (طن)", "31.850", "31.900"), new FieldChange("المادة", "Phosphate", "Potash")], "علي علي: تعديل الرحلة R-1001", "تعديل");

    private static readonly HistoryEntry Posted = new(new DateTime(2026, 10, 14, 11, 0, 0, DateTimeKind.Utc), "علي علي", "Post", null, [],
        "علي علي: ترحيل القيد JV-00022 (تسوية سائق STL-00002)", "ترحيل",
        [new HistoryLine("5101 · أجور السائقين — أحمد صالح", "100.000", null), new HistoryLine("1101 · الصندوق", null, "100.000")],
        [new HistoryLink("Settlement", "stl2", "STL-00002"), new HistoryLink("JournalEntry", "je22", "JV-00022")]);

    [Fact]
    public void History_shows_sentences_links_labelled_changes_and_journal_lines()
    {
        var cut = Render<RecordHistory>(p => p.Add(x => x.Items, [Edited, Added, Posted]));
        var text = Text(cut);
        Assert.Contains("علي علي: إضافة الرحلة R-1001 إلى مسودة الفاتورة INV-00001", text);
        // The invoice number is a link to its screen; the settlement and the entry too.
        var invoice = cut.FindAll("a").Single(a => a.TextContent == "INV-00001");
        Assert.Equal($"/c/{Company}/invoices/inv1", invoice.GetAttribute("href"));
        // This reader cannot open driver accounts or journal entries: those numbers are plain text (item 7).
        Assert.DoesNotContain(cut.FindAll("a"), a => a.TextContent is "STL-00002" or "JV-00022");
        Assert.Contains("(تسوية سائق STL-00002)", text);
        // Changes with the form's labels, old ← new in Arabic, every value isolated (bdi) so numbers keep their order.
        var change = cut.FindAll(".ev-c li")[0];
        Assert.Contains("الوزن المنزّل (طن):", change.TextContent);
        Assert.Contains(" ← ", change.TextContent);
        Assert.Equal(["31.850", "31.900"], change.QuerySelectorAll("bdi.num").Select(b => b.TextContent).Where(t => t.Contains('.')).ToArray());
        Assert.Contains("السبب: تصحيح الوزن", text);
        // The journal entry's lines as a small table: account, debit, credit.
        var rows = cut.FindAll(".ev-lines tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("5101 · أجور السائقين — أحمد صالح", rows[0].TextContent);
        Assert.Equal("100.000", rows[0].QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.Equal("100.000", rows[1].QuerySelectorAll("td")[2].TextContent.Trim());
        // Nothing technical reaches the reader.
        Assert.DoesNotMatch(@"\b(TRN|ACC|CORE)\.[A-Z]|InvoiceId|JournalEntryId|IsDeleted|LineNo", cut.Markup);
    }

    [Fact]
    public void History_arrow_follows_the_reading_direction_in_english()
    {
        L.SetLanguage("en");
        var cut = Render<RecordHistory>(p => p.Add(x => x.Items, [Edited with { Changes = [new FieldChange("Unloaded weight (t)", "31.850", "31.900")] }]));
        Assert.Contains(" → ", cut.Find(".ev-c li").TextContent);
    }

    [Fact]
    public void Audit_log_shows_readable_columns_and_the_technical_details_on_request()
    {
        var entry = new AuditLogEntry(new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc), "علي علي", "AddedToInvoice", "TRN.Trip", "R-1001", null, [], "10.0.0.1",
            "إضافة إلى فاتورة", "رحلة", "إضافة الرحلة R-1001 إلى مسودة الفاتورة INV-00001", null, [new HistoryLink("Invoice", "inv1", "INV-00001")],
            [new AuditChange("InvoiceId", null, "91")]);
        Respond("audit-log/facets", new { entityTypes = new[] { new { id = "TRN.Trip", name = "رحلة" } }, actions = new[] { new { id = "AddedToInvoice", name = "إضافة إلى فاتورة" } }, users = Array.Empty<object>() });
        Respond("audit-log", r => new PagedResult<AuditLogEntry>([r.RequestUri!.Query.Contains("technical=true", StringComparison.Ordinal) ? entry : entry with { Technical = null }], 1, 50, 1));
        var cut = Render<AuditLog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("R-1001", cut.Markup));
        var text = Text(cut);
        Assert.Contains("إضافة إلى فاتورة", text);                        // action in words
        Assert.Contains("رحلة", text);                                     // record type in words, never TRN.Trip
        Assert.DoesNotContain("TRN.Trip", text);
        Assert.Contains("إضافة الرحلة R-1001 إلى مسودة الفاتورة INV-00001", text);
        Assert.Equal($"/c/{Company}/invoices/inv1", cut.FindAll("a").Single(a => a.TextContent == "INV-00001").GetAttribute("href"));
        Assert.DoesNotContain("InvoiceId", text);
        Assert.Contains(cut.FindAll("select option"), o => o.TextContent == "رحلة");   // filter choice in words

        // Technical details: an administrator asks, the API is asked with technical=true and the stored values appear.
        var toggle = cut.FindAll("button").Single(b => b.TextContent.Contains(L["aud.technical"], StringComparison.Ordinal));
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        toggle.Click();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("audit-log?", StringComparison.Ordinal) && r.Path.Contains("technical=true", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => Assert.Contains("InvoiceId: null → 91", Text(cut)));
        Assert.Contains("AddedToInvoice · TRN.Trip", Text(cut));
        Assert.Equal("true", cut.FindAll("button").Single(b => b.TextContent.Contains(L["aud.technical"], StringComparison.Ordinal)).GetAttribute("aria-pressed"));

        // Exports follow the same choice.
        cut.FindAll("button").First(b => b.TextContent.Contains("CSV", StringComparison.OrdinalIgnoreCase) || b.GetAttribute("aria-label")?.Contains("CSV", StringComparison.OrdinalIgnoreCase) == true).Click();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("audit-log/export", StringComparison.Ordinal) && r.Path.Contains("technical=true", StringComparison.Ordinal)));
    }
}

/// <summary>A reader who may open driver accounts and journal entries: those documents in a history are links (DT-188, item 7).</summary>
public sealed class AuditLinksWithAccessTests() : PageTestBase(PermissionKeys.TripsView, PermissionKeys.DrvView, PermissionKeys.JeView)
{
    [Fact]
    public void Documents_the_reader_may_open_are_links()
    {
        var posted = new HistoryEntry(DateTime.UtcNow, "علي علي", "Post", null, [], "علي علي: ترحيل القيد JV-00022 (تسوية سائق STL-00002)", "ترحيل", null,
            [new HistoryLink("Settlement", "stl2", "STL-00002"), new HistoryLink("JournalEntry", "je22", "JV-00022"), new HistoryLink("Invoice", "inv1", "INV-9")]);
        var cut = Render<RecordHistory>(p => p.Add(x => x.Items, [posted]));
        Assert.Equal($"/c/{Company}/driver-accounts/settlements/stl2", cut.FindAll("a").Single(a => a.TextContent == "STL-00002").GetAttribute("href"));
        Assert.Equal($"/c/{Company}/accounting/journal/je22", cut.FindAll("a").Single(a => a.TextContent == "JV-00022").GetAttribute("href"));
        Assert.Equal(2, cut.FindAll("a").Count);   // a link whose words are not in the sentence is not added
    }
}

/// <summary>A users.manage reader of the audit log without settings.manage: no technical details offered (DT-188).</summary>
public sealed class AuditLogReaderTests() : PageTestBase(PermissionKeys.UsersManage)
{
    [Fact]
    public void Technical_details_are_not_offered_without_settings_manage()
    {
        Respond("audit-log/facets", new { entityTypes = Array.Empty<object>(), actions = Array.Empty<object>(), users = Array.Empty<object>() });
        Respond("audit-log", new PagedResult<AuditLogEntry>([new AuditLogEntry(DateTime.UtcNow, "رنا", "Create", "CORE.User", "رنا", null, [], null, "إنشاء", "مستخدم", "إنشاء المستخدم رنا")], 1, 50, 1));
        var cut = Render<AuditLog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("إنشاء المستخدم رنا", Text(cut)));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["aud.technical"], StringComparison.Ordinal));
        Assert.DoesNotContain(Requests, r => r.Path.Contains("technical=", StringComparison.Ordinal));
    }
}
