using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Account mapping (core.settings.manage; FR-092 … FR-095): every posting key with its account and status
/// Mapped / Missing; only active, postable accounts can be chosen; changes apply to new entries only and are audited
/// old → new by the interceptor (AccountId change on acc.PostingKeyMappings).
/// </summary>
public sealed class PostingKeyService(IAppDbContext db, PostingEngine engine, IUrlTokenService tokens, IChangeContext changes,
    OpeningBalanceService openings, PendingPostingService pending, IRequestContext ctx)
{
    public const string Mapped = "Mapped";
    public const string Missing = "Missing";

    public async Task<IReadOnlyList<PostingKeyResponse>> ListAsync(CancellationToken ct)
    {
        var setupOpen = await engine.IsSetupOpenAsync(ct).ConfigureAwait(false);
        var mappings = await db.Set<PostingKeyMapping>().AsNoTracking().Include(m => m.Account)
            .ToDictionaryAsync(m => m.PostingKey, ct).ConfigureAwait(false);
        return PostingKeys.All.Select(d =>
        {
            var m = mappings.GetValueOrDefault(d.Key);
            return new PostingKeyResponse(d.Key, d.NameAr, d.NameEn, !d.RequiredOnlyDuringSetup || setupOpen,
                m is null ? null : new AccountRef(tokens.Encode<Account>(m.Account.PublicId), m.Account.Code, m.Account.NameAr, m.Account.NameEn),
                m is null ? Missing : Mapped);
        }).ToList();
    }

    public async Task<PostingKeyResponse> SetAsync(string postingKey, PostingKeyRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var definition = PostingKeys.Find(postingKey) ?? throw Fail.NotFound();
        var accountPublicId = tokens.Decode<Account>(r.AccountId) ?? throw Fail.Field("accountId", ErrorCodes.ValidationRequired, new FieldRef("accountId"));
        var account = await db.Set<Account>().AsNoTracking().Where(a => a.PublicId == accountPublicId)
            .Select(a => new { a.Id, a.Code, a.Status, a.IsPostable, HasChildren = a.Children.Any() })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        if (account.Status != AccountingCodes.Active || !account.IsPostable || account.HasChildren)
            throw Fail.RuleOn("accountId", ErrorCodes.AccountNotPostable, account.Code); // FR-093

        var mapping = await db.Set<PostingKeyMapping>().SingleOrDefaultAsync(m => m.PostingKey == definition.Key, ct).ConfigureAwait(false);
        if (mapping is null)
        {
            db.Set<PostingKeyMapping>().Add(new PostingKeyMapping { PostingKey = definition.Key, AccountId = account.Id });
        }
        else if (mapping.AccountId != account.Id)
        {
            mapping.AccountId = account.Id;
            changes.SetAction(mapping, "MappingChange");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ListAsync(ct).ConfigureAwait(false)).Single(k => k.Key == definition.Key);
    }

    /// <summary>App-shell banners: posting readiness (FR-094), opening difference warning (FR-110), pending postings (FR-101).</summary>
    public async Task<AccountingStatusResponse> StatusAsync(CancellationToken ct)
    {
        var missing = await engine.MissingKeysAsync(ct).ConfigureAwait(false);
        var difference = await openings.DifferenceBalanceAsync(ct).ConfigureAwait(false);
        return new AccountingStatusResponse(missing.Count == 0, missing, difference != 0, difference,
            await pending.CountAsync(ct).ConfigureAwait(false), PostingKeys.Names(missing, ctx.Language));
    }
}
