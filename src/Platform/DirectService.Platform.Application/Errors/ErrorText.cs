namespace DirectService.Platform.Application.Errors;

/// <summary>
/// <see cref="IErrorText"/> over the modules' <see cref="IErrorTextProvider"/>s (DT-315; Platform since Stage 2 session 12, DT-342): a code's
/// message comes from the provider that knows it (an unknown code is returned as it is), a fix from the provider that has it, else the first
/// provider's default fix.
/// </summary>
public sealed class ErrorText(IEnumerable<IErrorTextProvider> providers) : IErrorText
{
    private readonly IErrorTextProvider[] providers = [.. providers];

    public string Format(string code, string language, params object?[] args) =>
        providers.FirstOrDefault(p => p.IsKnown(code)) is { } provider ? provider.Format(code, language, args) : code;

    public bool IsKnown(string code) => providers.Any(p => p.IsKnown(code));

    public string Fix(string codeOrFixKey, string language) =>
        (providers.FirstOrDefault(p => p.HasFix(codeOrFixKey)) ?? providers[0]).Fix(codeOrFixKey, language);
}
