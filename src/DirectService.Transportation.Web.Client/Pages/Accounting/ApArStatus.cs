using DirectService.Transportation.Contracts.ApAr;

namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>AP/AR document → status tag: void, closed (paid), partly paid, or open (unpaid).</summary>
public static class ApArStatus
{
    public static string Tag(ApArDocumentResponse d) => d.Status == ApArCodes.Void ? "void" : d.PaymentStatus switch
    {
        ApArCodes.Closed => "closed",
        ApArCodes.Partial => "partial",
        _ => "unpaid",
    };
}
