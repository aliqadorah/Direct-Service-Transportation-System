namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>Journal entry status (Posted, Reversed, Void) → status tag.</summary>
public static class JournalStatus
{
    public static string Tag(string status) => status switch
    {
        "Reversed" => "reversed",
        "Void" => "void",
        _ => "posted",
    };
}
