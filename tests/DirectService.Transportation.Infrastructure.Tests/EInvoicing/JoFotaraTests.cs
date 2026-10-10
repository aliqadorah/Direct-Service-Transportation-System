using System.Net;
using System.Xml.Linq;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Infrastructure.EInvoicing;

namespace DirectService.Transportation.Infrastructure.Tests.EInvoicing;

/// <summary>B7: JoFotara UBL 2.1 document and response handling (format as described for the ISTD guide; to confirm on the test environment, R-01).</summary>
public sealed class JoFotaraTests
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static EInvoiceDocument Doc(string kind = EInvoiceCodes.Invoice, bool receivable = true, string registration = EInvoiceCodes.Sales) => new(
        kind, kind == EInvoiceCodes.Invoice ? "INV-00001" : "CRN-00001", Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), new DateOnly(2026, 10, 9), 7, "JOD", 3, receivable,
        registration, "4569874", new EInvoiceParty("Arab Transport & Co.", "12345678", null, null), new EInvoiceParty("Arab Potash <Ltd>", "200014587", "0781234567", "العقبة"),
        [new EInvoiceLine(1, "Trip T-1", 1m, 180m, 5m, 175m, 16m, 28m), new EInvoiceLine(2, "Fees", 1m, 5m, 0m, 5m, 16m, 0.8m)],
        185m, 5m, 28.8m, 208.8m, kind == EInvoiceCodes.CreditNote ? new EInvoiceReference("INV-00001", Guid.Parse("11111111-2222-3333-4444-555555555555"), 817.8m) : null,
        kind == EInvoiceCodes.CreditNote ? "Returned trip" : "PO 55");

    [Fact]
    public void Invoice_document_has_the_national_structure()
    {
        var x = XDocument.Parse(JoFotaraUbl.Build(Doc()));
        var root = x.Root!;
        Assert.Equal("Invoice", root.Name.LocalName);
        Assert.Equal("388", root.Element(Cbc + "InvoiceTypeCode")!.Value);
        Assert.Equal("022", root.Element(Cbc + "InvoiceTypeCode")!.Attribute("name")!.Value);   // local, receivable, general sales tax
        Assert.Equal("7", root.Element(Cac + "AdditionalDocumentReference")!.Element(Cbc + "UUID")!.Value);   // ICV
        Assert.Equal("4569874", root.Element(Cac + "SellerSupplierParty")!.Descendants(Cbc + "ID").Single().Value);
        Assert.Equal("JO-AQ", root.Descendants(Cbc + "CountrySubentityCode").Single().Value);
        Assert.Equal("Arab Potash <Ltd>", root.Element(Cac + "AccountingCustomerParty")!.Descendants(Cbc + "RegistrationName").Single().Value);   // escaped, round-trips
        var totals = root.Element(Cac + "LegalMonetaryTotal")!;
        Assert.Equal("185.000", totals.Element(Cbc + "TaxExclusiveAmount")!.Value);
        Assert.Equal("208.800", totals.Element(Cbc + "PayableAmount")!.Value);
        Assert.Equal("JO", totals.Element(Cbc + "PayableAmount")!.Attribute("currencyID")!.Value);
        Assert.Equal(2, root.Elements(Cac + "InvoiceLine").Count());
        Assert.Equal("S", root.Elements(Cac + "InvoiceLine").First().Descendants(Cac + "TaxCategory").Single().Element(Cbc + "ID")!.Value);
        Assert.Null(root.Element(Cac + "BillingReference"));
        Assert.Equal("012", JoFotaraUbl.TypeName(Doc(receivable: false)));
    }

    [Fact]
    public void Credit_note_references_the_original_and_carries_the_reason()
    {
        var root = XDocument.Parse(JoFotaraUbl.Build(Doc(EInvoiceCodes.CreditNote))).Root!;
        Assert.Equal("381", root.Element(Cbc + "InvoiceTypeCode")!.Value);
        var reference = root.Element(Cac + "BillingReference")!.Element(Cac + "InvoiceDocumentReference")!;
        Assert.Equal("INV-00001", reference.Element(Cbc + "ID")!.Value);
        Assert.Equal("11111111-2222-3333-4444-555555555555", reference.Element(Cbc + "UUID")!.Value);
        Assert.Equal("Returned trip", root.Element(Cac + "PaymentMeans")!.Element(Cbc + "InstructionNote")!.Value);
        Assert.Null(root.Element(Cbc + "Note"));
    }

    [Fact]
    public void Market_rules_special_tax_and_income_with_tax()
    {
        Assert.Contains(JoFotaraUbl.Validate(Doc(registration: EInvoiceCodes.Special)), p => p.Code == "einvoice.specialNotSupported");
        Assert.Contains(JoFotaraUbl.Validate(Doc(registration: EInvoiceCodes.Income)), p => p.Code == "einvoice.incomeWithTax");
        Assert.Empty(JoFotaraUbl.Validate(Doc()));
    }

    [Fact]
    public void Responses_are_judged_by_EINV_STATUS()
    {
        var ok = JoFotaraConnector.Interpret(HttpStatusCode.OK,
            """{"EINV_RESULTS":{"status":"PASS","ERRORS":[]},"EINV_STATUS":"SUBMITTED","EINV_QR":"QVJBQg==","EINV_NUM":"J-55","EINV_INV_UUID":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}""");
        Assert.Equal(EInvoiceOutcomeKind.Accepted, ok.Kind);
        Assert.Equal("J-55", ok.SystemNumber);
        Assert.Equal("QVJBQg==", ok.QrCode);
        Assert.Equal(EInvoiceOutcomeKind.Accepted, JoFotaraConnector.Interpret(HttpStatusCode.Conflict, """{"EINV_STATUS":"ALREADY_SUBMITTED"}""").Kind);

        var refused = JoFotaraConnector.Interpret(HttpStatusCode.BadRequest,
            """{"EINV_RESULTS":{"status":"ERROR","ERRORS":[{"EINV_CODE":"E-12","EINV_MESSAGE":"Buyer name missing"}]},"EINV_STATUS":"NOT_SUBMITTED"}""");
        Assert.Equal(EInvoiceOutcomeKind.Rejected, refused.Kind);
        Assert.Equal("E-12: Buyer name missing", refused.Message);
        Assert.Equal(EInvoiceOutcomeKind.Rejected, JoFotaraConnector.Interpret(HttpStatusCode.Unauthorized, "").Kind);
        Assert.Equal(EInvoiceOutcomeKind.Failed, JoFotaraConnector.Interpret(HttpStatusCode.BadGateway, "<html>bad gateway</html>").Kind);
        Assert.Equal(EInvoiceOutcomeKind.Failed, JoFotaraConnector.Interpret(HttpStatusCode.TooManyRequests, "{}").Kind);
    }

    [Fact]
    public void Credentials_never_print()
    {
        Assert.DoesNotContain("s3cret", new EInvoiceCredentials("client-1", "s3cret", "Test").ToString(), StringComparison.Ordinal);
    }
}
