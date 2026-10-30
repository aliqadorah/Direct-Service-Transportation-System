using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Companies;

/// <summary>The current company for the shared engines (<see cref="ICompanyInfo"/>, DT-321; Platform since Stage 2 session 12).</summary>
public sealed class CompanyInfo(IAppDbContext db, IRequestContext ctx) : ICompanyInfo
{
    public Task<CompanyHeader> CurrentAsync(CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId)
            .Select(c => new CompanyHeader(c.Name, c.NameEn, c.TaxNumber, c.CommercialRegNo, c.Address, c.Phone, c.LogoFile, c.CurrencyCode, c.CurrencyDecimals, c.TimeZone))
            .SingleAsync(ct);

    public Task<string?> TimeZoneAsync(CancellationToken ct) =>
        db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => (string?)c.TimeZone).SingleOrDefaultAsync(ct);
}
