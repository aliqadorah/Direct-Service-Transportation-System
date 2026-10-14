using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Audit;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Tests.Audit;

/// <summary>
/// The central audit describer (owner feedback 14 Oct 2026, DT-188) against SQL Server: every record type, action and field has words in
/// Arabic and English; ids become numbers and names (soft-deleted and no-longer-found records included); values are formatted; technical
/// columns are left out; a journal entry and its lines are one event; documents named by an action are links.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditDescriberTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime At = new(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);

    private sealed class Tokens : IUrlTokenService
    {
        public string Encode<TEntity>(Guid publicId) => Encode(typeof(TEntity), publicId);
        public Guid? Decode<TEntity>(string? token) => null;
        public string Encode(Type entityType, Guid publicId) => $"{entityType.Name}:{publicId:N}";
        public Guid? Decode(Type entityType, string? token) => null;
    }

    private static AuditDescriber Describer(MasterDbContext ctx, TestTenant t, string language) =>
        new(ctx, new RequestContext
        {
            Language = language, TenantId = t.TenantId, CompanyId = t.CompanyId, UserId = t.UserId,
            Permissions = Contracts.Security.PermissionKeys.All.ToHashSet(),
        }, new Tokens());

    private static string Json(object values) => JsonSerializer.Serialize(values);

    private static AuditRow Row(long id, string action, string type, long? entityId, object? newValues, object? oldValues = null, long? user = null, DateTime? at = null) =>
        new(id, at ?? At, user, action, type, entityId, oldValues is null ? null : Json(oldValues), newValues is null ? null : Json(newValues));

    // ================================================================= vocabulary coverage (no database)

    /// <summary>Record types never written to the audit log (high-frequency technical rows, the log itself).</summary>
    private static readonly HashSet<string> NotAudited = ["CORE.UserSession", "CFG.RecordLock", "CORE.AuditLog", "TRN.RateHistory", "CORE.Notification", "CORE.Permission",
        "CORE.MaintenanceLog"];

    private static IEnumerable<(string Name, Type Type)> AuditedTypes() =>
        typeof(AuditLog).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.Split('.').Last() is "Acc" or "Cfg" or "Core" or "Trn" && t.GetProperty("Id") is not null)
            .Select(t => (Name: t.Namespace!.Split('.').Last().ToUpperInvariant() + "." + t.Name, Type: t))
            .Where(t => !NotAudited.Contains(t.Name));

    [Fact]
    public void Every_audited_record_type_has_a_title_and_a_noun_in_both_languages()
    {
        var missing = AuditedTypes().Where(t => !AuditVocabulary.Entities.ContainsKey(t.Name)).Select(t => t.Name).ToList();
        Assert.Empty(missing);
        Assert.All(AuditVocabulary.Entities.Values, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Title.Ar) || string.IsNullOrWhiteSpace(w.Title.En) || string.IsNullOrWhiteSpace(w.Noun.Ar) || string.IsNullOrWhiteSpace(w.Noun.En));
            Assert.DoesNotContain('.', w.Title.En);   // never "TRN.Trip"
        });
    }

    [Fact]
    public void Every_field_of_an_audited_record_has_a_label_or_is_hidden()
    {
        var unlabelled = AuditedTypes()
            .SelectMany(t => t.Type.GetProperties().Where(p => IsColumn(p.PropertyType)).Select(p => (t.Name, Column: p.Name)))
            .Where(f => !AuditVocabulary.Hidden.Contains(f.Column) && AuditVocabulary.Field(f.Name, f.Column) is null)
            .Select(f => $"{f.Name}.{f.Column}").ToList();
        Assert.Empty(unlabelled);

        static bool IsColumn(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal) || u == typeof(DateOnly) || u == typeof(DateTime) || u == typeof(Guid);
        }
    }

    /// <summary>The actions the application writes (SetAction / AuditTrail.Record); a new one must get words too.</summary>
    private static readonly string[] KnownActions =
    [
        "Create", "Update", "Delete", "Archive", "Restore", "Void", "Print", "Run", "Export", "Import", "Reorder", "Reviewed", "Clear",
        "AddedToInvoice", "RemovedFromInvoice", "Invoiced", "Settled", "SettlementVoided", "ReturnedByCreditNote", "Approve", "CreditNote",
        "CreditNoteVoided", "Receipt", "ReceiptVoid", "Payment", "PaymentVoid", "Return", "ReturnVoid", "Post", "Edit", "Reverse", "Reversal",
        "OpeningBalance", "Move", "Activate", "Deactivate", "Template", "MappingChange", "Close", "Reopen", "LinkApArParty", "SettingChange",
        "MigrationComplete", "SetupDone", "SetupReopened", "Cancel", "Resubmit", "Accepted", "Rejected", "Failed", "SignIn", "SignInFailed",
        "EmailVerified", "PasswordReset", "TwoFactorEnabled", "TwoFactorReset", "Unlock", "Disable", "Enable", "RoleChange", "PermissionChange",
        "Resend", "Accept", "Anonymise", "Paid", "PaidNotApplied", "Refunded", "PaymentFailed", "Credit", "ExtendTrial", "Suspend", "Reactivate",
        "Downgrade", "TrialEnded", "GraceEnded", "Reject", "End", "SupportRequest",
    ];

    [Fact]
    public void Every_action_has_a_label_and_a_sentence_in_both_languages()
    {
        Assert.DoesNotContain(KnownActions, a => !AuditVocabulary.Actions.ContainsKey(a));
        Assert.All(AuditVocabulary.Actions, kv =>
        {
            var w = kv.Value;
            Assert.False(string.IsNullOrWhiteSpace(w.Label.Ar) || string.IsNullOrWhiteSpace(w.Label.En), kv.Key);
            // Placeholders are the known ones only.
            foreach (var s in new[] { w.Sentence.Ar, w.Sentence.En })
                Assert.Empty(System.Text.RegularExpressions.Regex.Matches(s, @"\{[a-z]+\}").Select(m => m.Value).Except(["{r}", "{d}", "{x}"]));
        });
        // An unknown action is still words, never a code: "SomethingNew" → "Something new".
        Assert.Equal("Something new", AuditVocabulary.Action("SomethingNew").Label.En);
    }

    // ================================================================= sentences, both languages, every type × common actions

    [Fact]
    public async Task Every_record_type_and_common_action_reads_as_a_sentence_in_both_languages()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var rows = new List<AuditRow>();
            var id = 1L;
            foreach (var type in AuditVocabulary.Entities.Keys)
                foreach (var action in new[] { "Create", "Update", "Archive", "Delete", "Void", "Print" })
                    rows.Add(Row(id++, action, type, 900_000_000 + id, new { Name = "Sample", _reason = "Because" }, action == "Create" ? null : new { Name = "Old" }, t.UserId));
            foreach (var language in new[] { "ar", "en" })
            {
                var described = await Describer(ctx, t, language).DescribeAsync(rows, new DescribeOptions(CreateValues: true), Ct);
                Assert.Equal(rows.Count, described.Count);
                Assert.All(described, d =>
                {
                    Assert.DoesNotContain("{", d.Sentence, StringComparison.Ordinal);
                    Assert.DoesNotMatch(@"\b(TRN|ACC|CFG|CORE)\.[A-Z]", d.Sentence);   // never "TRN.Trip"
                    Assert.StartsWith(language == "en" ? "Owner " : "Owner: ", d.Sentence, StringComparison.Ordinal);
                    Assert.Contains("Sample", d.Sentence, StringComparison.Ordinal);   // the record by its name
                });
            }
        }
    }

    // ================================================================= ids → numbers and names, deleted targets

    [Fact]
    public async Task Ids_become_names_and_numbers_and_a_deleted_target_keeps_its_last_name()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var customer = new Customer { Name = "Arab Potash" };
            var material = new Material { Name = "Potash" };
            var site = new LoadingSite { Name = "Ghor Al-Safi" };
            var gone = new Material { Name = "Phosphate" };
            var account = new Account { Code = "1101", NameAr = "الصندوق الرئيسي", NameEn = "Main cash box", AccountType = "Asset" };
            ctx.AddRange(customer, material, site, gone, account);
            await ctx.SaveChangesAsync(Ct);
            var box = new CashBox { Name = "الصندوق الرئيسي", AccountId = account.Id };
            var invoice = new Invoice { Number = "INV-00001", InvoiceType = "Trip", CustomerId = customer.Id, InvoiceDate = new DateOnly(2026, 10, 14), DueDate = new DateOnly(2026, 11, 13) };
            ctx.AddRange(box, invoice);
            await ctx.SaveChangesAsync(Ct);
            gone.DeleteReason = "Entered by mistake";
            ctx.Remove(gone);   // soft delete: the history still names it
            await ctx.SaveChangesAsync(Ct);

            const long trip = 990_001;
            var rows = new List<AuditRow>
            {
                Row(1, "Update", "TRN.Trip", trip, new { MaterialId = material.Id, CustomerPrice = 6.5m, TripDate = "2026-10-15", IsManualRate = true, Notes = "" },
                    new { MaterialId = gone.Id, CustomerPrice = 6m, TripDate = "2026-10-14", IsManualRate = false, Notes = "x" }, t.UserId),
                Row(2, "AddedToInvoice", "TRN.Trip", trip, new { InvoiceId = invoice.Id, _reason = "مسودة فاتورة" }, new { InvoiceId = (long?)null }, t.UserId),
                Row(3, "Invoiced", "TRN.Trip", trip, new { _reason = "INV-00001" }, null, t.UserId),
                Row(4, "Update", "TRN.Receipt", 990_002, new { CashBoxId = box.Id, Amount = 1250m, PaymentMethod = "Cash" }, new { CashBoxId = (long?)null, Amount = 1000m, PaymentMethod = "Bank" }, t.UserId),
                Row(5, "Update", "TRN.CashBox", box.Id, new { AccountId = account.Id }, new { AccountId = 990_003L }, t.UserId),
                // A record no longer found: named by the last name its rows recorded.
                Row(6, "Update", "TRN.Partner", 990_004, new { Phone = "0791234567" }, new { Phone = "0781234567" }, t.UserId),
            };
            ctx.Add(new AuditLog { TenantId = t.TenantId, CompanyId = t.CompanyId, UserId = t.UserId, OccurredAt = At.AddDays(-3), Action = "Create", EntityType = "TRN.Partner", EntityId = 990_004,
                NewValues = Json(new { Name = "Al-Noor Transport" }) });
            ctx.Add(new AuditLog { TenantId = t.TenantId, CompanyId = t.CompanyId, UserId = t.UserId, OccurredAt = At.AddDays(-3), Action = "Create", EntityType = "TRN.Trip", EntityId = trip,
                NewValues = Json(new { ReceiptNo = "R-1001" }) });
            await ctx.SaveChangesAsync(Ct);

            var en = await Describer(ctx, t, "en").DescribeAsync(rows, new DescribeOptions(), Ct);
            var edit = en[0];
            Assert.Equal("Owner edited the trip R-1001", edit.Sentence);
            Assert.Contains(edit.Changes, c => c.Field == "Material" && c.OldValue == "Phosphate" && c.NewValue == "Potash");   // deleted target by its name
            Assert.Contains(edit.Changes, c => c.Field == "Customer price (per ton)" && c.OldValue == "6.000" && c.NewValue == "6.500");
            Assert.Contains(edit.Changes, c => c.Field == "Trip date" && c.OldValue == "14/10/2026" && c.NewValue == "15/10/2026");
            Assert.Contains(edit.Changes, c => c.Field == "Manual price" && c.OldValue == "No" && c.NewValue == "Yes");
            Assert.Contains(edit.Changes, c => c.Field == "Notes" && c.OldValue == "x" && c.NewValue == "—");
            Assert.Equal("Owner added the trip R-1001 to draft invoice INV-00001", en[1].Sentence);
            Assert.Equal(("Invoice", "INV-00001"), (en[1].Links.Single().Kind, en[1].Links.Single().Text));
            Assert.StartsWith("Invoice:", en[1].Links.Single().Id, StringComparison.Ordinal);
            Assert.Empty(en[1].Changes);
            Assert.Equal("Owner invoiced the trip R-1001 on invoice INV-00001", en[2].Sentence);
            Assert.Null(en[2].Reason);
            Assert.Contains(en[3].Changes, c => c.Field == "Cash box" && c.OldValue == "—" && c.NewValue == "الصندوق الرئيسي");
            Assert.Contains(en[3].Changes, c => c.Field == "Amount" && c.OldValue == "1,000.000" && c.NewValue == "1,250.000");
            Assert.Contains(en[3].Changes, c => c.Field == "Payment method" && c.OldValue == "Bank transfer" && c.NewValue == "Cash");
            Assert.Contains(en[4].Changes, c => c.Field == "Account" && c.OldValue == "(unknown)" && c.NewValue == "1101 · Main cash box");
            Assert.Equal("Owner edited partner Al-Noor Transport", en[5].Sentence);

            var ar = await Describer(ctx, t, "ar").DescribeAsync(rows, new DescribeOptions(), Ct);
            Assert.Equal("Owner: تعديل الرحلة R-1001", ar[0].Sentence);
            Assert.Contains(ar[0].Changes, c => c.Field == "المادة" && c.OldValue == "Phosphate" && c.NewValue == "Potash");
            Assert.Contains(ar[0].Changes, c => c.Field == "سعر يدوي" && c.OldValue == "لا" && c.NewValue == "نعم");
            Assert.Equal("Owner: إضافة الرحلة R-1001 إلى مسودة الفاتورة INV-00001", ar[1].Sentence);
            Assert.Equal("Owner: فوترة الرحلة R-1001 في الفاتورة INV-00001", ar[2].Sentence);
            Assert.Contains(ar[3].Changes, c => c.Field == "طريقة الدفع" && c.OldValue == "تحويل بنكي" && c.NewValue == "نقد");
            Assert.Contains(ar[4].Changes, c => c.Field == "الحساب" && c.NewValue == "1101 · الصندوق الرئيسي");
            Assert.Equal("تعديل", ar[0].ActionLabel);
            Assert.Equal("رحلة", ar[0].EntityLabel);
        }
    }

    // ================================================================= technical columns, users, settings, languages

    [Fact]
    public async Task Technical_columns_are_left_out_and_codes_read_as_words()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var rows = new List<AuditRow>
            {
                Row(1, "Update", "TRN.Trip", 1, new { IsDeleted = false, LineNo = 2, TenantId = 5, CompanyId = 7, UpdatedAt = "2026-10-14T09:00:00", RowVersion = "AAA", JournalEntryId = 27 },
                    new { IsDeleted = false, LineNo = 1, TenantId = 5, CompanyId = 7, UpdatedAt = "2026-10-13T09:00:00", RowVersion = "AAB", JournalEntryId = (long?)null }, t.UserId),
                Row(2, "Update", "CORE.User", t.UserId, new { LanguageCode = "en", DateFormat = "dd/MM/yyyy" }, new { LanguageCode = "ar", DateFormat = "yyyy-MM-dd" }, t.UserId),
                Row(3, "SettingChange", "CFG.CompanySetting", 1, new { SettingKey = "TRN.LockTariffs", SettingValue = "true" }, new { SettingKey = "TRN.LockTariffs", SettingValue = "false" }, t.UserId),
                Row(4, "Update", "TRN.Invoice", 2, new { Status = "Approved", TaxRate = 16m }, new { Status = "Draft", TaxRate = 0m }, null),
            };
            var en = await Describer(ctx, t, "en").DescribeAsync(rows, new DescribeOptions(), Ct);
            Assert.True(en[0].IsTechnicalOnly);   // keys, line numbers, stamps, row versions: nothing for a reader
            Assert.Empty(en[0].Changes);
            Assert.Contains(en[1].Changes, c => c.Field == "Language" && c.OldValue == "العربية" && c.NewValue == "English");
            Assert.Equal("Owner edited user Owner", en[1].Sentence);
            Assert.Equal("Owner changed the setting Lock tariffs", en[2].Sentence);
            Assert.Contains(en[2].Changes, c => c.Field == "Value" && c.OldValue == "No" && c.NewValue == "Yes");
            Assert.StartsWith("The system edited invoice", en[3].Sentence, StringComparison.Ordinal);
            Assert.Contains(en[3].Changes, c => c.Field == "Status" && c.OldValue == "Draft" && c.NewValue == "Approved");
            Assert.Contains(en[3].Changes, c => c.Field == "Tax rate" && c.OldValue == "0%" && c.NewValue == "16%");

            var ar = await Describer(ctx, t, "ar").DescribeAsync(rows, new DescribeOptions(), Ct);
            Assert.Contains(ar[1].Changes, c => c.Field == "اللغة" && c.OldValue == "العربية" && c.NewValue == "English");
            Assert.StartsWith("النظام: تعديل الفاتورة", ar[3].Sentence, StringComparison.Ordinal);
            Assert.Contains(ar[3].Changes, c => c.Field == "الحالة" && c.OldValue == "مسودة" && c.NewValue == "معتمدة");
        }
    }

    // ================================================================= company labels and custom fields

    [Fact]
    public async Task Fields_use_the_companys_labels_and_custom_fields_their_own()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new StandardFieldConfig { EntityType = "TRN.Trip", FieldKey = "receiptNo", Label = "رقم الكشف" });
            ctx.Add(new CustomFieldDefinition { EntityType = "TRN.Trip", FieldKey = "customsNo", LabelAr = "رقم البيان الجمركي", LabelEn = "Customs no.", FieldType = "Text" });
            await ctx.SaveChangesAsync(Ct);
            var rows = new List<AuditRow>
            {
                Row(1, "Update", "TRN.Trip", 1, new { ReceiptNo = "R-2", CustomValues = Json(new { customsNo = "C-9" }) }, new { ReceiptNo = "R-1", CustomValues = Json(new { customsNo = "C-1" }) }, t.UserId),
            };
            var ar = (await Describer(ctx, t, "ar").DescribeAsync(rows, new DescribeOptions(), Ct)).Single();
            Assert.Contains(ar.Changes, c => c.Field == "رقم الكشف" && c.OldValue == "R-1" && c.NewValue == "R-2");   // the company's label
            Assert.Contains(ar.Changes, c => c.Field == "رقم البيان الجمركي" && c.OldValue == "C-1" && c.NewValue == "C-9");
            var en = (await Describer(ctx, t, "en").DescribeAsync(rows, new DescribeOptions(), Ct)).Single();
            Assert.Contains(en.Changes, c => c.Field == "Customs no.");
        }
    }

    // ================================================================= a journal entry and its lines: one event

    [Fact]
    public async Task A_journal_entry_and_its_lines_are_one_event_with_its_source_document()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var wages = new Account { Code = "5101", NameAr = "أجور السائقين", NameEn = "Driver wages", AccountType = "Expense" };
            var cash = new Account { Code = "1101", NameAr = "الصندوق", NameEn = "Cash", AccountType = "Asset" };
            var owned = new OwnershipType { Name = "Company", FleetClass = "Own", IsBuiltIn = true };
            ctx.AddRange(wages, cash, owned);
            await ctx.SaveChangesAsync(Ct);
            var driver = new Driver
            {
                FullName = "Ahmad Saleh", NationalId = "9871234560", Phone1 = "0791234567", LicenceNo = "L-1", LicenceType = "C", LicenceExpiry = new DateOnly(2027, 1, 1),
                PlateNumber = "10-11111", CapacityTons = 32m, OwnershipTypeId = owned.Id, RegistrationExpiry = new DateOnly(2027, 1, 1),
                InsuranceExpiry = new DateOnly(2027, 1, 1), InspectionExpiry = new DateOnly(2027, 1, 1),
            };
            var box = new CashBox { Name = "Main", AccountId = cash.Id };
            ctx.AddRange(driver, box);
            await ctx.SaveChangesAsync(Ct);
            var settlement = new Settlement
            {
                Number = "STL-00002", DriverId = driver.Id, SettlementDate = new DateOnly(2026, 10, 14), GrossWages = 100m, NetPayable = 100m, PaymentMethod = "Cash", CashBoxId = box.Id,
            };
            ctx.Add(settlement);
            await ctx.SaveChangesAsync(Ct);
            var entry = new JournalEntry
            {
                Number = "JV-00022", EntryDate = new DateOnly(2026, 10, 14), PeriodMonth = "2026-10", Description = "Driver settlement", SourceModule = "TRN",
                SourceType = "Settlement", SourceId = settlement.Id, TotalDebit = 100m, TotalCredit = 100m,
            };
            entry.Lines.Add(new EntryLine { LineNo = 1, AccountId = wages.Id, Debit = 100m, PartyModule = "TRN", PartyType = "Driver", PartyId = driver.Id });
            entry.Lines.Add(new EntryLine { LineNo = 2, AccountId = cash.Id, Credit = 100m });
            ctx.Add(entry);
            await ctx.SaveChangesAsync(Ct);

            var stored = await ctx.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == "ACC.JournalEntry" && a.EntityId == entry.Id)
                .Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, "Post", a.EntityType, a.EntityId, a.OldValues, a.NewValues)).ToListAsync(Ct);
            var en = (await Describer(ctx, t, "en").DescribeAsync(stored, new DescribeOptions(), Ct)).Single();
            Assert.Equal("Owner posted journal entry JV-00022 (Driver settlement STL-00002)", en.Sentence);
            Assert.Equal([("5101 · Driver wages — Ahmad Saleh", "100.000", null), ("1101 · Cash", null, "100.000")],
                en.Lines.Select(l => (l.Account, l.Debit, l.Credit)).ToArray());
            Assert.Equal(("Settlement", "STL-00002"), (en.Links.Single().Kind, en.Links.Single().Text));
            var ar = (await Describer(ctx, t, "ar").DescribeAsync(stored, new DescribeOptions(), Ct)).Single();
            Assert.Equal("Owner: ترحيل القيد JV-00022 (تسوية سائق STL-00002)", ar.Sentence);
            Assert.Equal("1101 · الصندوق", ar.Lines[1].Account);

            // Rows written before DT-188 carried a temporary key for the entry: their lines still join the entry's event.
            var legacyAt = At.AddDays(-1);
            ctx.Add(new AuditLog { TenantId = t.TenantId, CompanyId = t.CompanyId, UserId = t.UserId, OccurredAt = legacyAt, Action = "Create", EntityType = "ACC.EntryLine", EntityId = 990_101,
                NewValues = Json(new { JournalEntryId = -9223372036854774806L, LineNo = 1, AccountId = cash.Id, Debit = 7m, Credit = 0m }) });
            await ctx.SaveChangesAsync(Ct);
            var legacy = Row(77, "Post", "ACC.JournalEntry", 990_100, new { Number = "JV-00009", SourceModule = "ACC" }, null, t.UserId, legacyAt);
            var old = (await Describer(ctx, t, "en").DescribeAsync([legacy], new DescribeOptions(), Ct)).Single();
            Assert.Equal("1101 · Cash", Assert.Single(old.Lines).Account);
        }
    }
}
