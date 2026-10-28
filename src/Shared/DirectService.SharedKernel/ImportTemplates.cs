namespace DirectService.SharedKernel;

/// <summary>One column of an import template: header as the import matches it, required, format, allowed values / notes, sample value.</summary>
public sealed record ImportTemplateColumn(string Header, bool Required, string Format, string Allowed, object? Sample);

/// <summary>
/// What a module puts into the template of one import kind, in the reader's language: the data sheet's title, its columns with the sample
/// row, an optional second sample row (a journal entry needs two lines) and, when the default does not fit, the sentence telling what
/// replaces the sample row.
/// </summary>
public sealed record ImportTemplateContent(string Title, IReadOnlyList<ImportTemplateColumn> Columns, IReadOnlyList<ImportTemplateColumn>? SecondSample = null,
    string? ReplaceSample = null);

/// <summary>A module's import templates (design a.3; Stage 2 session 7, DT-317). The kind is the URL word of the importer ("trips", "accounts" …).</summary>
public interface IImportTemplateProvider
{
    IReadOnlyList<string> Kinds { get; }

    Task<ImportTemplateContent> ContentAsync(string kind, CancellationToken ct);
}
