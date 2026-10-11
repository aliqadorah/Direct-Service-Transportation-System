namespace DirectService.Transportation.Web.Client.Layout;

/// <summary>A command-bar entry: text (e.g. "TRP-10481 · أحمد صالح"), its kind ("رحلة", "قائمة") and where it leads.</summary>
public sealed record CommandResult(string Text, string Kind, string Href);
