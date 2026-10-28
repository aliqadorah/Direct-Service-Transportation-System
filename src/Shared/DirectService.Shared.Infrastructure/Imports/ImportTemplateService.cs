namespace DirectService.Shared.Infrastructure.Imports;

/// <summary>
/// "Download template" of every importer (owner feedback 13 Oct 2026): an Excel file in the user's language whose first sheet has
/// exactly the columns the import reads (headers it matches, the company's field labels and custom fields included), one sample row
/// (a journal entry needs two lines), and a second sheet explaining each column: required or not, format, allowed values. Replacing the
/// sample with real values gives a file that imports cleanly. The columns of each kind come from the modules'
/// <see cref="IImportTemplateProvider"/>s; this engine lays out the file (Stage 2 session 7, DT-317).
/// </summary>
public sealed class ImportTemplateService(IRequestContext ctx, ISpreadsheetService sheets, IEnumerable<IImportTemplateProvider> providers)
{
    private bool En => ctx.Language == "en";

    private string T(string ar, string en) => En ? en : ar;

    /// <summary>Every kind with a template, in the providers' order.</summary>
    public IReadOnlyList<string> Kinds => [.. providers.SelectMany(p => p.Kinds)];

    public async Task<GeneratedFile> TemplateAsync(string kind, CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(p => p.Kinds.Contains(kind)) ?? throw Fail.NotFound();
        var content = await provider.ContentAsync(kind, ct).ConfigureAwait(false);
        var cols = content.Columns;
        var samples = new List<IReadOnlyList<object?>> { cols.Select(c => c.Sample).ToList() };
        if (content.SecondSample is not null) samples.Add(content.SecondSample.Select(c => c.Sample).ToList());
        var template = new SpreadsheetTemplate(content.Title, T("شرح الأعمدة", "Column notes"), !En, cols.Select(c => c.Header).ToList(), cols.Select(c => c.Required).ToList(),
            samples, Intro(content.ReplaceSample), [T("العمود", "Column"), T("مطلوب", "Required"), T("الصيغة", "Format"), T("القيم المسموحة / ملاحظات", "Allowed values / notes")],
            cols.Select(c => (IReadOnlyList<string>)[c.Header, c.Required ? T("نعم", "Yes") : T("لا", "No"), c.Format, c.Allowed]).ToList());
        return new GeneratedFile($"{kind}-template-{(En ? "en" : "ar")}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteTemplate(template));
    }

    private string Intro(string? replaceSample) =>
        T("الصف الأول عناوين الأعمدة كما يقرؤها الاستيراد: لا تغيّرها. الأعمدة الملوّنة بالبرتقالي مطلوبة. استبدل الصف المثال (", "The first row holds the column headers the import reads: do not change them. Orange columns are required. Replace the sample row") +
        (replaceSample ?? T(") ببياناتك، صفاً لكل سجل.", " with your data, one row per record.")) +
        T(" التواريخ yyyy-mm-dd والأرقام بنقطة عشرية. الحد 20,000 صف و10 ميغابايت؛ القيم فقط بلا معادلات.", " Dates as yyyy-mm-dd, numbers with a dot for decimals. Up to 20,000 rows and 10 MB; values only, no formulas.");
}
