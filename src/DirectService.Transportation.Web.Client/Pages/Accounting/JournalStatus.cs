namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>Journal entry status (Posted, Reversed, Void; Draft for a journal draft, revision 6) → status tag.</summary>
public static class JournalStatus
{
    public const string Draft = "Draft";

    public static string Tag(string status) => status switch
    {
        "Reversed" => "reversed",
        "Void" => "void",
        Draft => "draft",
        _ => "posted",
    };
}
