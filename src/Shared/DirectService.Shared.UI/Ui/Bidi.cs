using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DirectService.Shared.UI.Ui;

/// <summary>
/// Bidi isolation (owner fix 1 of step 6): every left-to-right run inside Arabic text — codes such as TRP-10481 or INV-02210, dates,
/// amounts, IBANs, Latin words such as "Volvo FH" — is rendered as ONE isolated unit (<c>&lt;bdi class="num"&gt;</c>), so it keeps its
/// internal order in RTL. Runs of Latin tokens joined by spaces, "·" or "/" stay together. Same expression as the approved board.
/// </summary>
public static partial class Bidi
{
    private const string Token = @"[A-Za-z0-9](?:[A-Za-z0-9.,:%٪+\-−–_@']*[A-Za-z0-9%٪])?";

    [GeneratedRegex("(" + Token + @"(?:(?:\s*[·/]\s*|\s+)" + Token + ")*)", RegexOptions.CultureInvariant)]
    private static partial Regex Run();

    /// <summary>Splits text into (text, isLtrRun) parts.</summary>
    public static IEnumerable<(string Text, bool Ltr)> Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var at = 0;
        foreach (Match m in Run().Matches(text))
        {
            if (m.Index > at) yield return (text[at..m.Index], false);
            yield return (m.Value, true);
            at = m.Index + m.Length;
        }
        if (at < text.Length) yield return (text[at..], false);
    }

    /// <summary>Writes the text with its left-to-right runs isolated.</summary>
    public static void Write(RenderTreeBuilder b, int seq, string? text)
    {
        ArgumentNullException.ThrowIfNull(b);
        foreach (var (part, ltr) in Split(text))
        {
            if (!ltr) { b.AddContent(seq, part); continue; }
            b.OpenElement(seq + 1, "bdi");
            b.AddAttribute(seq + 2, "class", "num");
            b.AddContent(seq + 3, part);
            b.CloseElement();
        }
    }

    /// <summary>A fragment for use in markup: <c>@Bidi.Text(name)</c>.</summary>
    public static RenderFragment Text(string? text) => b => Write(b, 0, text);
}
