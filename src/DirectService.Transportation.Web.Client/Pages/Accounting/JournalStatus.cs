namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>Journal entry status (Posted, Reversed, Void; Draft for a journal draft, revision 6) → status tag.</summary>
public static class JournalStatus
{
    public const string Draft = "Draft";

    /// <summary>DT-279: the list view (and API status filter) of every cancelled entry — Reversed or Void.</summary>
    public const string Cancelled = "Cancelled";

    public static string Tag(string status) => status switch
    {
        "Reversed" => "reversed",
        "Void" => "void",
        Draft => "draft",
        _ => "posted",
    };
}
