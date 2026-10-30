using DirectService.Platform.Domain;

namespace DirectService.Transportation.Application.Common;

/// <summary>The printed company header from a company row (the printer itself takes a <see cref="CompanyHeader"/>).</summary>
public static class CompanyHeaderPrinting
{
    public static string Header(this DocumentPrinter printer, Company company, bool showLogo = true, string? band = null)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.Header(Of(company), showLogo, band);
    }

    /// <summary>The print header of a company row a service has already read (was <c>CompanyInfo.Of</c> of Platform.Application, Stage 2 session 14).</summary>
    public static CompanyHeader Of(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return new CompanyHeader(company.Name, company.NameEn, company.TaxNumber, company.CommercialRegNo, company.Address, company.Phone, company.LogoFile,
            company.CurrencyCode, company.CurrencyDecimals, company.TimeZone);
    }
}
