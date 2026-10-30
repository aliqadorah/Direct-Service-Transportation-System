using System.Globalization;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Identity;
using DirectService.Platform.Contracts.Modules;
using DirectService.Transportation.Application.Platform;

namespace DirectService.Transportation.Application.Modules;

/// <summary>
/// The demo company's sample data (5.4, P-09, G-11) through the normal services: the approved chart and mapping, customers, a partner, materials,
/// sites, own-fleet and partner drivers, tariffs, this month's driver rates, trips, an approved invoice and a receipt (<see cref="IDemoDataContributor"/>,
/// moved unchanged out of <see cref="DemoCompanyService"/>, DT-346).
/// </summary>
public sealed class TransportationDemoData(IAppDbContext db, IUrlTokenService tokens, ChartOfAccountsService chart, CustomerService customers, PartnerService partners,
    MaterialService materials, LoadingSiteService loadingSites, UnloadingSiteService unloadingSites, OwnershipTypeService ownershipTypes, DriverService drivers,
    TariffService tariffs, MonthlyRateService rates, TripService trips, InvoiceService invoices, ReceiptService receipts, MasterDataSupport support) : IDemoDataContributor
{
    public async Task AddDemoDataAsync(CancellationToken ct)
    {
        await chart.ApplyTemplateAsync(ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var potash = (await customers.CreateAsync(new CustomerRequest("شركة البوتاس (تجريبي)", "100200300", null, "0781234567", null, "العقبة", "العقبة", null, 30, null, null, null, null, null), ct)
            .ConfigureAwait(false)).Value;
        var phosphate = (await customers.CreateAsync(new CustomerRequest("مناجم الفوسفات (تجريبي)", "100200301", null, "0781234568", null, "عمّان", "عمّان", null, 45, null, null, null, null, null), ct)
            .ConfigureAwait(false)).Value;
        var partner = (await partners.CreateAsync(new PartnerRequest("شركة النور للنقل (تجريبي)", null, null, "0791234567", null, null, null, null, null, null, null), ct).ConfigureAwait(false)).Value;
        var material = (await materials.CreateAsync(new MaterialRequest("بوتاس", "Potash", null, null), ct).ConfigureAwait(false)).Value;
        var loading = (await loadingSites.CreateAsync(new SiteRequest("غور الصافي", "الأغوار", null, null), ct).ConfigureAwait(false)).Value;
        var unloading = (await unloadingSites.CreateAsync(new SiteRequest("ميناء العقبة", "العقبة", null, null), ct).ConfigureAwait(false)).Value;
        var types = await ownershipTypes.ListAsync(ct).ConfigureAwait(false);
        var rented = (await ownershipTypes.CreateAsync(new OwnershipTypeRequest("مستأجرة", "External", null, null), ct).ConfigureAwait(false)).Value;
        DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId) =>
            new(name, nid, "0791234567", null, null, "L-" + nid[^3..], "C", today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                today.AddYears(1), today.AddYears(1), today.AddYears(1), null, null, null, null);
        var own1 = (await drivers.CreateAsync(Driver("أحمد صالح (تجريبي)", "9000000001", "10-90001", types.Single(t => t.IsBuiltIn).Id, null), ct).ConfigureAwait(false)).Value;
        var own2 = (await drivers.CreateAsync(Driver("محمود الحويطات (تجريبي)", "9000000002", "10-90002", types.Single(t => t.IsBuiltIn).Id, null), ct).ConfigureAwait(false)).Value;
        var ext = (await drivers.CreateAsync(Driver("سامي العبادي (تجريبي)", "9000000003", "10-90003", rented.Id, partner.Id), ct).ConfigureAwait(false)).Value;
        await tariffs.CreateAsync(new TariffRequest("Customer", potash.Id, material.Id, null, null, 6.000m, null, null, null, null, null), ct).ConfigureAwait(false);
        await tariffs.CreateAsync(new TariffRequest("Customer", phosphate.Id, null, null, null, 5.250m, null, null, null, null, null), ct).ConfigureAwait(false);
        await tariffs.CreateAsync(new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null), ct).ConfigureAwait(false);
        await rates.SaveAsync(today.ToString("yyyy-MM", CultureInfo.InvariantCulture), new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]), ct)
            .ConfigureAwait(false);
        var tripIds = new List<string>();
        var plan = new (DriverResponse Driver, string? Partner, CustomerResponse Customer, decimal Loaded, decimal Unloaded)[]
        {
            (own1, null, potash, 32.000m, 31.850m), (own2, null, potash, 31.500m, 31.400m), (ext, partner.Id, potash, 32.000m, 31.900m),
            (own1, null, phosphate, 30.000m, 29.950m), (own2, null, phosphate, 30.500m, 30.100m), (ext, partner.Id, phosphate, 31.000m, 31.000m),
        };
        var n = 0;
        foreach (var (driver, partnerId, customer, loaded, unloaded) in plan)
        {
            var saved = await trips.CreateAsync(new TripRequest("DEMO-" + (++n).ToString("D3", CultureInfo.InvariantCulture), today, null, null, driver.Id, customer.Id, loading.Id,
                unloading.Id, material.Id, partnerId, loaded, unloaded, null, null, null, null, null, null, null), ct).ConfigureAwait(false);
            if (customer.Id == potash.Id) tripIds.Add(saved.Value.Trip.Id);
        }
        var invoice = (await invoices.CreateAsync(new InvoiceRequest(potash.Id, "Trip", today, null, tripIds, null, 0, 0, null, null, null, true, null, null), ct)
            .ConfigureAwait(false)).Value;
        var box = await db.Set<CashBox>().AsNoTracking().Select(b => b.PublicId).FirstAsync(ct).ConfigureAwait(false);
        await receipts.CreateAsync(new ReceiptRequest(potash.Id, today, invoice.Invoice.Id, Math.Round(invoice.Invoice.Total / 2, 3), "Cash", tokens.Encode<CashBox>(box), null, null, 0,
            null, null), ct).ConfigureAwait(false);
    }
}
