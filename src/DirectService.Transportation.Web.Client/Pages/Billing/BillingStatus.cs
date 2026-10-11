namespace DirectService.Transportation.Web.Client.Pages.Billing;

/// <summary>API status codes of billing and cash documents → status tags of the board (<see cref="Ui.Statuses"/>).</summary>
public static class BillingStatus
{
    /// <summary>Invoice payment state (7.3.2): Draft, Unpaid, PartiallyPaid, PartiallyReturned, Closed, Void.</summary>
    public static string Invoice(string paymentState) => paymentState switch
    {
        "Draft" => "draft",
        "Unpaid" => "unpaid",
        "PartiallyPaid" => "partial",
        "PartiallyReturned" => "partReturned",
        "Closed" => "closed",
        "Void" => "void",
        _ => paymentState,
    };

    /// <summary>E-invoice status (B7): NotRequired, Queued, Submitted, Rejected.</summary>
    public static string EInvoice(string? status) => status switch
    {
        "Queued" => "queued",
        "Submitted" => "submitted",
        "Rejected" => "rejected",
        _ => "notRequired",
    };

    /// <summary>Cash documents and credit notes: Active / Posted → posted, Void → void.</summary>
    public static string Document(string status) => status is "Void" ? "void" : "posted";
}
