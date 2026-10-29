using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.EInvoicing;

/// <summary>
/// Row locks of e-invoicing (raw SQL, listed in BUILD_STATUS.md): the company's invoice counter (ICV) incremented atomically inside
/// the document's transaction (gap-free with the document, one counter for every module); submissions claimed with
/// UPDLOCK + READPAST so two workers or a worker and a user never send the same document twice; and a waiting lock used by voids.
/// </summary>
public sealed class EInvoiceStore(MasterDbContext db, IDataScope scope) : IEInvoiceStore
{
    private long CompanyId => scope.CompanyId ?? throw new InvalidOperationException("No company in scope.");

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("E-invoicing locks are taken only inside a transaction.");
    }

    public async Task<long> NextIcvAsync(CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        // One statement: the exclusive row lock is held until the document's transaction ends (a rollback returns the value).
        var values = await db.Database.SqlQuery<long>($"""
            UPDATE cfg.EInvoiceSettings SET IcvCounter = IcvCounter + 1
            OUTPUT inserted.IcvCounter AS Value
            WHERE CompanyId = {companyId} AND IsDeleted = 0
            """).ToListAsync(ct).ConfigureAwait(false);
        return values.Count == 1 ? values[0] : throw new InvalidOperationException($"E-invoicing settings are missing for company {companyId}.");
    }

    public async Task<bool> TryClaimAsync(long submissionId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        var rows = await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM trn.TRN_EInvoiceSubmissions WITH (UPDLOCK, ROWLOCK, READPAST)
            WHERE Id = {submissionId} AND CompanyId = {companyId} AND IsDeleted = 0
            """).ToListAsync(ct).ConfigureAwait(false);
        return rows.Count == 1;
    }

    public async Task LockAsync(long submissionId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM trn.TRN_EInvoiceSubmissions WITH (UPDLOCK, ROWLOCK)
            WHERE Id = {submissionId} AND CompanyId = {companyId} AND IsDeleted = 0
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task LockDocumentAsync(long? invoiceId, long? creditNoteId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM trn.TRN_EInvoiceSubmissions WITH (UPDLOCK, ROWLOCK)
            WHERE CompanyId = {companyId} AND IsDeleted = 0 AND (InvoiceId = {invoiceId} OR CreditNoteId = {creditNoteId})
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryClaimApArAsync(long submissionId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        var rows = await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM acc.ApArEInvoiceSubmissions WITH (UPDLOCK, ROWLOCK, READPAST)
            WHERE Id = {submissionId} AND CompanyId = {companyId} AND IsDeleted = 0
            """).ToListAsync(ct).ConfigureAwait(false);
        return rows.Count == 1;
    }

    public async Task LockApArAsync(long submissionId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM acc.ApArEInvoiceSubmissions WITH (UPDLOCK, ROWLOCK)
            WHERE Id = {submissionId} AND CompanyId = {companyId} AND IsDeleted = 0
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task LockApArDocumentAsync(long? documentId, long? returnId, CancellationToken ct)
    {
        RequireTransaction();
        var companyId = CompanyId;
        await db.Database.SqlQuery<long>($"""
            SELECT Id AS Value FROM acc.ApArEInvoiceSubmissions WITH (UPDLOCK, ROWLOCK)
            WHERE CompanyId = {companyId} AND IsDeleted = 0 AND (ApArDocumentId = {documentId} OR ApArReturnId = {returnId})
            """).ToListAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Credentials encrypted with ASP.NET Core Data Protection under their own purpose (SEC-09).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector(DataProtectionPurposes.EInvoiceCredentials);

    public byte[] Protect(string value) => _protector.Protect(System.Text.Encoding.UTF8.GetBytes(value));

    public string Unprotect(byte[] value) => System.Text.Encoding.UTF8.GetString(_protector.Unprotect(value));
}

/// <summary>In-process wake-up of the e-invoicing worker (a signal, not a queue: the table is the queue).</summary>
public sealed class EInvoiceSignal : IEInvoiceSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    /// <summary>Waits for a signal or the timeout; true when signalled.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}
