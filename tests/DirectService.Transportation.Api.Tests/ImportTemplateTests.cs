using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner feedback 13 Oct 2026: every importer has a "Download template" — an Excel file in the user's language with exactly the
/// columns the import reads, one sample row (two lines for a journal entry) and a second sheet explaining each column. The samples are
/// chosen so that, imported in order (lists, tariffs, chart, journal, trips), every template imports cleanly as it is; real files replace
/// the sample rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ImportTemplateTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    /// <summary>Import paths in dependency order (lists before the tariffs and trips naming them).</summary>
    private static readonly string[] Paths =
    [
        "/ownership-types/import", "/partners/import", "/materials/import", "/loading-sites/import", "/unloading-sites/import", "/customers/import",
        "/drivers/import", "/tariffs/import", "/accounting/accounts/import", "/accounting/journal-entries/import", "/trips/import",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Every_template_has_the_import_columns_a_sample_and_notes_and_imports_cleanly(string language)
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        if (language == "ar")
            await (await c.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("ar", null, o.Me.DateFormat))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();

        foreach (var path in Paths)
        {
            if (path == "/trips/import")
            {
                // The driver of the sample is in the company's own fleet: its wage comes from the loading site's monthly rate.
                var site = (await c.GetFromJsonAsync<List<System.Text.Json.JsonElement>>(U(o, "/loading-sites/options")))!.Single().GetProperty("id").GetString()!;
                var month = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                await (await c.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"), new MonthRatesRequest([new SiteRateRequest(site, 3.500m, 4.250m, null)]))).EnsureOkAsync();
            }
            var file = await c.GetAsync(U(o, path + "/template"));
            await file.EnsureOkAsync();
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.Content.Headers.ContentType!.MediaType);
            var bytes = await file.Content.ReadAsByteArrayAsync();
            Assert.EndsWith($"-template-{language}.xlsx", file.Content.Headers.ContentDisposition!.FileNameStar ?? file.Content.Headers.ContentDisposition.FileName!, StringComparison.Ordinal);

            using (var book = new XLWorkbook(new MemoryStream(bytes)))
            {
                Assert.Equal(2, book.Worksheets.Count);
                var data = book.Worksheet(1);
                var notes = book.Worksheet(2);
                var headers = data.Row(1).CellsUsed().Select(x => x.GetString()).ToList();
                Assert.NotEmpty(headers);
                Assert.Equal(language == "ar", data.RightToLeft);
                Assert.Equal(path.EndsWith("journal-entries/import", StringComparison.Ordinal) ? 3 : 2, data.LastRowUsed()!.RowNumber());
                // The notes sheet explains every column (headers on row 3, one row per column from row 4).
                Assert.Equal(headers, Enumerable.Range(4, headers.Count).Select(r => notes.Cell(r, 1).GetString()).ToList());
                Assert.All(Enumerable.Range(4, headers.Count), r => Assert.False(string.IsNullOrWhiteSpace(notes.Cell(r, 3).GetString())));
                if (language == "ar") Assert.Contains(headers, h => h.Any(ch => ch is >= '؀' and <= 'ۿ'));
            }

            // Every column is matched by the import (nothing unused), and the template imports as it is.
            if (path is not ("/accounting/accounts/import" or "/accounting/journal-entries/import"))
            {
                var preview = await (await c.PostAsync(U(o, path + "/preview"), Form(bytes, path))).Content.ReadFromJsonAsync<ColumnsOnly>();
                Assert.Empty(preview!.Columns.Unused);
            }
            var imported = await c.PostAsync(U(o, path), Form(bytes, path));
            await imported.EnsureOkAsync();
        }
        Assert.Single((await c.GetFromJsonAsync<TripPage>(U(o, "/trips")))!.Page.Items);
    }

    [Fact]
    public async Task Templates_need_the_permission_of_their_import()
    {
        var o = await f.SignUpOwnerAsync();
        var anonymous = f.NewClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymous.GetAsync(U(o, "/trips/import/template"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, "/nothing/import/template"))).StatusCode);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string path)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var form = new MultipartFormDataContent { { content, "file", "template.xlsx" } };
        if (path == "/trips/import") form.Add(new StringContent("strict"), "mode");
        return form;
    }

    private sealed record ColumnsOnly(ImportColumns Columns);
}
