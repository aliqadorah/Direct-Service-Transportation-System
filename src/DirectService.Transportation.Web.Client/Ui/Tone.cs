namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>Semantic tone of badges, banners and toasts (colour is never the only signal: an icon and text always go with it).</summary>
public enum Tone
{
    Info,
    Ok,
    Warn,
    Err,
    Mute,
    Primary,
    Lock,
}

public static class ToneCss
{
    public static string Badge(Tone t) => t switch
    {
        Tone.Ok => "b-ok",
        Tone.Warn => "b-warn",
        Tone.Err => "b-err",
        Tone.Mute or Tone.Lock => "b-mute",
        Tone.Primary => "b-pri",
        _ => "b-info",
    };

    public static string Banner(Tone t) => t switch
    {
        Tone.Ok => "ok",
        Tone.Warn => "warn",
        Tone.Err => "err",
        Tone.Lock or Tone.Mute => "lock",
        _ => "info",
    };

    public static string Icon(Tone t) => t switch
    {
        Tone.Ok => "check",
        Tone.Lock => "lock",
        Tone.Info or Tone.Primary or Tone.Mute => "info",
        _ => "alert",
    };
}

/// <summary>
/// Status tags of the approved board: key → tone and icon; the text is <c>st.&lt;key&gt;</c> in <see cref="Localization.UiText"/>.
/// Screens map API status codes to these keys (e.g. trip "Unbilled" → <c>unbilled</c>, period "Closed" → <c>closedPeriod</c>).
/// </summary>
public static class Statuses
{
    public static readonly IReadOnlyDictionary<string, (Tone Tone, string? Icon)> All = new Dictionary<string, (Tone, string?)>(StringComparer.Ordinal)
    {
        ["unbilled"] = (Tone.Info, "clock"), ["invoiced"] = (Tone.Ok, "check"), ["settled"] = (Tone.Mute, "check"), ["void"] = (Tone.Err, "x"),
        ["short"] = (Tone.Warn, "scale"), ["breach"] = (Tone.Err, "alert"), ["manual"] = (Tone.Warn, "edit"), ["review"] = (Tone.Warn, "alert"),
        ["draft"] = (Tone.Mute, "edit"), ["unpaid"] = (Tone.Info, "clock"), ["partial"] = (Tone.Warn, "clock"), ["partReturned"] = (Tone.Warn, "refresh"), ["closed"] = (Tone.Ok, "check"),
        ["posted"] = (Tone.Ok, "check"), ["pending"] = (Tone.Warn, "clock"), ["reversed"] = (Tone.Mute, "refresh"),
        ["active"] = (Tone.Ok, "check"), ["archived"] = (Tone.Mute, "archive"), ["open"] = (Tone.Ok, "check"), ["closedPeriod"] = (Tone.Mute, "lock"),
        ["queued"] = (Tone.Info, "clock"), ["submitted"] = (Tone.Ok, "check"), ["rejected"] = (Tone.Err, "x"), ["notRequired"] = (Tone.Mute, null),
        ["mapped"] = (Tone.Ok, "check"), ["missing"] = (Tone.Err, "alert"), ["disabled"] = (Tone.Mute, "lock"), ["invited"] = (Tone.Info, "mail"),
        ["trial"] = (Tone.Info, "clock"), ["pastDue"] = (Tone.Warn, "alert"), ["suspended"] = (Tone.Err, "lock"), ["cancelled"] = (Tone.Mute, "x"),
        ["failed"] = (Tone.Err, "alert"), ["paid"] = (Tone.Ok, "check"), ["due"] = (Tone.Info, "clock"), ["requested"] = (Tone.Info, "clock"),
        ["approved"] = (Tone.Ok, "check"), ["expired"] = (Tone.Mute, "clock"), ["credited"] = (Tone.Mute, "refresh"), ["deleted"] = (Tone.Mute, "trash"),
    };
}
