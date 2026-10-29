using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Platform;

/// <summary>The current company for the shared engines (<see cref="ICompanyInfo"/>, DT-321); moves to Platform with the company services.</summary>
public sealed class CompanyInfo(IAppDbContext db, IRequestContext ctx) : ICompanyInfo
{
    public Task<CompanyHeader> CurrentAsync(CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId)
            .Select(c => new CompanyHeader(c.Name, c.NameEn, c.TaxNumber, c.CommercialRegNo, c.Address, c.Phone, c.LogoFile, c.CurrencyCode, c.CurrencyDecimals, c.TimeZone))
            .SingleAsync(ct);

    public Task<string?> TimeZoneAsync(CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => (string?)c.TimeZone).SingleOrDefaultAsync(ct);

    /// <summary>The print header of a company row a service has already read.</summary>
    public static CompanyHeader Of(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return new CompanyHeader(company.Name, company.NameEn, company.TaxNumber, company.CommercialRegNo, company.Address, company.Phone, company.LogoFile,
            company.CurrencyCode, company.CurrencyDecimals, company.TimeZone);
    }
}
