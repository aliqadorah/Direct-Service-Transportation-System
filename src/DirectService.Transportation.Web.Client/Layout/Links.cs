namespace DirectService.Transportation.Web.Client.Layout;

/// <summary>Screen addresses of records referenced by the API (search hits, notification links), relative to the company.</summary>
public static class Links
{
    public static string? ForSearchHit(string company, string type, string id) => type switch
    {
        "trip" => NavModel.Href(company, $"trips/{id}"),
        "invoice" => NavModel.Href(company, $"invoices/{id}"),
        "receipt" => NavModel.Href(company, $"receipts/{id}"),
        "advance" => NavModel.Href(company, $"driver-accounts/advances/{id}"),
        "settlement" => NavModel.Href(company, $"driver-accounts/settlements/{id}"),
        "driver" => NavModel.Href(company, $"masters/drivers/{id}"),
        "customer" => NavModel.Href(company, $"masters/customers/{id}"),
        "partner" => NavModel.Href(company, $"masters/partners/{id}"),
        _ => null,
    };

    /// <summary>The screen of the document that posted an entry (FR-097, FR-121), by its source type; null when it has no screen.</summary>
    public static string? ForSource(string company, string? type, string? id) => id is null ? null : type switch
    {
        "Trip" => NavModel.Href(company, $"trips/{id}"),
        "Invoice" => NavModel.Href(company, $"invoices/{id}"),
        "CreditNote" => NavModel.Href(company, $"credit-notes/{id}"),
        "Receipt" => NavModel.Href(company, $"receipts/{id}"),
        "Advance" => NavModel.Href(company, $"driver-accounts/advances/{id}"),
        "Settlement" => NavModel.Href(company, $"driver-accounts/settlements/{id}"),
        "PartnerPayment" => NavModel.Href(company, $"partner-accounts/payments/{id}"),
        "SupplierBill" => NavModel.Href(company, $"accounting/payables/{id}"),
        "Receivable" => NavModel.Href(company, $"accounting/receivables/{id}"),
        "ApArPayment" => NavModel.Href(company, $"accounting/apar-payments/{id}"),
        "ApArReturn" => NavModel.Href(company, $"accounting/apar-returns/{id}"),
        _ => null,
    };

    /// <summary>
    /// The screen of a record named by a history event (DT-188): a document as <see cref="ForSource"/>, a journal entry or a driver's profile;
    /// customers and partners open in their list's panel, which has no address of its own, so they are not linked.
    /// </summary>
    public static string? ForRecord(string company, string kind, string id) => kind switch
    {
        "JournalEntry" => NavModel.Href(company, $"accounting/journal/{id}"),
        "Driver" => NavModel.Href(company, $"masters/drivers/{id}"),
        _ => ForSource(company, kind, id),
    };

    /// <summary>The screen of a setup-checklist step (the API names the area by its API path).</summary>
    public static string ForSetupStep(string company, string link) => NavModel.Href(company, link.TrimStart('/') switch
    {
        "me" => "profile",
        "settings/profile" => "settings/company",
        "settings/e-invoicing" => "settings/e-invoicing",
        "accounting/accounts" => "accounting/chart",
        "accounting/posting-keys" => "settings/mapping",
        "drivers" => "masters/drivers",
        "monthly-rates" => "masters/monthly-rates",
        "users" => "settings/users",
        "trips/import" => "trips/import",
        var other => other,
    });

    /// <summary>A drill-down link of the dashboard and reports (type, record, list filters) as a screen address with the filters in the query.</summary>
    public static string? ForDrill(string company, string type, string? id, IReadOnlyDictionary<string, string>? filters)
    {
        var route = type switch
        {
            "trips" => "trips",
            "trip" => $"trips/{id}",
            "invoices" => "invoices",
            "invoice" => $"invoices/{id}",
            "credit-note" => $"credit-notes/{id}",
            "receipt" => $"receipts/{id}",
            "driver-account" => $"driver-accounts/{id}",
            "partner-account" => $"partner-accounts/{id}",
            "drivers" => "masters/drivers",
            "monthly-rates" => "masters/monthly-rates",
            "pending-postings" => "accounting/automatic?tab=pending",
            "einvoice-submissions" => "e-invoicing",
            "journal-entry" => $"accounting/journal/{id}",
            _ => null,
        };
        if (route is null || route.EndsWith('/')) return null;   // a record link without its id
        var href = NavModel.Href(company, route);
        if (filters is { Count: > 0 })
            href += (href.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join('&', filters.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)));
        return href;
    }

    /// <summary>A notification link (API-style path) as a screen address; tenant links are absolute.</summary>
    public static string? ForNotification(string? company, string? link)
    {
        if (string.IsNullOrEmpty(link) || link[0] != '/' || link.StartsWith("//", StringComparison.Ordinal)) return null;
        if (link.StartsWith("/tenant/", StringComparison.Ordinal) || link.StartsWith("/console/", StringComparison.Ordinal)) return link;
        if (company is null) return null;
        if (link.StartsWith("/ledger/pending-postings", StringComparison.Ordinal)) return NavModel.Href(company, "accounting/automatic?tab=pending");
        if (link.StartsWith("/exports/", StringComparison.Ordinal)) return NavModel.Href(company, "exports/" + link["/exports/".Length..]);
        if (link.StartsWith("/apar/receivables/", StringComparison.Ordinal)) return NavModel.Href(company, "accounting/receivables/" + link["/apar/receivables/".Length..]);
        if (link.StartsWith("/apar/returns/", StringComparison.Ordinal)) return NavModel.Href(company, "accounting/apar-returns/" + link["/apar/returns/".Length..]);
        return NavModel.Href(company, link.TrimStart('/'));
    }
}
