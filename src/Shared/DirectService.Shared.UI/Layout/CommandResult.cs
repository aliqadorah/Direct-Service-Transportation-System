namespace DirectService.Shared.UI.Layout;

/// <summary>
/// A command-bar entry: text (e.g. "TRP-10481 · أحمد صالح"), its kind ("رحلة", "قائمة") and where it leads. <paramref name="Terms"/> is what a
/// typed text is also matched against (menu pages: their name in Arabic and English, DT-239).
/// </summary>
public sealed record CommandResult(string Text, string Kind, string Href, string? Terms = null);
