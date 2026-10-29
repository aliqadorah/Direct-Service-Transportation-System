namespace DirectService.SharedKernel;

/// <summary>
/// The Arabic or English message of a problem code, for Shared code that writes messages itself (import previews, export and report
/// problems). The codes stay where they are defined (<see cref="SharedErrorCodes"/> or a module's codes) and the texts in the error
/// catalogue of the application, unchanged; this interface only lets Shared read them without the module Contracts (Stage 2 session 7,
/// DT-315).
/// </summary>
public interface IErrorText
{
    /// <summary>The message of the code in the language ("ar" or "en"); a <c>FieldRef</c> argument is shown by its label. Unknown code → the code.</summary>
    string Format(string code, string language, params object?[] args);

    /// <summary>True when the code has messages.</summary>
    bool IsKnown(string code);

    /// <summary>How to fix a rejected import row: the fix of the code or fix key, else the default fix.</summary>
    string Fix(string codeOrFixKey, string language);
}

/// <summary>
/// The messages and import fixes a module contributes to <see cref="IErrorText"/> (Stage 2 session 12, DT-342). The Platform implementation asks
/// the provider that knows the code; with one provider (today the application's error catalogue) every answer is that provider's.
/// </summary>
public interface IErrorTextProvider
{
    /// <summary>True when the code has messages here.</summary>
    bool IsKnown(string code);

    /// <summary>The message of a code this provider knows, in the language ("ar" or "en").</summary>
    string Format(string code, string language, params object?[] args);

    /// <summary>True when the code or fix key has its own import fix here.</summary>
    bool HasFix(string codeOrFixKey);

    /// <summary>The import fix of the code or fix key, else this provider's default fix.</summary>
    string Fix(string codeOrFixKey, string language);
}
