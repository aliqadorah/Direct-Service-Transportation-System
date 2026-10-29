namespace DirectService.SharedKernel;

/// <summary>An Arabic / English pair of the vocabulary.</summary>
public readonly record struct Words(string Ar, string En)
{
    public string In(bool en) => en ? En : Ar;
}

/// <summary>
/// The shapes of the readable history's vocabulary (owner feedback 14 Oct 2026, DT-188) and the wording rules that are the same for every
/// module. Each module brings its words through an <see cref="IAuditVocabularyProvider"/> (Stage 2 session 9).
/// </summary>
public static class AuditWords
{
    /// <summary>A record type: its title (record-type column, filter) and the noun used in a sentence ("the trip", "الرحلة").</summary>
    public sealed record EntityWords(Words Title, Words Noun);

    /// <summary>
    /// An action: its short label (action column, filter) and the sentence. {r} is the record ("the trip R-1001"), {d} the document the
    /// action names ("draft invoice INV-00001"), {x} the extra detail (a reason, a change of name). The Arabic sentence is a verbal noun, so
    /// it never guesses the gender of the user who acted.
    /// </summary>
    public sealed record ActionWords(Words Label, Words Sentence);

    /// <summary>How a field's value is shown.</summary>
    public enum Kind { Auto, Hidden, Text, Code, Money, Percent, Number, Date, DateTime, Bool, Ref, Custom, Masked }

    public sealed record FieldWords(Words Label, Kind Kind = Kind.Auto, string? Target = null);

    /// <summary>The words of a record type no module knows: its last part ("Widget").</summary>
    public static EntityWords UnknownEntity(string? type)
    {
        var name = type?.Split('.').LastOrDefault() ?? "";
        return new(new(name, name), new(name, name));
    }

    /// <summary>The words of an action no module knows: split into words ("Something happened").</summary>
    public static ActionWords UnknownAction(string action)
    {
        var words = Humanize(action);
        return new(new(words, words), new(words + " {r}", words.ToLowerInvariant() + " {r}"));
    }

    /// <summary>The event as a sentence: "Ali Ali added the trip…" / "علي علي: إضافة الرحلة…" (the Arabic verbal noun needs no gender).</summary>
    public static string Sentence(string? user, string predicate, bool en) => en ? $"{user ?? "The system"} {predicate}" : $"{user ?? "النظام"}: {predicate}";

    /// <summary>"SomeCode" → "Some code".</summary>
    public static string Humanize(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(code[i - 1])) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Whether a text looks like a document number ("INV-00001"): digits and no space.</summary>
    public static bool LooksLikeNumber(string? text) => text is not null && text.Length > 0 && text.Any(char.IsDigit) && !text.Contains(' ', StringComparison.Ordinal);
}
