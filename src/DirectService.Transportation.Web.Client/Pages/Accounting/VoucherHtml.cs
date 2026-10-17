using System.Globalization;
using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.Web.Client.Pages.Accounting;

/// <summary>
/// The printable journal voucher (FR-103) from the API's voucher data: company, number, date, description, lines with accounts, cost centres
/// and parties, totals, amount in words in the reader's language and signature boxes; A4, in the reader's direction, every value encoded.
/// </summary>
public static class VoucherHtml
{
    public static string Build(JournalVoucherResponse v, Loc l, string dateFormat)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(l);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        static string N(string s) => "<bdi dir=\"ltr\">" + WebUtility.HtmlEncode(s) + "</bdi>";
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{l.Language}\" dir=\"{l.Dir}\"><head><meta charset=\"utf-8\"><title>{E(v.Number)}</title><style>")
         .Append("@page{size:A4;margin:14mm}body{font-family:'IBM Plex Sans Arabic','Segoe UI',Tahoma,sans-serif;font-size:11pt;color:#1B2229;margin:0}")
         .Append("h1{font-size:15pt;text-align:center;margin:8px 0}.meta{text-align:center;margin-bottom:10px}table{width:100%;border-collapse:collapse}")
         .Append("th,td{border:1px solid #8A9592;padding:4px 6px;vertical-align:top}th{background:#E4EDEE}.n{text-align:end;white-space:nowrap}")
         .Append(".sig{display:flex;justify-content:space-between;margin-top:36px}.sig span{min-width:30%;border-top:1px solid #333;padding-top:4px;text-align:center}")
         .Append(".head{display:flex;justify-content:space-between;border-bottom:2px solid #0F3B44;padding-bottom:6px}.void{color:#B42318;font-weight:700;text-align:center}")
         .Append("</style></head><body>");
        b.Append("<div class=\"head\"><b>").Append(E(l.Pick(v.CompanyName, v.CompanyNameEn))).Append("</b><span>").Append(E(l["je.voucherTitle"])).Append("</span></div>");
        b.Append("<h1>").Append(E(l["je.voucherTitle"])).Append("</h1>");
        b.Append("<div class=\"meta\">").Append(E(l["doc.number"])).Append(' ').Append(N(v.Number)).Append(" · ").Append(E(l["trip.date"])).Append(' ')
         .Append(N(Fmt.Date(v.EntryDate, dateFormat))).Append("</div>");
        if (v.Status == "Void") b.Append("<div class=\"void\">").Append(E(l["st.void"])).Append("</div>");
        b.Append("<p><b>").Append(E(l["je.description"])).Append(":</b> ").Append(E(v.Description)).Append("</p>");
        if (!string.IsNullOrEmpty(v.Reference)) b.Append("<p><b>").Append(E(l["je.reference"])).Append(":</b> ").Append(E(v.Reference)).Append("</p>");
        b.Append("<table><thead><tr><th>#</th><th>").Append(E(l["je.account"])).Append("</th><th>").Append(E(l["je.costCentre"])).Append("</th><th>")
         .Append(E(l["je.party"])).Append("</th><th>").Append(E(l["drv.note"])).Append("</th><th class=\"n\">").Append(E(l["je.debit"])).Append("</th><th class=\"n\">")
         .Append(E(l["je.credit"])).Append("</th></tr></thead><tbody>");
        foreach (var line in v.Lines)
        {
            b.Append("<tr><td>").Append(N(line.LineNo.ToString(CultureInfo.InvariantCulture))).Append("</td><td>").Append(N(line.Account.Code)).Append(' ')
             .Append(E(l.Pick(line.Account.NameAr, line.Account.NameEn))).Append("</td><td>").Append(E(line.CostCentre?.Name)).Append("</td><td>")
             .Append(E(line.Party?.Name)).Append("</td><td>").Append(E(line.Note)).Append("</td><td class=\"n\">").Append(line.Debit == 0 ? "" : N(Fmt.Money(line.Debit)))
             .Append("</td><td class=\"n\">").Append(line.Credit == 0 ? "" : N(Fmt.Money(line.Credit))).Append("</td></tr>");
        }
        b.Append("</tbody><tfoot><tr><th colspan=\"5\">").Append(E(l["ui.total"])).Append(' ').Append(N(v.CurrencyCode)).Append("</th><th class=\"n\">")
         .Append(N(Fmt.Money(v.TotalDebit))).Append("</th><th class=\"n\">").Append(N(Fmt.Money(v.TotalCredit))).Append("</th></tr></tfoot></table>");
        b.Append("<p>").Append(E(l.F("doc.inWords", l.En ? v.AmountInWordsEn : v.AmountInWordsAr))).Append("</p>");
        b.Append("<div class=\"sig\">");
        foreach (var s in v.Signatures) b.Append("<span>").Append(E(s)).Append("</span>");
        b.Append("</div></body></html>");
        return b.ToString();
    }
}
