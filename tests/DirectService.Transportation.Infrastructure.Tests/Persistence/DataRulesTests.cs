using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Tests.Persistence;

/// <summary>
/// Step 3 integration tests: the data-access rules of the build prompt, against SQL Server.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DataRulesTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_is_converted_to_soft_delete_with_who_when_why_and_is_audited()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var m = new Material { Name = "Phosphate" };
            ctx.Add(m);
            await ctx.SaveChangesAsync(Ct);

            m.DeleteReason = "Entered by mistake";
            ctx.Remove(m);
            await ctx.SaveChangesAsync(Ct);

            Assert.False(await ctx.Set<Material>().AnyAsync(x => x.Id == m.Id, Ct));
            var row = await ctx.Set<Material>().IgnoreQueryFilters([QueryFilters.SoftDelete])
                .AsNoTracking().SingleAsync(x => x.Id == m.Id, Ct);
            Assert.True(row.IsDeleted);
            Assert.Equal(t.UserId, row.DeletedBy);
            Assert.NotNull(row.DeletedAt);
            Assert.Equal("Entered by mistake", row.DeleteReason);

            var audit = await ctx.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityType == "TRN.Material" && a.EntityId == m.Id)
                .OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct);
            Assert.Equal(["Create", "Delete"], audit);
        }
    }

    [Fact]
    public async Task Delete_without_reason_is_refused()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var m = new Material { Name = "Potash" };
            ctx.Add(m);
            await ctx.SaveChangesAsync(Ct);
            ctx.Remove(m);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
            Assert.Contains("reason", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Append_only_audit_log_cannot_be_changed_or_deleted()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new Material { Name = "Sand" });
            await ctx.SaveChangesAsync(Ct);
            var log = await ctx.Set<AuditLog>().Where(a => a.TenantId == t.TenantId).FirstAsync(Ct);
            log.Action = "Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
        }
        var (ctx2, _) = db.ContextFor(t);
        await using (ctx2)
        {
            var log = await ctx2.Set<AuditLog>().Where(a => a.TenantId == t.TenantId).FirstAsync(Ct);
            ctx2.Remove(log);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx2.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Tenant_and_company_filters_isolate_data()
    {
        var a = await db.CreateTenantAsync();
        var b = await db.CreateTenantAsync();
        var a2 = await db.AddCompanyAsync(a);

        long materialId;
        var (ctxA, _) = db.ContextFor(a);
        await using (ctxA)
        {
            var m = new Material { Name = "Grain" };
            ctxA.Add(m);
            await ctxA.SaveChangesAsync(Ct);
            materialId = m.Id;
            Assert.True(await ctxA.Set<Material>().AnyAsync(x => x.Id == materialId, Ct));
        }

        var (ctxB, _) = db.ContextFor(b);
        await using (ctxB)
        {
            Assert.False(await ctxB.Set<Material>().AnyAsync(x => x.Id == materialId, Ct));
            Assert.False(await ctxB.Set<Company>().AnyAsync(x => x.Id == a.CompanyId, Ct));
            Assert.False(await ctxB.Set<User>().AnyAsync(x => x.Id == a.UserId, Ct));
        }

        var (ctxA2, _) = db.ContextFor(a, a2);
        await using (ctxA2)
        {
            Assert.False(await ctxA2.Set<Material>().AnyAsync(x => x.Id == materialId, Ct));
            // tenant-level rows stay visible across the tenant's companies
            Assert.True(await ctxA2.Set<Company>().AnyAsync(x => x.Id == a.CompanyId, Ct));
        }
    }

    [Fact]
    public async Task Writes_into_another_tenant_or_company_are_refused()
    {
        var a = await db.CreateTenantAsync();
        var b = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(a);
        await using (ctx)
        {
            ctx.Add(new Material { Name = "Clinker", TenantId = b.TenantId, CompanyId = b.CompanyId });
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync(Ct));
        }
        var (ctx2, _) = db.ContextFor(a);
        await using (ctx2)
        {
            ctx2.Add(new Material { Name = "Clinker", CompanyId = b.CompanyId });
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx2.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Stamps_are_set_by_the_server_and_creation_columns_never_change()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var m = new Material { Name = "Gypsum" };
            ctx.Add(m);
            await ctx.SaveChangesAsync(Ct);
            Assert.Equal(t.TenantId, m.TenantId);
            Assert.Equal(t.CompanyId, m.CompanyId);
            Assert.Equal(t.UserId, m.CreatedBy);
            var created = m.CreatedAt;
            var publicId = m.PublicId;
            Assert.NotEqual(Guid.Empty, publicId);

            m.Name = "Gypsum (fine)";
            m.CreatedAt = created.AddDays(-10);
            await ctx.SaveChangesAsync(Ct);
            var row = await ctx.Set<Material>().AsNoTracking().SingleAsync(x => x.Id == m.Id, Ct);
            Assert.Equal(created, row.CreatedAt, TimeSpan.FromMilliseconds(5));
            Assert.Equal(t.UserId, row.UpdatedBy);
            Assert.NotNull(row.UpdatedAt);
            Assert.Equal(publicId, row.PublicId);
        }
    }

    [Fact]
    public async Task Audit_masks_secrets()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var json = await ctx.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityType == "CORE.User" && a.EntityId == t.UserId && a.Action == "Create")
                .Select(a => a.NewValues).SingleAsync(Ct);
            Assert.NotNull(json);
            Assert.Contains("\"PasswordHash\":\"***\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("test-hash", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RowVersion_refuses_a_stale_update()
    {
        var t = await db.CreateTenantAsync();
        long id;
        var (c1, _) = db.ContextFor(t);
        await using (c1)
        {
            var m = new Material { Name = "Basalt" };
            c1.Add(m);
            await c1.SaveChangesAsync(Ct);
            id = m.Id;
        }
        var (a, _) = db.ContextFor(t);
        var (b, _) = db.ContextFor(t);
        await using (a)
        await using (b)
        {
            var ma = await a.Set<Material>().SingleAsync(x => x.Id == id, Ct);
            var mb = await b.Set<Material>().SingleAsync(x => x.Id == id, Ct);
            ma.Name = "Basalt A";
            await a.SaveChangesAsync(Ct);
            mb.Name = "Basalt B";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Filtered_unique_index_ignores_soft_deleted_rows()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var m = new Material { Name = "Limestone" };
            ctx.Add(m);
            await ctx.SaveChangesAsync(Ct);

            ctx.Add(new Material { Name = "Limestone" });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
            ctx.ChangeTracker.Clear();

            var tracked = await ctx.Set<Material>().SingleAsync(x => x.Id == m.Id, Ct);
            tracked.DeleteReason = "Duplicate";
            ctx.Remove(tracked);
            await ctx.SaveChangesAsync(Ct);

            ctx.Add(new Material { Name = "Limestone" });
            await ctx.SaveChangesAsync(Ct); // allowed: the old row is soft-deleted
        }
    }

    [Fact]
    public async Task Collation_is_case_and_accent_insensitive_and_equates_Arabic_digits()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new LoadingSite { Name = "موقع 123" });
            await ctx.SaveChangesAsync(Ct);
            ctx.Add(new LoadingSite { Name = "موقع ١٢٣" });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
            ctx.ChangeTracker.Clear();

            ctx.Add(new UnloadingSite { Name = "Aqaba Port" });
            await ctx.SaveChangesAsync(Ct);
            ctx.Add(new UnloadingSite { Name = "AQABA PORT" });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Varchar_columns_keep_arabic_characters_exactly()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var o = new OwnershipType { Name = "Company", FleetClass = "Own", IsBuiltIn = true };
            ctx.Add(o);
            await ctx.SaveChangesAsync(Ct);
            var d = new Driver
            {
                FullName = "سائق", NationalId = "9871234560", Phone1 = "0791234567", LicenceNo = "رخصة-٤٥",
                LicenceType = "Heavy", LicenceExpiry = new DateOnly(2027, 1, 1), PlateNumber = "١٢-٣٤٥٦٧",
                CapacityTons = 32m, OwnershipTypeId = o.Id, RegistrationExpiry = new DateOnly(2027, 1, 1),
                InsuranceExpiry = new DateOnly(2027, 1, 1), InspectionExpiry = new DateOnly(2027, 1, 1),
            };
            ctx.Add(d);
            await ctx.SaveChangesAsync(Ct);
            var row = await ctx.Set<Driver>().AsNoTracking().SingleAsync(x => x.Id == d.Id, Ct);
            Assert.Equal("رخصة-٤٥", row.LicenceNo);
            Assert.Equal("١٢-٣٤٥٦٧", row.PlateNumber);
        }
    }

    [Fact]
    public async Task Tariff_specificity_is_computed_by_the_database()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var customer = new Customer { Name = "Arab Potash" };
            var material = new Material { Name = "Potash" };
            ctx.AddRange(customer, material);
            await ctx.SaveChangesAsync(Ct);

            var all = new Tariff { PartyType = "Customer", Price = 5.5m };
            var partyMaterial = new Tariff { PartyType = "Customer", CustomerId = customer.Id, MaterialId = material.Id, Price = 6m };
            ctx.AddRange(all, partyMaterial);
            await ctx.SaveChangesAsync(Ct);

            var scores = await ctx.Set<Tariff>().AsNoTracking()
                .Where(x => x.Id == all.Id || x.Id == partyMaterial.Id)
                .OrderBy(x => x.Id).Select(x => x.Specificity).ToListAsync(Ct);
            Assert.Equal(new byte[] { 0, 12 }, scores);
        }
    }

    [Fact]
    public async Task Check_constraints_are_enforced_by_the_database()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var site = new LoadingSite { Name = "Shidiya" };
            ctx.Add(site);
            await ctx.SaveChangesAsync(Ct);

            // CK_TRN_MonthlySiteRates_OwnFleetRate_ExternalFleetRate: at least one rate
            ctx.Add(new MonthlySiteRate { RateMonth = "2026-10", LoadingSiteId = site.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
            ctx.ChangeTracker.Clear();

            // CK_TRN_MonthlySiteRates_RateMonth: YYYY-MM
            ctx.Add(new MonthlySiteRate { RateMonth = "10-2026", LoadingSiteId = site.Id, OwnFleetRate = 3.5m });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
            ctx.ChangeTracker.Clear();

            ctx.Add(new MonthlySiteRate { RateMonth = "2026-10", LoadingSiteId = site.Id, OwnFleetRate = 3.5m });
            await ctx.SaveChangesAsync(Ct);
        }
    }

    [Fact]
    public async Task Reference_data_is_seeded()
    {
        var (ctx, _) = db.ContextFor(await db.CreateTenantAsync());
        await using (ctx)
        {
            var keys = await ctx.Set<Permission>().AsNoTracking().Select(p => p.PermissionKey).ToListAsync(Ct);
            Assert.Equal(26, keys.Count);   // 22 of PRD 3.4 + 3 budget keys (revision 6 part B) + acct.postentry (DT-201)
            Assert.Contains("trn.trips.view", keys);
            Assert.Contains("acc.acct.maint", keys);
            Assert.Contains("core.tenant.manage", keys);
            Assert.All(keys, k => Assert.Matches("^(trn|acc|core)\\.[a-z]+\\.[a-z]+$", k));
            var plan = await ctx.Set<Plan>().AsNoTracking().SingleAsync(p => p.Code == "TRIAL", Ct);
            Assert.False(plan.IsActive);
        }
    }

    [Fact]
    public async Task Company_defaults_follow_revision_5_and_the_design()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var c = await ctx.Set<Company>().AsNoTracking().SingleAsync(x => x.Id == t.CompanyId, Ct);
            Assert.Equal("Active", c.Status);
            Assert.Equal("JO", c.CountryCode);
            Assert.Equal("JOD", c.CurrencyCode);
            Assert.Equal(3, c.CurrencyDecimals);
            Assert.Equal(16.00m, c.TaxRate);
            var tenant = await ctx.Set<Tenant>().AsNoTracking().SingleAsync(x => x.Id == t.TenantId, Ct);
            Assert.Equal(8, tenant.PasswordMinLength);
            Assert.Equal(30, tenant.SessionIdleTimeoutMinutes);
            Assert.True(tenant.PasswordRequireLetter && tenant.PasswordRequireDigit);
            Assert.Equal("Trial", tenant.Status);
        }
    }

    [Fact]
    public async Task Invoice_layout_type_defaults_to_claim_statement()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            Assert.Equal("Claim", new Invoice().LayoutType);
            var dflt = await ctx.Database.SqlQueryRaw<string>(
                "SELECT definition AS [Value] FROM sys.default_constraints WHERE name = 'DF_TRN_Invoices_LayoutType'")
                .SingleAsync(Ct);
            Assert.Equal("('Claim')", dflt);
        }
    }

    [Fact]
    public async Task Record_locks_allow_one_active_holder_per_record()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new RecordLock { EntityType = "TRN.Trip", EntityId = 42, UserId = t.UserId });
            await ctx.SaveChangesAsync(Ct);
            ctx.Add(new RecordLock { EntityType = "TRN.Trip", EntityId = 42, UserId = t.UserId });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task An_update_through_a_modified_entry_is_audited_as_update_and_only_a_real_undelete_as_restore()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var m = new Material { Name = "Gypsum" };
            ctx.Add(m);
            await ctx.SaveChangesAsync(Ct);

            // The row-version check marks the whole entry Modified (every property flagged, IsDeleted included).
            ctx.Entry(m).State = EntityState.Modified;
            m.Name = "Gypsum (fine)";
            await ctx.SaveChangesAsync(Ct);

            m.DeleteReason = "Duplicate";
            ctx.Remove(m);
            await ctx.SaveChangesAsync(Ct);

            var deleted = await ctx.Set<Material>().IgnoreQueryFilters([QueryFilters.SoftDelete]).SingleAsync(x => x.Id == m.Id, Ct);
            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            deleted.DeletedBy = null;
            deleted.DeleteReason = null;
            await ctx.SaveChangesAsync(Ct);

            var audit = await ctx.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityType == "TRN.Material" && a.EntityId == m.Id)
                .OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct);
            Assert.Equal(["Create", "Update", "Delete", "Restore"], audit);
        }
    }

    /// <summary>
    /// DT-188: a key of a row inserted in the same save (a journal entry and its lines, an account moved under a new parent) is written
    /// to the audit log as the real saved id, never EF's temporary negative key ("JournalEntryId: — → -9223372036854774806").
    /// </summary>
    [Fact]
    public async Task Audit_records_the_saved_ids_of_rows_inserted_in_the_same_save()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            var cash = new Domain.Acc.Account { Code = "1101", NameAr = "الصندوق", AccountType = "Asset" };
            ctx.Add(cash);
            await ctx.SaveChangesAsync(Ct);

            var parent = new Domain.Acc.Account { Code = "11", NameAr = "النقدية", AccountType = "Asset", IsPostable = false };
            ctx.Add(parent);
            cash.Parent = parent;   // an existing row now points at a row inserted in this save
            var entry = new Domain.Acc.JournalEntry
            {
                Number = "JV-00001", EntryDate = new DateOnly(2026, 10, 14), PeriodMonth = "2026-10", Description = "Test", SourceModule = "ACC",
                IsManual = true, TotalDebit = 5, TotalCredit = 5,
            };
            entry.Lines.Add(new Domain.Acc.EntryLine { LineNo = 1, Account = cash, Debit = 5 });
            entry.Lines.Add(new Domain.Acc.EntryLine { LineNo = 2, Account = cash, Credit = 5 });
            ctx.Add(entry);
            await ctx.SaveChangesAsync(Ct);

            var lines = await ctx.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == "ACC.EntryLine" && a.TenantId == t.TenantId).Select(a => a.NewValues!).ToListAsync(Ct);
            Assert.Equal(2, lines.Count);
            Assert.All(lines, json => Assert.Contains($"\"JournalEntryId\":{entry.Id},", json, StringComparison.Ordinal));
            var moved = await ctx.Set<AuditLog>().AsNoTracking().Where(a => a.EntityType == "ACC.Account" && a.EntityId == cash.Id && a.Action == "Update")
                .Select(a => a.NewValues!).SingleAsync(Ct);
            Assert.Contains($"\"ParentId\":{parent.Id}", moved, StringComparison.Ordinal);
            Assert.True(parent.Id > 0 && entry.Id > 0);
            Assert.DoesNotContain(":-", string.Concat(lines) + moved, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Customer_name_is_unique_among_active_customers_only()
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new Customer { Name = "Al-Hayat", Status = "Archived" });
            ctx.Add(new Customer { Name = "Al-Hayat" });
            await ctx.SaveChangesAsync(Ct);

            ctx.Add(new Customer { Name = "al-hayat" });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }
    }
}
