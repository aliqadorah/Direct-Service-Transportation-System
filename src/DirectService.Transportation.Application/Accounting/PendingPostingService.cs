using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// A module's financial document type that posts automatic entries (8.6). Registered by each document module
/// (trips B5, invoices B6, cash documents B8, AP/AR B9) so pending postings can be retried and entries can link
/// back to their source (FR-097, FR-101, FR-102).
/// </summary>
public interface IPostingSourceHandler
{
    string SourceModule { get; }
    string SourceType { get; }

    /// <summary>The entry the document should post now, or null when it no longer needs one (e.g. voided meanwhile).</summary>
    Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct);

    /// <summary>Links the posted entry to the document (its JournalEntryId).</summary>
    Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct);

    /// <summary>Number and URL token of each document, for links from the journal.</summary>
    Task<IReadOnlyDictionary<long, SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct);
}

/// <summary>The registered <see cref="IPostingSourceHandler"/>s by module and type.</summary>
public sealed class PostingSources(IEnumerable<IPostingSourceHandler> handlers)
{
    private readonly Dictionary<(string, string), IPostingSourceHandler> _byType =
        handlers.ToDictionary(h => (h.SourceModule, h.SourceType));

    public IPostingSourceHandler? Find(string module, string? type) =>
        type is not null && _byType.TryGetValue((module, type), out var h) ? h : null;

    /// <summary>Source document links for a set of (module, type, id) triples.</summary>
    public async Task<Dictionary<(string Module, string Type, long Id), SourceDocumentRef>> DescribeAsync(
        IEnumerable<(string Module, string? Type, long? Id)> sources, CancellationToken ct)
    {
        var result = new Dictionary<(string, string, long), SourceDocumentRef>();
        foreach (var group in sources.Where(s => s.Type is not null && s.Id is not null).GroupBy(s => (s.Module, s.Type!)))
        {
            var handler = Find(group.Key.Module, group.Key.Item2);
            if (handler is null) continue;
            var ids = group.Select(s => s.Id!.Value).Distinct().ToList();
            foreach (var (id, doc) in await handler.DescribeAsync(ids, ct).ConfigureAwait(false))
                result[(group.Key.Module, group.Key.Item2, id)] = doc;
        }
        return result;
    }
}

/// <summary>
/// Pending postings (FR-101): list (acc.je.view), retry one or all (acc.je.export) and the automatic retry of the
/// background worker. A retry posts in its own transaction; business refusals and technical failures keep the row
/// open with the new reason and attempt count.
/// </summary>
public sealed class PendingPostingService(IAppDbContext db, PostingEngine engine, PostingSources sources, IUrlTokenService tokens,
    IRequestContext ctx, TimeProvider clock, Microsoft.Extensions.Logging.ILogger<PendingPostingService>? log = null)
{
    public async Task<IReadOnlyList<PendingPostingResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<PendingPosting>().AsNoTracking().Where(p => p.ResolvedAt == null)
            .OrderBy(p => p.CreatedAt).Take(1000).ToListAsync(ct).ConfigureAwait(false);
        var docs = await sources.DescribeAsync(rows.Select(r => (r.SourceModule, (string?)r.SourceType, (long?)r.SourceId)), ct).ConfigureAwait(false);
        return rows.Select(r => new PendingPostingResponse(tokens.Encode<PendingPosting>(r.PublicId), r.SourceModule, r.SourceType,
            docs.GetValueOrDefault((r.SourceModule, r.SourceType, r.SourceId)), r.Reason, r.Attempts, r.LastAttemptAt, r.CreatedAt)).ToList();
    }

    public Task<int> CountAsync(CancellationToken ct) =>
        db.Set<PendingPosting>().AsNoTracking().CountAsync(p => p.ResolvedAt == null, ct);

    public async Task<RetryResult> RetryOneAsync(string token, CancellationToken ct)
    {
        var publicId = tokens.Decode<PendingPosting>(token) ?? throw Fail.NotFound();
        var id = await db.Set<PendingPosting>().AsNoTracking().Where(p => p.PublicId == publicId && p.ResolvedAt == null)
            .Select(p => (long?)p.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        var posted = await RetryAsync(id, ct).ConfigureAwait(false);
        return new RetryResult(posted ? 1 : 0, posted ? 0 : 1);
    }

    public async Task<RetryResult> RetryAllAsync(CancellationToken ct)
    {
        var ids = await db.Set<PendingPosting>().AsNoTracking().Where(p => p.ResolvedAt == null).OrderBy(p => p.Id)
            .Select(p => p.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
        int posted = 0, pending = 0;
        foreach (var id in ids)
        {
            if (await RetryAsync(id, ct).ConfigureAwait(false)) posted++;
            else pending++;
        }
        return new RetryResult(posted, pending);
    }

    /// <summary>Retries one open pending posting of the current company; true when it was posted (or no longer needed).</summary>
    public async Task<bool> RetryAsync(long pendingId, CancellationToken ct)
    {
        string failure;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                var p = await db.Set<PendingPosting>().SingleOrDefaultAsync(x => x.Id == pendingId && x.ResolvedAt == null, c).ConfigureAwait(false);
                if (p is null) return;
                var handler = sources.Find(p.SourceModule, p.SourceType);
                if (handler is null) throw Fail.Rule(ErrorCodes.PendingNoHandler);
                var request = await handler.BuildAsync(p.SourceId, c).ConfigureAwait(false);
                if (request is not null)
                {
                    var entry = await engine.PostAsync(request, c).ConfigureAwait(false);
                    await handler.AttachAsync(p.SourceId, entry, c).ConfigureAwait(false);
                }
                p.Attempts++;
                p.LastAttemptAt = clock.GetUtcNow().UtcDateTime;
                p.ResolvedAt = p.LastAttemptAt;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (AppException ex)
        {
            failure = ErrorCatalog.Format(ex.Code, ctx.Language, ex.Args);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = PostingEngine.TechnicalReason(ex, log);
        }

        // Record the failed attempt in a fresh unit of work (the failed one was rolled back).
        db.ChangeTracker.Clear();
        var row = await db.Set<PendingPosting>().SingleOrDefaultAsync(x => x.Id == pendingId, ct).ConfigureAwait(false);
        if (row is not null && row.ResolvedAt is null)
        {
            row.Attempts++;
            row.LastAttemptAt = clock.GetUtcNow().UtcDateTime;
            row.Reason = failure.Length <= 1000 ? failure : failure[..1000];
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return false;
    }
}
