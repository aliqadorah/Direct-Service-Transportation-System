using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same-origin API (/api/v1) with the auth cookie; the antiforgery header is added by the API client (step 8).
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddClientUi();
builder.Services.AddScoped<Api>();
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<FormConfig>();
builder.Services.AddScoped<TripLookups>();
builder.Services.AddScoped<Downloads>();
builder.Services.AddScoped<MoneyAccounts>();
builder.Services.AddScoped<AccountingLookups>();

await builder.Build().RunAsync().ConfigureAwait(false);
