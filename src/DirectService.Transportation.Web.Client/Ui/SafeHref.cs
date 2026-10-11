namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>Links built from data (toasts, search results) may only point inside the application.</summary>
public static class SafeHref
{
    public static string? Of(string? href) =>
        href is { Length: > 0 } h && h[0] == '/' && !h.StartsWith("//", StringComparison.Ordinal) && !h.StartsWith("/\\", StringComparison.Ordinal) ? h : null;
}
