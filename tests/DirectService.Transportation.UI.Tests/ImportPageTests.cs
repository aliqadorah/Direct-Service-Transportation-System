using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Client.Pages.Trips;
using Microsoft.AspNetCore.Components.Forms;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Import screens (owner feedback 13 Oct 2026): steps with Back and Next, rejected rows with column and fix, corrected files.</summary>
public sealed class ImportPageTests : PageTestBase
{
    private static readonly ImportField[] TripFields =
    [
        new("receiptNo", "رقم السند", true), new("tripDate", "التاريخ", true), new("driverId", "السائق", true), new("loadingSiteId", "موقع التحميل", true),
        new("notes", "ملاحظات", false),
    ];

    private static TripImportPreview Preview(IReadOnlyDictionary<string, string> matched, IReadOnlyList<string>? missing = null) => new(
        2, 1, 1, 0, 0, "Tolerant",
        [
            new ImportRowResult(2, "IMP-1", []),
            new ImportRowResult(3, "IMP-2", ["«موقع التحميل» غير معروف: الحسا."],
                [new ImportProblem("import.unknown", "Loading site", "«موقع التحميل» غير معروف: الحسا.", "اكتب الاسم كما هو في البيانات الأساسية")]),
        ],
        new ImportColumns(matched, ["Remarks"]), TripFields, missing);

    private static readonly Dictionary<string, string> Matched = new(StringComparer.Ordinal)
    {
        ["receiptNo"] = "Receipt no", ["tripDate"] = "Date", ["driverId"] = "Driver", ["loadingSiteId"] = "Loading site",
    };

    private IRenderedComponent<TripImport> Upload()
    {
        var cut = Render<TripImport>(p => p.Add(x => x.Company, Company));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Receipt no,Date,Driver,Loading site,Remarks\nIMP-1,2026-10-12,x,y,z", "trips.csv"));
        return cut;
    }

    private void Click(IRenderedComponent<TripImport> cut, string text) =>
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == text).Click());

    [Fact]
    public void Trip_import_moves_back_and_next_keeping_the_file_and_matching_and_lists_each_problem_with_its_column_and_fix()
    {
        Respond("trips/import/preview", Preview(Matched));
        var cut = Upload();
        cut.WaitForAssertion(() => Assert.Contains("trips.csv", cut.Markup));

        // Step 2: every field with its label; required ones marked; the matching is editable.
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains("موقع التحميل", cut.Markup));
        Act(() => cut.Find("#map-notes").Change("Remarks"));

        // Step 3: the file is checked again with the chosen matching; each problem with row, column, problem and fix; a summary.
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains(L["imp.fix"], cut.Markup));
        Assert.Contains(Requests, r => r.Path.StartsWith("trips/import/preview", StringComparison.Ordinal) && r.Body!.Contains("\"notes\":\"Remarks\"", StringComparison.Ordinal));
        var text = Text(cut);
        Assert.Contains("Loading site", text);
        Assert.Contains("اكتب الاسم كما هو في البيانات الأساسية", text);
        Assert.Contains(L.F("imp.summary", 1, 1, 2), text);
        Assert.DoesNotContain("IMP-1", text);   // valid rows are counted, not listed
        Assert.Single(cut.FindAll("tbody tr"));

        // Back to the matching and to the file: nothing is lost.
        Click(cut, L["imp.back"]);
        cut.WaitForAssertion(() => Assert.Equal("Remarks", cut.Find("#map-notes").GetAttribute("value")));
        Click(cut, L["imp.back"]);
        cut.WaitForAssertion(() => Assert.Contains("trips.csv", cut.Markup));

        // Forward again to the import step and import the valid row.
        Respond("trips/import", new TripImportResult("b1", 2, 1, 1, 0));
        Respond("trips/import/preview", Preview(Matched));
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => cut.Find("#map-notes"));
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains(L["imp.fix"], cut.Markup));
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains(L.F("imp.skipped", 1), cut.Markup));
        Click(cut, L.F("trip.importValid", 1));
        cut.WaitForAssertion(() => Assert.Contains(L["trip.importDone"], cut.Markup));
        Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "trips/import" && r.Body!.Contains("\"notes\":\"Remarks\"", StringComparison.Ordinal));
    }

    /// <summary>Owner feedback 13 Oct 2026: every import screen offers its Excel template.</summary>
    [Theory]
    [InlineData("trips")]
    [InlineData("drivers")]
    [InlineData("tariffs")]
    [InlineData("accounting/accounts")]
    [InlineData("accounting/journal-entries")]
    public void Import_screens_download_their_template(string kind)
    {
        Respond(kind + "/import/template", new { });
        IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut = kind switch
        {
            "trips" => Render<TripImport>(p => p.Add(x => x.Company, Company)),
            "drivers" or "tariffs" => Render<DirectService.Transportation.Web.Client.Pages.Masters.MasterImport>(p => p.Add(x => x.Company, Company).Add(x => x.Kind, kind)),
            _ => Render<DirectService.Transportation.Web.Client.Pages.Accounting.ImportPanel>(p => p.Add(x => x.PreviewPath, kind + "/import/preview")
                .Add(x => x.ImportPath, kind + "/import").Add(x => x.Done, "accounting")),
        };
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["imp.template"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "GET" && r.Path == kind + "/import/template"));
    }

    [Fact]
    public void Trip_import_asks_for_a_missing_required_column_and_takes_a_corrected_file_on_the_check_step()
    {
        var withoutDriver = new Dictionary<string, string>(Matched, StringComparer.Ordinal);
        withoutDriver.Remove("driverId");
        Respond("trips/import/preview", r => r.Content!.ReadAsStringAsync().Result.Contains("\"driverId\":\"Remarks\"", StringComparison.Ordinal)
            ? Preview(new Dictionary<string, string>(withoutDriver) { ["driverId"] = "Remarks" })
            : Preview(withoutDriver, ["driverId"]));
        var cut = Upload();
        cut.WaitForAssertion(() => Assert.Contains("trips.csv", cut.Markup));
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains(L["imp.requiredColumn"], cut.Markup));
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == L["ui.next"]).HasAttribute("disabled"));
        Act(() => cut.Find("#map-driverId").Change("Remarks"));
        Click(cut, L["ui.next"]);
        cut.WaitForAssertion(() => Assert.Contains(L["imp.reupload"], cut.Markup));

        // A corrected file is checked at once on the same step, with the same matching.
        var before = Requests.Count;
        cut.FindComponents<InputFile>()[^1].UploadFiles(InputFileContent.CreateFromText("Receipt no,Date,Remarks,Loading site\nIMP-1,2026-10-12,x,y", "trips-fixed.csv"));
        cut.WaitForAssertion(() => Assert.True(Requests.Count > before));
        cut.WaitForAssertion(() => Assert.Contains(L["imp.fix"], cut.Markup));
        Assert.Contains("\"driverId\":\"Remarks\"", Requests[^1].Body!, StringComparison.Ordinal);
        Assert.Contains("trips-fixed.csv", Requests[^1].Body!, StringComparison.Ordinal);
    }
}
