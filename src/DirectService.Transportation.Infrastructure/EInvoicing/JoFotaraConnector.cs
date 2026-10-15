using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.EInvoicing;

/// <summary>JoFotara endpoints per environment (configurable; the published endpoint is used for both until ISTD confirms a test URL — R-01).</summary>
public sealed class JoFotaraOptions
{
    public const string Section = "EInvoicing:JoFotara";
    public const string DefaultUrl = "https://backend.jofotara.gov.jo/core/invoices/";

    public string TestUrl { get; set; } = DefaultUrl;
    public string ProductionUrl { get; set; } = DefaultUrl;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// The launch-market format (INT-01): Jordan's national e-invoicing system JoFotara, UBL 2.1. Built from the ISTD technical guide as
/// described by public secondary sources (guide v1.5): invoice type 388 / return 381 with the three-digit name (trade type 0 = local,
/// payment 1 cash / 2 receivable, tax family 1 income / 2 general sales / 3 special); ICV in AdditionalDocumentReference; the
/// income-source sequence in SellerSupplierParty; discounts per line (header allowance = their sum); tax per line; amounts with
/// <c>currencyID="JO"</c> as in the guide's samples; returns reference the original invoice (BillingReference) and carry the reason
/// (PaymentMeans). Field-level details are to be confirmed against the official guide and the test environment (TST-04, R-01).
/// </summary>
public static class JoFotaraUbl
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    /// <summary>Governorate codes (ISO 3166-2:JO) from the customer's city, Arabic or English.</summary>
    private static readonly Dictionary<string, string> Governorates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["عمان"] = "JO-AM", ["عمّان"] = "JO-AM", ["amman"] = "JO-AM", ["اربد"] = "JO-IR", ["إربد"] = "JO-IR", ["irbid"] = "JO-IR",
        ["الزرقاء"] = "JO-AZ", ["الزرقا"] = "JO-AZ", ["zarqa"] = "JO-AZ", ["البلقاء"] = "JO-BA", ["السلط"] = "JO-BA", ["balqa"] = "JO-BA", ["salt"] = "JO-BA",
        ["المفرق"] = "JO-MA", ["mafraq"] = "JO-MA", ["الكرك"] = "JO-KA", ["karak"] = "JO-KA", ["الطفيلة"] = "JO-AT", ["tafilah"] = "JO-AT", ["tafila"] = "JO-AT",
        ["معان"] = "JO-MN", ["maan"] = "JO-MN", ["ma'an"] = "JO-MN", ["العقبة"] = "JO-AQ", ["aqaba"] = "JO-AQ", ["جرش"] = "JO-JA", ["jerash"] = "JO-JA",
        ["عجلون"] = "JO-AJ", ["ajloun"] = "JO-AJ", ["مادبا"] = "JO-MD", ["madaba"] = "JO-MD",
    };

    public static string TypeName(EInvoiceDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var family = d.RegistrationType switch { EInvoiceCodes.Income => "1", EInvoiceCodes.Special => "3", _ => "2" };
        return "0" + (d.Receivable ? "2" : "1") + family;
    }

    public static string Build(EInvoiceDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var income = d.RegistrationType == EInvoiceCodes.Income;
        string A(decimal v) => v.ToString("F" + d.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        XElement Amount(string name, decimal v) => new(Cbc + name, new XAttribute("currencyID", "JO"), A(v));
        XElement TaxScheme() => new(Cac + "TaxScheme", new XElement(Cbc + "ID", new XAttribute("schemeAgencyID", "6"), new XAttribute("schemeID", "UN/ECE 5153"), "VAT"));

        var root = new XElement(Inv + "Invoice",
            new XAttribute("xmlns", Inv.NamespaceName), new XAttribute(XNamespace.Xmlns + "cac", Cac.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc.NamespaceName), new XAttribute(XNamespace.Xmlns + "ext", Ext.NamespaceName),
            new XElement(Cbc + "ProfileID", "reporting:1.0"),
            new XElement(Cbc + "ID", d.Number),
            new XElement(Cbc + "UUID", d.Uuid.ToString()),
            new XElement(Cbc + "IssueDate", d.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "InvoiceTypeCode", new XAttribute("name", TypeName(d)), d.Kind == EInvoiceCodes.CreditNote ? "381" : "388"),
            d.Kind == EInvoiceCodes.Invoice && !string.IsNullOrWhiteSpace(d.Note) ? new XElement(Cbc + "Note", d.Note) : null,
            new XElement(Cbc + "DocumentCurrencyCode", d.Currency),
            new XElement(Cbc + "TaxCurrencyCode", d.Currency),
            d.Original is { } o
                ? new XElement(Cac + "BillingReference", new XElement(Cac + "InvoiceDocumentReference",
                    new XElement(Cbc + "ID", o.Number), new XElement(Cbc + "UUID", o.Uuid.ToString()), new XElement(Cbc + "DocumentDescription", A(o.Total))))
                : null,
            new XElement(Cac + "AdditionalDocumentReference", new XElement(Cbc + "ID", "ICV"), new XElement(Cbc + "UUID", d.Icv.ToString(CultureInfo.InvariantCulture))),
            new XElement(Cac + "AccountingSupplierParty", new XElement(Cac + "Party",
                new XElement(Cac + "PostalAddress", new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "JO"))),
                new XElement(Cac + "PartyTaxScheme", new XElement(Cbc + "CompanyID", d.Seller.TaxNumber), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))),
                new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", d.Seller.Name)))),
            new XElement(Cac + "AccountingCustomerParty",
                new XElement(Cac + "Party",
                    d.Buyer.TaxNumber is { } tin ? new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", new XAttribute("schemeID", "TN"), tin)) : null,
                    new XElement(Cac + "PostalAddress",
                        Governorate(d.Buyer.City) is { } gov ? new XElement(Cbc + "CountrySubentityCode", gov) : null,
                        new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "JO"))),
                    new XElement(Cac + "PartyTaxScheme",
                        d.Buyer.TaxNumber is { } t2 ? new XElement(Cbc + "CompanyID", t2) : null,
                        new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))),
                    new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", d.Buyer.Name))),
                d.Buyer.Phone is { Length: >= 9 and <= 14 } phone ? new XElement(Cac + "AccountingContact", new XElement(Cbc + "Telephone", phone)) : null),
            new XElement(Cac + "SellerSupplierParty", new XElement(Cac + "Party", new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", d.IncomeSourceSequence)))),
            d.Kind == EInvoiceCodes.CreditNote
                ? new XElement(Cac + "PaymentMeans", new XElement(Cbc + "PaymentMeansCode", new XAttribute("listID", "UN/ECE 4461"), "10"),
                    new XElement(Cbc + "InstructionNote", d.Note))
                : null,
            new XElement(Cac + "AllowanceCharge", new XElement(Cbc + "ChargeIndicator", "false"), new XElement(Cbc + "AllowanceChargeReason", "discount"), Amount("Amount", d.Discount)),
            income ? null : new XElement(Cac + "TaxTotal", Amount("TaxAmount", d.Tax)),
            new XElement(Cac + "LegalMonetaryTotal",
                Amount("TaxExclusiveAmount", d.TaxExclusive),
                Amount("TaxInclusiveAmount", d.Payable),
                Amount("AllowanceTotalAmount", d.Discount),
                Amount("PayableAmount", d.Payable)),
            d.Lines.Select(l => new XElement(Cac + "InvoiceLine",
                new XElement(Cbc + "ID", l.Number.ToString(CultureInfo.InvariantCulture)),
                new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "PCE"), l.Quantity.ToString("0.###", CultureInfo.InvariantCulture)),
                Amount("LineExtensionAmount", l.NetAmount),
                income ? null : new XElement(Cac + "TaxTotal",
                    Amount("TaxAmount", l.Tax),
                    Amount("RoundingAmount", l.NetAmount + l.Tax),
                    new XElement(Cac + "TaxSubtotal",
                        Amount("TaxAmount", l.Tax),
                        new XElement(Cac + "TaxCategory",
                            new XElement(Cbc + "ID", new XAttribute("schemeAgencyID", "6"), new XAttribute("schemeID", "UN/ECE 5305"), l.TaxRate > 0 ? "S" : "Z"),
                            new XElement(Cbc + "Percent", l.TaxRate.ToString("0.##", CultureInfo.InvariantCulture)),
                            TaxScheme()))),
                new XElement(Cac + "Item", new XElement(Cbc + "Name", l.Description)),
                new XElement(Cac + "Price",
                    Amount("PriceAmount", l.UnitPrice),
                    new XElement(Cac + "AllowanceCharge", new XElement(Cbc + "ChargeIndicator", "false"), new XElement(Cbc + "AllowanceChargeReason", "DISCOUNT"),
                        Amount("Amount", l.Discount))))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Declaration + Environment.NewLine + root.ToString(SaveOptions.DisableFormatting);
    }

    private static string? Governorate(string? city) =>
        string.IsNullOrWhiteSpace(city) ? null : Governorates.TryGetValue(city.Trim(), out var code) ? code : null;

    /// <summary>JoFotara rules on top of the common checks: special tax not supported; income registrations carry no sales tax.</summary>
    public static IReadOnlyList<EInvoiceProblem> Validate(EInvoiceDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var problems = new List<EInvoiceProblem>();
        if (d.RegistrationType == EInvoiceCodes.Special) problems.Add(new("registrationType", ErrorCodes.EInvoiceSpecialNotSupported));
        if (d.RegistrationType == EInvoiceCodes.Income && d.Tax != 0) problems.Add(new("registrationType", ErrorCodes.EInvoiceIncomeWithTax));
        return problems;
    }
}

/// <summary>
/// JoFotara submission: POST {"invoice": base64(UBL)} with the company's Client-Id and Secret-Key headers. The final state is read from
/// EINV_STATUS (SUBMITTED / ALREADY_SUBMITTED = accepted; NOT_SUBMITTED = rejected, reasons in EINV_RESULTS.ERRORS / EINV_MESSAGE),
/// not from the HTTP code; accepted responses carry EINV_INV_UUID, EINV_NUM and EINV_QR. Network errors, timeouts, 429 and 5xx are
/// technical failures (retried); refused credentials are a rejection (retrying will not help). Credentials are never logged.
/// </summary>
public sealed class JoFotaraConnector(IHttpClientFactory http, IOptions<JoFotaraOptions> options) : IEInvoiceConnector
{
    public const string HttpClientName = "JoFotara";

    public string Name => EInvoiceCodes.JoFotara;

    public IReadOnlyList<EInvoiceProblem> Validate(EInvoiceDocument document) => JoFotaraUbl.Validate(document);

    public string Build(EInvoiceDocument document) => JoFotaraUbl.Build(document);

    public async Task<EInvoiceOutcome> SubmitAsync(string payload, EInvoiceCredentials credentials, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var o = options.Value;
        var url = credentials.Environment == EInvoiceCodes.Production ? o.ProductionUrl : o.TestUrl;
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { invoice = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) }),
        };
        request.Headers.Add("Client-Id", credentials.ClientId);
        request.Headers.Add("Secret-Key", credentials.SecretKey);
        var client = http.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Interpret(response.StatusCode, body);
    }

    /// <summary>Maps a response to an outcome (public for tests).</summary>
    public static EInvoiceOutcome Interpret(HttpStatusCode status, string body)
    {
        var code = (int)status;
        JsonElement? json = null;
        try
        {
            using var parsed = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            json = parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            // not JSON: decided by the HTTP code below
        }
        var einvStatus = json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("EINV_STATUS", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
        if (einvStatus is "SUBMITTED" or "ALREADY_SUBMITTED")
        {
            var r = json!.Value;
            return new EInvoiceOutcome(EInvoiceOutcomeKind.Accepted,
                Guid.TryParse(Text(r, "EINV_INV_UUID"), out var uuid) ? uuid : null, Text(r, "EINV_NUM"), Text(r, "EINV_QR"), einvStatus, body);
        }
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new EInvoiceOutcome(EInvoiceOutcomeKind.Rejected, null, null, null, $"{EInvoiceOutcome.CredentialsRefusedText} (HTTP {code}). {Reasons(json)}".Trim(), body, true);
        if (einvStatus == "NOT_SUBMITTED" || (code is >= 400 and < 500 and not 408 and not 429 && json is not null))
            return new EInvoiceOutcome(EInvoiceOutcomeKind.Rejected, null, null, null, Reasons(json) is { Length: > 0 } why ? why : $"HTTP {code}", body);
        return new EInvoiceOutcome(EInvoiceOutcomeKind.Failed, null, null, null, $"JoFotara technical error (HTTP {code}). {Reasons(json)}".Trim(), body);
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    /// <summary>Error messages from EINV_RESULTS.ERRORS (code: message) or EINV_MESSAGE.</summary>
    private static string Reasons(JsonElement? json)
    {
        if (json is not { ValueKind: JsonValueKind.Object } j) return string.Empty;
        var parts = new List<string>();
        if (j.TryGetProperty("EINV_RESULTS", out var results) && results.ValueKind == JsonValueKind.Object
            && results.TryGetProperty("ERRORS", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var msg = Text(e, "EINV_MESSAGE");
                var c = Text(e, "EINV_CODE");
                if (msg is not null) parts.Add(c is null ? msg : $"{c}: {msg}");
            }
        }
        if (parts.Count == 0 && Text(j, "EINV_MESSAGE") is { } m) parts.Add(m);
        return string.Join(" | ", parts);
    }
}

/// <summary>
/// Development and test connector (A-15): builds the same JoFotara UBL, checks that it adds up, and answers like JoFotara. Customer
/// names containing "[reject]" are refused and "[fail]" simulates a technical failure, and a client id containing "[refuse]" has its
/// credentials refused (HTTP 401), so every path can be exercised. Offered only
/// where EInvoicing:AllowSimulator is set, and never in the Production environment.
/// </summary>
public sealed class SimulatorConnector : IEInvoiceConnector
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public string Name => EInvoiceCodes.Simulator;

    public IReadOnlyList<EInvoiceProblem> Validate(EInvoiceDocument document) => JoFotaraUbl.Validate(document);

    public string Build(EInvoiceDocument document) => JoFotaraUbl.Build(document);

    public Task<EInvoiceOutcome> SubmitAsync(string payload, EInvoiceCredentials credentials, CancellationToken ct)
    {
        var x = XDocument.Parse(payload);
        decimal D(XElement? e) => decimal.Parse(e?.Value ?? "0", CultureInfo.InvariantCulture);
        var buyer = x.Descendants(Cac + "AccountingCustomerParty").Descendants(Cbc + "RegistrationName").FirstOrDefault()?.Value ?? string.Empty;
        var number = x.Root!.Element(Cbc + "ID")?.Value;
        var uuid = Guid.Parse(x.Root.Element(Cbc + "UUID")!.Value);
        var icv = x.Root.Elements(Cac + "AdditionalDocumentReference").Elements(Cbc + "UUID").FirstOrDefault()?.Value;
        var totals = x.Root.Element(Cac + "LegalMonetaryTotal")!;
        var lines = x.Root.Elements(Cac + "InvoiceLine").ToList();
        var lineTax = lines.Sum(l => D(l.Element(Cac + "TaxTotal")?.Element(Cbc + "TaxAmount")));
        var lineDiscount = lines.Sum(l => D(l.Element(Cac + "Price")?.Element(Cac + "AllowanceCharge")?.Element(Cbc + "Amount")));
        var headerTax = D(x.Root.Element(Cac + "TaxTotal")?.Element(Cbc + "TaxAmount"));
        var payable = D(totals.Element(Cbc + "PayableAmount"));
        var expected = D(totals.Element(Cbc + "TaxExclusiveAmount")) - D(totals.Element(Cbc + "AllowanceTotalAmount")) + headerTax;

        EInvoiceOutcome outcome;
        if (credentials.ClientId.Contains("[refuse]", StringComparison.OrdinalIgnoreCase))
            outcome = JoFotaraConnector.Interpret(HttpStatusCode.Unauthorized, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["EINV_RESULTS"] = new { status = "ERROR", ERRORS = new[] { new Dictionary<string, string> { ["EINV_CODE"] = "CORE-401-001", ["EINV_MESSAGE"] = "Invalid client credentials" } } },
            }));
        else if (buyer.Contains("[fail]", StringComparison.OrdinalIgnoreCase))
            outcome = new(EInvoiceOutcomeKind.Failed, null, null, null, "Simulated technical failure (HTTP 503).", null);
        else if (buyer.Contains("[reject]", StringComparison.OrdinalIgnoreCase))
            outcome = Rejected("E-INV-BUYER", "Simulated rejection: buyer data refused.");
        else if (lineTax != headerTax || lineDiscount != D(totals.Element(Cbc + "AllowanceTotalAmount")) || payable != expected)
            outcome = Rejected("E-INV-TOTALS", "Simulated rejection: totals do not add up.");
        else
        {
            var qr = Convert.ToBase64String(Encoding.UTF8.GetBytes($"SIMULATED|{number}|{payable.ToString(CultureInfo.InvariantCulture)}|{uuid}"));
            var body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["EINV_RESULTS"] = new { status = "PASS", INFO = Array.Empty<object>(), WARNINGS = Array.Empty<object>(), ERRORS = Array.Empty<object>() },
                ["EINV_STATUS"] = "SUBMITTED", ["EINV_QR"] = qr, ["EINV_NUM"] = $"SIM-{icv}", ["EINV_INV_UUID"] = uuid.ToString(),
            });
            outcome = JoFotaraConnector.Interpret(HttpStatusCode.OK, body);
        }
        return Task.FromResult(outcome);
    }

    private static EInvoiceOutcome Rejected(string code, string message) => JoFotaraConnector.Interpret(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["EINV_RESULTS"] = new { status = "ERROR", ERRORS = new[] { new Dictionary<string, string> { ["EINV_CODE"] = code, ["EINV_MESSAGE"] = message } } },
        ["EINV_STATUS"] = "NOT_SUBMITTED",
    }));
}
