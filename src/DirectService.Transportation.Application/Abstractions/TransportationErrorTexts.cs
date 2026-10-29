using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>The messages of <see cref="ErrorCatalog"/> and the fixes of <see cref="ImportFixCatalog"/> for <see cref="IErrorText"/> (DT-315, DT-342).</summary>
public sealed class TransportationErrorTexts : IErrorTextProvider
{
    public bool IsKnown(string code) => ErrorCatalog.IsKnown(code);

    public string Format(string code, string language, params object?[] args) => ErrorCatalog.Format(code, language, args);

    public bool HasFix(string codeOrFixKey) => ImportFixCatalog.All.ContainsKey(codeOrFixKey);

    public string Fix(string codeOrFixKey, string language) => ImportFixCatalog.For(codeOrFixKey, language);
}
