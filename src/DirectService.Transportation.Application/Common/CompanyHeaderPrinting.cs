using DirectService.Transportation.Application.Platform;
using DirectService.Platform.Domain;

namespace DirectService.Transportation.Application.Common;

/// <summary>The printed company header from a company row (the printer itself takes a <see cref="CompanyHeader"/>).</summary>
public static class CompanyHeaderPrinting
{
    public static string Header(this DocumentPrinter printer, Company company, bool showLogo = true, string? band = null)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.Header(CompanyInfo.Of(company), showLogo, band);
    }
}
