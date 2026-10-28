using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>The messages of <see cref="ErrorCatalog"/> and the fixes of <see cref="ImportFixCatalog"/> for Shared code (<see cref="IErrorText"/>, DT-315).</summary>
public sealed class ErrorText : IErrorText
{
    public string Format(string code, string language, params object?[] args) => ErrorCatalog.Format(code, language, args);

    public bool IsKnown(string code) => ErrorCatalog.IsKnown(code);

    public string Fix(string codeOrFixKey, string language) => ImportFixCatalog.For(codeOrFixKey, language);
}
