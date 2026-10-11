using System.Text;

namespace DirectService.Transportation.Application.Common;

/// <summary>
/// The brand fonts (IBM Plex Sans Arabic, Inter — SIL OFL) as <c>@font-face</c> rules with embedded data: URIs, so printouts and PDFs
/// use the same typefaces as the screens without any network request (the PDF engine loads only data: resources). Built once.
/// </summary>
public static class BrandFonts
{
    private const string ArabicRange = "U+0600-06FF, U+0750-077F, U+0870-088E, U+0890-0891, U+0897-08E1, U+08E3-08FF, U+200C-200E, U+2010-2011, U+204F, U+2E41, U+FB50-FDFF, U+FE70-FE74, U+FE76-FEFC";
    private const string LatinRange = "U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD";

    private static readonly Lazy<string> Faces = new(Build);

    public static string Css => Faces.Value;

    private static string Build()
    {
        var sb = new StringBuilder();
        foreach (var w in new[] { 400, 600, 700 })
        {
            Face(sb, "IBM Plex Sans Arabic", w, $"ibm-plex-sans-arabic-arabic-{w}-normal.woff2", ArabicRange);
            Face(sb, "IBM Plex Sans Arabic", w, $"ibm-plex-sans-arabic-latin-{w}-normal.woff2", LatinRange);
            Face(sb, "Inter", w, $"inter-latin-{w}-normal.woff2", null);
        }
        return sb.ToString();
    }

    private static void Face(StringBuilder sb, string family, int weight, string file, string? range)
    {
        using var s = typeof(BrandFonts).Assembly.GetManifestResourceStream("fonts/" + file);
        if (s is null) return; // a missing file falls back to the next font in the stack
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        sb.Append("@font-face{font-family:'").Append(family).Append("';font-style:normal;font-weight:").Append(weight)
          .Append(";src:url(data:font/woff2;base64,").Append(Convert.ToBase64String(ms.ToArray())).Append(") format('woff2')");
        if (range is not null) sb.Append(";unicode-range:").Append(range);
        sb.Append("}\n");
    }
}
