using System.Globalization;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace DirectService.Accounting.Tests;

/// <summary>The posting snapshot on its own database (no other test's data in the company or its numbers).</summary>
public sealed class PostingSnapshotFactory : ApiFactory
{
    protected override string? DatabaseName => "DirectServiceMaster_PostingSnapshotTests";
}

[CollectionDefinition(Name)]
public sealed class PostingSnapshotCollection : ICollectionFixture<PostingSnapshotFactory>
{
    public const string Name = "PostingSnapshot";
}

/// <summary>
/// The golden posting snapshot of Stage 3 (design j, "Proven by"; DT-379). One fixed company posts every document type through the REST API —
/// own-fleet and partner trips (edit, void, a paid partner trip voided with "Return" and with "Deduct", a pending posting retried), trip and
/// manual invoices, credit notes, receipts with allocations, advances, settlements with deductions, a previous balance, a repayment and a
/// carry-forward, partner payments with a previous balance, a deduction and a repayment, supplier bills with a deduction, receivables, AP/AR
/// payments and returns, manual entries with tax, reference and cost centre (edit and void), opening balances posted twice — and the void
/// of each. The entries are then read with SQL (tables never change in Stage 3) and compared, value by value, with
/// <c>Golden/posting-snapshot.json</c>: every entry (number, date, period, description, reference, source, status, reversal links, totals,
/// void reason), every line (account code, debit, credit, cost centre, party, note, reference, tax code, rate, amount, tax account, tax-for
/// line), the pending postings and every document's link to its entry. Database ids, tokens and times are replaced by stable labels.
/// <para>
/// The golden file was taken once on the Stage 2 code (tag stage2-done-2026-10-30, a temporary worktree) and is never regenerated: a
/// difference is a change of posting behaviour. When the file is missing or different, the test writes what it found to
/// <c>posting-snapshot.actual.json</c> next to the test assembly and fails.
/// </para>
/// </summary>
[Collection(PostingSnapshotCollection.Name)]
public sealed class PostingSnapshotTests(PostingSnapshotFactory f)
{
    /// <summary>Every document is dated here (a fixed past date, so the numbers, periods and texts never depend on the day of the run).</summary>
    private const string Day = "2026-10-01";
    private const string Later = "2026-10-20";
    private const string Month = "2026-10";

    [Fact]
    public async Task Every_document_type_posts_exactly_the_entries_of_the_Stage_2_snapshot()
    {
        var actual = await SnapshotAsync(await PostEverythingAsync());
        var golden = Path.Combine(AppContext.BaseDirectory, "Golden", "posting-snapshot.json");
        var written = Path.Combine(AppContext.BaseDirectory, "posting-snapshot.actual.json");
        if (!File.Exists(golden))
        {
            await File.WriteAllTextAsync(written, actual);
            Assert.Fail($"No golden posting snapshot. What the code posts was written to {written}.");
        }
        var expected = (await File.ReadAllTextAsync(golden)).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (expected == actual) return;
        await File.WriteAllTextAsync(written, actual);
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var at = Enumerable.Range(0, Math.Min(e.Length, a.Length)).FirstOrDefault(i => e[i] != a[i], Math.Min(e.Length, a.Length));
        Assert.Fail($"The postings differ from the Stage 2 snapshot at line {at + 1} (all of it: {written}).\n" +
            $"  golden: {(at < e.Length ? e[at].Trim() : "(end)")}\n  actual: {(at < a.Length ? a[at].Trim() : "(end)")}\n" +
            $"  context: {string.Join(" | ", e.Skip(Math.Max(0, at - 6)).Take(7).Select(l => l.Trim()))}");
    }

    // ------------------------------------------------------------------ the documents

    private sealed class Api(SignedInUser o)
    {
        public SignedInUser Owner => o;

        private string U(string path) => $"/api/v1/companies/{o.CompanyId}{path}";

        public async Task<JsonElement> PostAsync(string path, object body)
        {
            var r = await o.Client.PostAsJsonAsync(U(path), body);
            await r.EnsureOkAsync();
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<JsonElement> PutAsync(string path, object body)
        {
            var r = await o.Client.PutAsJsonAsync(U(path), body);
            await r.EnsureOkAsync();
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<JsonElement> GetAsync(string path)
        {
            var r = await o.Client.GetAsync(U(path));
            await r.EnsureOkAsync();
            return await r.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    private static string S(JsonElement e, params string[] path)
    {
        foreach (var p in path) e = e.GetProperty(p);
        return e.GetString()!;
    }

    private static decimal N(JsonElement e, params string[] path)
    {
        foreach (var p in path) e = e.GetProperty(p);
        return e.GetDecimal();
    }

    /// <summary>Posts every document type in a new company and returns the owner's e-mail (to find the company with SQL).</summary>
    private async Task<string> PostEverythingAsync()
    {
        var owner = await f.SignUpOwnerAsync("Posting Snapshot Co");
        var api = new Api(owner);

        // Master data: the transport chart template (49 accounts, cash box 110101, every posting key mapped), a bank, a cost centre.
        await api.PostAsync("/accounting/accounts/template", new { });
        var accounts = (await api.GetAsync("/accounting/accounts")).EnumerateArray().ToDictionary(a => S(a, "code"), a => S(a, "id"));
        var box = S((await api.GetAsync("/accounting/cash-boxes"))[0], "id");
        var bank = S(await api.PostAsync("/accounting/bank-accounts", new { name = "Current account", bankName = "Cairo Amman Bank", accountNumber = "0131000302", accountId = accounts["110201"] }), "id");
        var cc = S(await api.PostAsync("/accounting/cost-centres", new { code = "CC-01", name = "Own fleet" }), "id");
        var taxCodes = (await api.GetAsync("/accounting/tax-codes/options")).EnumerateArray().ToDictionary(t => S(t, "code"), t => S(t, "id"));

        var customer = S(await api.PostAsync("/customers", new { name = "Arab Potash", phone = "0781234567", paymentTermsDays = 30 }), "id");
        var partner = S(await api.PostAsync("/partners", new { name = "Al-Noor Transport" }), "id");
        var partner2 = S(await api.PostAsync("/partners", new { name = "Al-Fajr Trucking" }), "id");
        var material = S(await api.PostAsync("/materials", new { name = "Potash" }), "id");
        var loading = S(await api.PostAsync("/loading-sites", new { name = "Ghor Al-Safi" }), "id");
        var unloading = S(await api.PostAsync("/unloading-sites", new { name = "Aqaba Port" }), "id");
        var ownType = S((await api.GetAsync("/ownership-types")).EnumerateArray().Single(t => t.GetProperty("isBuiltIn").GetBoolean()), "id");
        var rented = S(await api.PostAsync("/ownership-types", new { name = "Rented", fleetClass = "External" }), "id");
        async Task<string> DriverAsync(string name, string nid, string plate, string type, string? partnerId) =>
            S(await api.PostAsync("/drivers", new
            {
                fullName = name, nationalId = nid, phone1 = "0791234567", licenceNo = "L-1", licenceType = "C", licenceExpiry = "2030-01-01", paymentMethod = "Cash",
                plateNumber = plate, truckType = "Volvo", capacityTons = 32m, ownershipTypeId = type, partnerId,
                registrationExpiry = "2030-01-01", insuranceExpiry = "2030-01-01", inspectionExpiry = "2030-01-01",
            }), "id");
        var driver = await DriverAsync("Ahmad Saleh", "9871234560", "10-11111", ownType, null);
        var driver2 = await DriverAsync("Khaled Omari", "9871234562", "10-33333", ownType, null);
        var driver3 = await DriverAsync("Yousef Haddad", "9871234563", "10-44444", ownType, null);
        var partnerDriver = await DriverAsync("Sami Abbadi", "9871234561", "10-22222", rented, partner);
        var partner2Driver = await DriverAsync("Nabil Zoubi", "9871234564", "10-55555", rented, partner2);
        await api.PostAsync("/tariffs", new { partyType = "Customer", partyId = customer, materialId = material, price = 6.000m });
        await api.PostAsync("/tariffs", new { partyType = "Partner", partyId = partner, price = 2.750m });
        await api.PostAsync("/tariffs", new { partyType = "Partner", partyId = partner2, price = 2.500m });
        await api.PutAsync($"/monthly-rates/{Month}", new { sites = new[] { new { loadingSiteId = loading, ownFleetRate = 3.500m, externalFleetRate = 4.250m } } });

        object TripBody(string receipt, string driverId, string? partnerId, decimal loaded = 32.000m, decimal unloaded = 31.850m, string? rowVersion = null) => new
        {
            receiptNo = receipt, tripDate = Day, driverId, customerId = customer, loadingSiteId = loading, unloadingSiteId = unloading, materialId = material,
            partnerId, loadedWeight = loaded, unloadedWeight = unloaded, rowVersion,
        };
        async Task<JsonElement> TripAsync(string receipt, string driverId, string? partnerId = null) =>
            (await api.PostAsync("/trips", TripBody(receipt, driverId, partnerId))).GetProperty("trip");
        async Task<string> TripVersionAsync(string id) => S(await api.GetAsync($"/trips/{id}"), "trip", "rowVersion");

        // Opening balances, posted twice (the second posting reverses the first).
        await api.PutAsync("/accounting/opening-balances", new { lines = new[] { new { accountId = accounts["110201"], debit = 5000m, credit = 0m }, new { accountId = accounts["3101"], debit = 0m, credit = 4000m } } });
        await api.PostAsync("/accounting/opening-balances/post", new { entryDate = Day });
        await api.PutAsync("/accounting/opening-balances", new { lines = new[] { new { accountId = accounts["110201"], debit = 6000m, credit = 0m }, new { accountId = accounts["3101"], debit = 0m, credit = 6000m } } });
        await api.PostAsync("/accounting/opening-balances/post", new { entryDate = Day });

        // Trips: own fleet (wage accrual), partner (partner cost), an edit (reverse + post), a void.
        var t1 = await TripAsync("R-1001", driver);
        var t2 = await TripAsync("R-1002", driver);
        var t3 = await TripAsync("R-2001", partnerDriver, partner);
        var t5 = await TripAsync("R-1003", driver);
        await api.PostAsync($"/trips/{S(t5, "id")}/void", new { reason = "Entered twice", rowVersion = await TripVersionAsync(S(t5, "id")) });
        var t6 = await TripAsync("R-1004", driver);
        await api.PutAsync($"/trips/{S(t6, "id")}", TripBody("R-1004", driver, null, 30.000m, 29.900m, await TripVersionAsync(S(t6, "id"))));
        _ = t1;
        _ = t2;

        // A pending posting retried: a trip saved while wage accrual was off, queued, posted by the retry once accrual is on again.
        var ops = await api.GetAsync("/settings/operations");
        object Ops(bool accrue) => new
        {
            billOnUnloadedWeight = ops.GetProperty("billOnUnloadedWeight").GetBoolean(), payDriverOnLoadedWeight = ops.GetProperty("payDriverOnLoadedWeight").GetBoolean(),
            shortageTolerancePct = N(ops, "shortageTolerancePct"), lockTariffs = ops.GetProperty("lockTariffs").GetBoolean(),
            invoiceApproval = ops.GetProperty("invoiceApproval").GetBoolean(), accrueDriverWage = accrue, confirmWeightBasisChange = false,
        };
        await api.PutAsync("/settings/operations", Ops(false));
        await TripAsync("R-1005", driver);
        await api.PutAsync("/settings/operations", Ops(true));
        await QueuePendingTripAsync(owner.Email, "R-1005");
        var pending = (await api.GetAsync("/ledger/pending-postings")).EnumerateArray().Single();
        await api.PostAsync($"/ledger/pending-postings/{S(pending, "id")}/retry", new { });

        // Invoices: a trip invoice with discount and fees, a manual invoice, a manual invoice voided.
        var i1 = (await api.PostAsync("/invoices", new
        {
            customerId = customer, invoiceType = "Trip", invoiceDate = Day, tripIds = new[] { S(t1, "id"), S(t3, "id") }, discount = 20m, extraFees = 5m,
            extraFeesDescription = "Night loading", approve = true,
        }));
        var i2 = await api.PostAsync("/invoices", new
        {
            customerId = customer, invoiceType = "Manual", invoiceDate = Day, lines = new[] { new { description = "Equipment move", quantity = 1m, unitPrice = 1000m } },
            discount = 0m, extraFees = 0m, approve = true,
        });
        var i3 = await api.PostAsync("/invoices", new
        {
            customerId = customer, invoiceType = "Manual", invoiceDate = Day, lines = new[] { new { description = "Crane hire", quantity = 2m, unitPrice = 150m } },
            discount = 0m, extraFees = 0m, approve = true,
        });
        await api.PostAsync($"/invoices/{S(i3, "invoice", "id")}/void", new { reason = "Wrong customer", rowVersion = S(i3, "invoice", "rowVersion") });

        // Credit notes: one on the partner trip's line of the trip invoice (kept), one on the manual invoice (voided).
        var i1Line = i1.GetProperty("lines").EnumerateArray().Last();
        await api.PostAsync("/credit-notes", new { invoiceId = S(i1, "invoice", "id"), creditDate = Day, reason = "Short delivery", lines = new[] { new { invoiceLineId = S(i1Line, "id"), quantity = (decimal?)null } } });
        var i2Line = i2.GetProperty("lines")[0];
        var cn2 = await api.PostAsync("/credit-notes", new { invoiceId = S(i2, "invoice", "id"), creditDate = Day, reason = "Price agreed lower", lines = new[] { new { invoiceLineId = S(i2Line, "id"), quantity = (decimal?)0.25m } } });
        await api.PostAsync($"/credit-notes/{S(cn2, "creditNote", "id")}/void", new { reason = "Issued by mistake", rowVersion = S(cn2, "creditNote", "rowVersion") });

        // Receipts: one allocated to both invoices with the rest on account (cash), one by bank transfer voided.
        await api.PostAsync("/receipts", new
        {
            customerId = customer, receiptDate = Day, amount = 700m, paymentMethod = "Cash", cashBoxId = box, attachmentsCount = 0,
            allocations = new[] { new { invoiceId = S(i1, "invoice", "id"), amount = 150m }, new { invoiceId = S(i2, "invoice", "id"), amount = 400m } },
        });
        var rc2 = await api.PostAsync("/receipts", new { customerId = customer, receiptDate = Day, invoiceId = S(i2, "invoice", "id"), amount = 200m, paymentMethod = "Bank", bankAccountId = bank, reference = "TRF-5521", attachmentsCount = 0 });
        await api.PostAsync($"/receipts/{S(rc2, "receipt", "id")}/void", new { reason = "Bounced", rowVersion = S(rc2, "receipt", "rowVersion") });

        // Advances: one settled later, one voided.
        await api.PostAsync("/advances", new { driverId = driver, advanceDate = Day, amount = 100m, paymentMethod = "Cash", cashBoxId = box, notes = "Fuel" });
        var a2 = await api.PostAsync("/advances", new { driverId = driver, advanceDate = Day, amount = 50m, paymentMethod = "Cash", cashBoxId = box, notes = "Tyres" });
        await api.PostAsync($"/advances/{S(a2, "advance", "id")}/void", new { reason = "Not paid out", rowVersion = S(a2, "advance", "rowVersion") });

        // Manual entries: a previous balance of the driver (he owes 30) and of the partner (it owes 10), one with tax / reference / cost
        // centre, one edited (reverse + post), one voided.
        object Line(string code, decimal debit, decimal credit, string? partyType = null, string? partyId = null, string? note = null, string? reference = null,
            string? costCentre = null, string? taxCode = null, string? taxAccount = null) => new
        {
            accountId = accounts[code], debit, credit, costCentreId = costCentre, party = partyType is null ? null : new { type = partyType, id = partyId }, note, reference,
            taxCodeId = taxCode is null ? null : taxCodes[taxCode], taxAccountId = taxAccount is null ? null : accounts[taxAccount],
        };
        await api.PostAsync("/accounting/journal-entries", new { entryDate = Day, description = "Driver balance before the system", lines = new[] { Line("1104", 30m, 0m, "Driver", driver), Line("110101", 0m, 30m) } });
        await api.PostAsync("/accounting/journal-entries", new { entryDate = Day, description = "Partner balance before the system", lines = new[] { Line("2103", 10m, 0m, "Partner", partner), Line("110101", 0m, 10m) } });
        await api.PostAsync("/accounting/journal-entries", new
        {
            entryDate = Day, description = "Workshop invoice", reference = "INV-2210",
            lines = new[] { Line("5299", 100m, 0m, note: "Brake repair", costCentre: cc, taxCode: "VAT_JO_16", taxAccount: "1105"), Line("110101", 0m, 116m, reference: "PAY-77") },
        });
        var jv4 = await api.PostAsync("/accounting/journal-entries", new { entryDate = Day, description = "Office rent", lines = new[] { Line("5202", 250m, 0m), Line("110101", 0m, 250m) } });
        await api.PutAsync($"/accounting/journal-entries/{S(jv4, "id")}", new { entryDate = Day, description = "Office rent (October)", rowVersion = S(jv4, "rowVersion"), lines = new[] { Line("5202", 275m, 0m), Line("110101", 0m, 275m) } });
        var jv5 = await api.PostAsync("/accounting/journal-entries", new { entryDate = Day, description = "Bank charges", lines = new[] { Line("5203", 12m, 0m), Line("110201", 0m, 12m) } });
        await api.PostAsync($"/accounting/journal-entries/{S(jv5, "id")}/void", new { reason = "Charged twice", rowVersion = S(jv5, "rowVersion") });

        // Settlements: all open items of the first driver with a fine and his previous balance; a repayment with carry-forward; a void.
        await api.PostAsync("/settlements", new
        {
            driverId = driver, settlementDate = Later, deductions = new[] { new { type = "Fine", amount = 10m, note = "Late delivery" } }, paymentMethod = "Cash", cashBoxId = box,
        });
        await TripAsync("R-3001", driver2);
        await api.PostAsync("/advances", new { driverId = driver2, advanceDate = Day, amount = 200m, paymentMethod = "Cash", cashBoxId = box, notes = "Advance" });
        await api.PostAsync("/settlements", new { driverId = driver2, settlementDate = Later, deductions = Array.Empty<object>(), paymentMethod = "Cash", cashBoxId = box, repaidAmount = 50m });
        await TripAsync("R-4001", driver3);
        var s3 = await api.PostAsync("/settlements", new { driverId = driver3, settlementDate = Later, deductions = Array.Empty<object>(), paymentMethod = "Bank", bankAccountId = bank, reference = "TRF-7001" });
        await api.PostAsync($"/settlements/{S(s3, "settlement", "id")}/void", new { reason = "Wrong driver", rowVersion = S(s3, "settlement", "rowVersion") });

        // Partner payments: the first partner's trip paid by CliQ (previous balance set off); a paid trip voided with "Deduct", whose
        // amount the next payment sets off; the second partner: a paid trip voided with "Return" and the repayment; a payment voided.
        await api.PostAsync("/partner-payments", new { partnerId = partner, paymentDate = Day, amount = N(t3, "partnerCost") - 10m, paymentMethod = "CliQ", bankAccountId = bank, reference = "CLIQ-1" });
        var t4 = await TripAsync("R-2002", partnerDriver, partner);
        await api.PostAsync("/partner-payments", new { partnerId = partner, paymentDate = Day, amount = N(t4, "partnerCost"), paymentMethod = "Cash", cashBoxId = box });
        await api.PostAsync($"/trips/{S(t4, "id")}/void", new { reason = "Load cancelled", rowVersion = await TripVersionAsync(S(t4, "id")), partnerPaidHandling = "Deduct" });
        var t9 = await TripAsync("R-2003", partnerDriver, partner);
        var t9b = await TripAsync("R-2004", partnerDriver, partner);
        await api.PostAsync("/partner-payments", new { partnerId = partner, paymentDate = Later, amount = N(t9, "partnerCost") + N(t9b, "partnerCost") - N(t4, "partnerCost"), paymentMethod = "Cash", cashBoxId = box });
        var t10 = await TripAsync("R-5001", partner2Driver, partner2);
        await api.PostAsync("/partner-payments", new { partnerId = partner2, paymentDate = Day, amount = N(t10, "partnerCost"), paymentMethod = "Cash", cashBoxId = box });
        await api.PostAsync($"/trips/{S(t10, "id")}/void", new { reason = "Customer refused the load", rowVersion = await TripVersionAsync(S(t10, "id")), partnerPaidHandling = "Return" });
        await api.PostAsync("/partner-payments", new { partnerId = partner2, paymentDate = Later, amount = N(t10, "partnerCost"), paymentMethod = "Cash", cashBoxId = box, kind = "Repayment" });
        var t11 = await TripAsync("R-5002", partner2Driver, partner2);
        var p6 = await api.PostAsync("/partner-payments", new { partnerId = partner2, paymentDate = Later, amount = N(t11, "partnerCost"), paymentMethod = "Cash", cashBoxId = box });
        await api.PostAsync($"/partner-payments/{S(p6, "payment", "id")}/void", new { reason = "Paid to the wrong partner", rowVersion = S(p6, "payment", "rowVersion") });

        // AP/AR: a supplier bill (tax rate per line, cost centre, a deduction) paid in part and returned in part; a second bill whose
        // payment, return and the bill itself are voided; receivables of an accounting customer and of the transport customer, collected.
        var supplier = S(await api.PostAsync("/apar/parties", new { role = "Supplier", name = "Al-Manaseer Fuel", taxNumber = "100200300", phone = "0791112223", address = "Amman", controlAccountId = accounts["2101"], paymentTermsDays = 30 }), "id");
        var arParty = S(await api.PostAsync("/apar/parties", new { role = "Customer", name = "Ports Company", taxNumber = "500600700", controlAccountId = accounts["110302"], paymentTermsDays = 0 }), "id");
        var b1 = await api.PostAsync("/apar/bills", new
        {
            partyId = supplier, documentDate = Day, reference = "88412",
            lines = new object[] { new { description = "Diesel — October fleet", accountId = accounts["5103"], amount = 3120m, taxRate = 16m, costCentreId = cc, deduction = 120m }, new { description = "Oil and filters", accountId = accounts["5104"], amount = 410m } },
        });
        await api.PostAsync("/apar/payments", new { documentId = S(b1, "document", "id"), paymentDate = Later, amount = 2000m, paymentMethod = "Cash", cashBoxId = box, attachmentsCount = 0 });
        await api.PostAsync("/apar/returns", new { documentId = S(b1, "document", "id"), returnDate = Later, amount = 100m, reason = "Filters returned" });
        var b2 = await api.PostAsync("/apar/bills", new { partyId = supplier, documentDate = Day, reference = "88500", lines = new[] { new { description = "Tyres", accountId = accounts["5104"], amount = 800m, taxRate = 16m } } });
        var py2 = await api.PostAsync("/apar/payments", new { documentId = S(b2, "document", "id"), paymentDate = Later, amount = 300m, paymentMethod = "Bank", bankAccountId = bank, reference = "TRF-9001", attachmentsCount = 0 });
        var rt2 = await api.PostAsync("/apar/returns", new { documentId = S(b2, "document", "id"), returnDate = Later, amount = 200m, reason = "One tyre returned" });
        await api.PostAsync($"/apar/payments/{S(py2, "payment", "id")}/void", new { reason = "Paid twice", rowVersion = S(py2, "payment", "rowVersion") });
        await api.PostAsync($"/apar/returns/{S(rt2, "return", "id")}/void", new { reason = "Return refused", rowVersion = S(rt2, "return", "rowVersion") });
        await api.PostAsync($"/apar/bills/{S(b2, "document", "id")}/void", new { reason = "Duplicate bill", rowVersion = S(await api.GetAsync($"/apar/bills/{S(b2, "document", "id")}"), "document", "rowVersion") });
        var r1 = await api.PostAsync("/apar/receivables", new { partyId = arParty, documentDate = Day, reference = "PC-77", lines = new[] { new { description = "Truck rental", accountId = accounts["4102"], amount = 1400m, taxRate = 16m } } });
        await api.PostAsync("/apar/payments", new { documentId = S(r1, "document", "id"), paymentDate = Later, amount = 1000m, paymentMethod = "Cash", cashBoxId = box, attachmentsCount = 0 });
        await api.PostAsync("/apar/receivables", new { customerId = customer, documentDate = Day, lines = new[] { new { description = "Storage", accountId = accounts["4102"], amount = 300m, taxRate = 16m } } });

        return owner.Email;
    }

    /// <summary>Queues a pending posting for a trip that has no entry (what a technical failure of the posting leaves behind).</summary>
    private async Task QueuePendingTripAsync(string email, string receiptNo)
    {
        await using var c = await OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT INTO acc.PendingPostings (TenantId, CompanyId, SourceModule, SourceType, SourceId, Reason, Attempts, CreatedAt, IsDeleted)
            SELECT t.TenantId, t.CompanyId, 'TRN', 'Trip', t.Id, 'Simulated technical failure', 0, SYSUTCDATETIME(), 0
            FROM trn.TRN_Trips t WHERE t.ReceiptNo = @r AND t.JournalEntryId IS NULL AND t.CompanyId = (
                SELECT TOP 1 r.CompanyId FROM core.UserCompanyRoles r JOIN core.Users u ON u.Id = r.UserId WHERE u.Email = @e)
            """, c);
        cmd.Parameters.AddWithValue("@r", receiptNo);
        cmd.Parameters.AddWithValue("@e", email);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ------------------------------------------------------------------ the snapshot

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(f.Database);
        await c.OpenAsync();
        return c;
    }

    private static async Task<List<Dictionary<string, object?>>> RowsAsync(SqlConnection c, string sql, long company)
    {
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@c", company);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync())
            rows.Add(Enumerable.Range(0, r.FieldCount).ToDictionary(r.GetName, i => r.IsDBNull(i) ? null : r.GetValue(i), StringComparer.Ordinal));
        return rows;
    }

    private static string? Text(object? v) => v switch
    {
        null => null,
        DateTime d => d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    /// <summary>The entries, lines, pending postings and document links of the company, with ids replaced by stable labels.</summary>
    private async Task<string> SnapshotAsync(string email)
    {
        await using var c = await OpenAsync();
        await using var find = new SqlCommand("SELECT TOP 1 r.CompanyId FROM core.UserCompanyRoles r JOIN core.Users u ON u.Id = r.UserId WHERE u.Email = @e", c);
        find.Parameters.AddWithValue("@e", email);
        var company = (long)(await find.ExecuteScalarAsync())!;

        var accounts = (await RowsAsync(c, "SELECT Id, Code FROM acc.Accounts WHERE CompanyId = @c", company)).ToDictionary(r => (long)r["Id"]!, r => (string)r["Code"]!);
        var centres = (await RowsAsync(c, "SELECT Id, Code FROM acc.CostCentres WHERE CompanyId = @c", company)).ToDictionary(r => (long)r["Id"]!, r => (string)r["Code"]!);
        var taxCodes = (await RowsAsync(c, "SELECT Id, Code FROM acc.TaxCodes", company)).ToDictionary(r => (long)r["Id"]!, r => (string)r["Code"]!);
        var parties = new Dictionary<(string, string, long), string>();
        foreach (var (module, type, sql) in new[]
        {
            ("TRN", "Customer", "SELECT Id, Name FROM trn.TRN_Customers WHERE CompanyId = @c"),
            ("TRN", "Driver", "SELECT Id, FullName AS Name FROM trn.TRN_Drivers WHERE CompanyId = @c"),
            ("TRN", "Partner", "SELECT Id, Name FROM trn.TRN_Partners WHERE CompanyId = @c"),
            ("ACC", "ApArParty", "SELECT Id, Name FROM acc.ApArParties WHERE CompanyId = @c"),
        })
            foreach (var r in await RowsAsync(c, sql, company)) parties[(module, type, (long)r["Id"]!)] = (string)r["Name"]!;

        var entries = await RowsAsync(c, "SELECT * FROM acc.JournalEntries WHERE CompanyId = @c ORDER BY Id", company);
        var lines = await RowsAsync(c, "SELECT * FROM acc.EntryLines WHERE CompanyId = @c ORDER BY JournalEntryId, [LineNo]", company);
        var pendings = await RowsAsync(c, "SELECT * FROM acc.PendingPostings WHERE CompanyId = @c ORDER BY Id", company);
        var number = entries.ToDictionary(e => (long)e["Id"]!, e => (string)e["Number"]!);

        // A document is named by its type and its order among the company's documents of that type (ids differ from run to run).
        var sources = entries.Where(e => e["SourceId"] is not null).Select(e => ((string)e["SourceModule"]!, (string)e["SourceType"]!, (long)e["SourceId"]!))
            .Concat(pendings.Select(p => ((string)p["SourceModule"]!, (string)p["SourceType"]!, (long)p["SourceId"]!))).Distinct()
            .GroupBy(s => (s.Item1, s.Item2)).SelectMany(g => g.OrderBy(s => s.Item3).Select((s, i) => (s, Label: $"{s.Item1}.{s.Item2}#{i + 1}")))
            .ToDictionary(x => x.s, x => x.Label);
        string? Party(object? module, object? type, object? id) => id is null ? null
            : parties.TryGetValue(((string)module!, (string)type!, (long)id), out var name) ? $"{module}/{type}/{name}" : $"{module}/{type}/(unknown)";
        string? Code(Dictionary<long, string> map, object? id) => id is null ? null : map.TryGetValue((long)id, out var code) ? code : "(unknown)";

        var linesByEntry = lines.GroupBy(l => (long)l["JournalEntryId"]!).ToDictionary(g => g.Key, g => g.ToList());
        var root = new JsonObject
        {
            ["entries"] = new JsonArray(entries.Select(e =>
            {
                var own = linesByEntry.GetValueOrDefault((long)e["Id"]!) ?? [];
                var lineNo = own.ToDictionary(l => (long)l["Id"]!, l => (int)l["LineNo"]!);
                return (JsonNode)new JsonObject
                {
                    ["number"] = Text(e["Number"]),
                    ["entryDate"] = Text(e["EntryDate"]),
                    ["periodMonth"] = Text(e["PeriodMonth"]),
                    ["description"] = Text(e["Description"]),
                    ["reference"] = Text(e["Reference"]),
                    ["source"] = e["SourceId"] is null ? Text(e["SourceModule"]) + (e["SourceType"] is null ? "" : "." + Text(e["SourceType"]))
                        : sources[((string)e["SourceModule"]!, (string)e["SourceType"]!, (long)e["SourceId"]!)],
                    ["isManual"] = (bool)e["IsManual"]!,
                    ["status"] = Text(e["Status"]),
                    ["reverses"] = e["ReversesEntryId"] is long rv ? number[rv] : null,
                    ["reversedBy"] = e["ReversedByEntryId"] is long rb ? number[rb] : null,
                    ["totalDebit"] = Text(e["TotalDebit"]),
                    ["totalCredit"] = Text(e["TotalCredit"]),
                    ["voidReason"] = Text(e["VoidReason"]),
                    ["voided"] = e["VoidedAt"] is not null,
                    ["exported"] = e["ExportBatchId"] is not null,
                    ["lines"] = new JsonArray(own.Select(l => (JsonNode)new JsonObject
                    {
                        ["lineNo"] = (int)l["LineNo"]!,
                        ["account"] = Code(accounts, l["AccountId"]),
                        ["debit"] = Text(l["Debit"]),
                        ["credit"] = Text(l["Credit"]),
                        ["costCentre"] = Code(centres, l["CostCentreId"]),
                        ["party"] = Party(l["PartyModule"], l["PartyType"], l["PartyId"]),
                        ["note"] = Text(l["Note"]),
                        ["reference"] = Text(l["Reference"]),
                        ["taxCode"] = Code(taxCodes, l["TaxCodeId"]),
                        ["taxRate"] = Text(l["TaxRate"]),
                        ["taxAmount"] = Text(l["TaxAmount"]),
                        ["taxAccount"] = Code(accounts, l["TaxAccountId"]),
                        ["taxForLine"] = l["TaxForLineId"] is long tf ? lineNo.GetValueOrDefault(tf, -1) : null,
                    }).ToArray()),
                };
            }).ToArray()),
            ["pendingPostings"] = new JsonArray(pendings.Select(p => (JsonNode)new JsonObject
            {
                ["source"] = sources[((string)p["SourceModule"]!, (string)p["SourceType"]!, (long)p["SourceId"]!)],
                ["reason"] = Text(p["Reason"]),
                ["attempts"] = (int)p["Attempts"]!,
                ["resolved"] = p["ResolvedAt"] is not null,
                ["deleted"] = (bool)p["IsDeleted"]!,
            }).ToArray()),
            ["documentLinks"] = await LinksAsync(c, company, number),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>
    /// Every document row of the company that links to a journal entry (any column of the trn / acc tables whose name ends in
    /// "JournalEntryId", "EntryId" or "ReversalEntryId"), in id order per table, with its number and status where the table has them.
    /// </summary>
    private static readonly string[] DocumentColumns = ["Number", "ReceiptNo", "Status", "IsDeleted"];

    private static async Task<JsonObject> LinksAsync(SqlConnection c, long company, Dictionary<long, string> number)
    {
        var columns = await RowsAsync(c, """
            SELECT c.TABLE_SCHEMA AS S, c.TABLE_NAME AS T, c.COLUMN_NAME AS C FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
            WHERE c.TABLE_SCHEMA IN ('trn', 'acc') AND c.COLUMN_NAME LIKE '%EntryId' AND c.DATA_TYPE = 'bigint'
              AND NOT (c.TABLE_NAME IN ('JournalEntries', 'EntryLines') AND c.TABLE_SCHEMA = 'acc')
            ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME
            """, company);
        var all = await RowsAsync(c, "SELECT TABLE_SCHEMA AS S, TABLE_NAME AS T, COLUMN_NAME AS C FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA IN ('trn', 'acc')", company);
        bool Has(string s, string t, string col) => all.Any(x => (string)x["S"]! == s && (string)x["T"]! == t && (string)x["C"]! == col);
        var result = new JsonObject();
        foreach (var table in columns.GroupBy(x => ((string)x["S"]!, (string)x["T"]!)))
        {
            var (s, t) = table.Key;
            if (!Has(s, t, "CompanyId")) continue;
            var cols = table.Select(x => (string)x["C"]!).ToList();
            var extra = DocumentColumns.Where(x => Has(s, t, x)).ToList();
            var rows = await RowsAsync(c, $"SELECT {string.Join(", ", cols.Concat(extra).Select(x => "[" + x + "]"))} FROM [{s}].[{t}] WHERE CompanyId = @c ORDER BY Id", company);
            var linked = rows.Where(r => cols.Any(x => r[x] is not null)).ToList();
            if (linked.Count == 0) continue;
            result[$"{s}.{t}"] = new JsonArray(rows.Select((r, i) => (r, i)).Where(x => cols.Any(col => x.r[col] is not null)).Select(x =>
            {
                var o = new JsonObject { ["row"] = x.i + 1 };
                foreach (var e in extra) o[e] = x.r[e] is bool b ? b : Text(x.r[e]);
                foreach (var col in cols) o[col] = x.r[col] is long id ? number.GetValueOrDefault(id, "(other company)") : null;
                return (JsonNode)o;
            }).ToArray());
        }
        return result;
    }
}
