namespace DirectService.Accounting.Contracts.Posting;

/// <summary>The document an entry belongs to; stored as SourceModule / SourceType / SourceId of acc.JournalEntries (values never change, design e).</summary>
public sealed record DocumentRef(string Module, string Type, long Id);

/// <summary>A party on an entry line; stored as PartyModule / PartyType / PartyId of acc.EntryLines (e.g. TRN/Customer/17, ACC/ApArParty/9).</summary>
public sealed record PartyRef(string Module, string Type, long Id);

/// <summary>
/// One line of an entry: either a posting key (resolved through the company's account mapping) or an explicit account (cash box, bank), never
/// both. Exactly one of <see cref="Debit"/> and <see cref="Credit"/> is above zero, already rounded to the currency's decimals. With a tax code
/// the amount is before tax and the engine adds the tax line on the same side to <see cref="TaxAccountId"/> (DT-255).
/// </summary>
public sealed record PostingLineSpec(
    string? MappingKey, long? AccountId,
    decimal Debit, decimal Credit,
    long? CostCentreId = null, PartyRef? Party = null,
    string? Note = null, string? Reference = null,
    long? TaxCodeId = null, long? TaxAccountId = null)
{
    /// <summary>A debit of a posting key (Stage 3 session 4b, like the engine's <c>PostingLine.DebitKey</c>).</summary>
    public static PostingLineSpec DebitKey(string key, decimal amount, PartyRef? party = null) => new(key, null, amount, 0, null, party);

    /// <summary>A credit of a posting key.</summary>
    public static PostingLineSpec CreditKey(string key, decimal amount, PartyRef? party = null) => new(key, null, 0, amount, null, party);
}

/// <summary>The entry of a document (FR-097): dated like the document (BR-034), with its description, reference and lines.</summary>
public sealed record PostingDocument(DocumentRef Source, DateOnly EntryDate, string Description, string? Reference, IReadOnlyList<PostingLineSpec> Lines);

/// <summary>A posted entry: its id (for the document's JournalEntryId), number and URL token.</summary>
public sealed record PostedEntry(long JournalEntryId, string Number, string Token);

/// <summary>
/// How every module posts to accounting (design c). Each call runs in the caller's transaction, so the document, its entry, the number and
/// the audit rows commit together (NFR-04): without an ambient transaction the posting methods throw <see cref="InvalidOperationException"/>.
/// Business failures (unbalanced, closed period, unmapped key, unpostable account, missing control party) throw the usual problem (FR-098).
/// </summary>
public interface IAccountingPosting
{
    /// <summary>Refuses (posting.keysMissing) while a required posting key of the product (and of ACC) is unmapped (FR-094).</summary>
    Task EnsureReadyAsync(string productCode, CancellationToken ct);

    /// <summary>
    /// Posts the entry; a technical failure rolls back to a savepoint, keeps the document and queues the entry in acc.PendingPostings
    /// (one open row per document) for the source's <see cref="IPostingSourceHandler"/>: then null (FR-101). Idempotent like <see cref="PostAsync"/>.
    /// </summary>
    Task<PostedEntry?> PostOrPendAsync(PostingDocument document, CancellationToken ct);

    /// <summary>
    /// Posts the entry; every failure refuses (FR-098). Idempotent: when the source already has a posted entry that is not reversed, that entry
    /// is returned and nothing is posted (checked under the company's journal-number lock).
    /// </summary>
    Task<PostedEntry> PostAsync(PostingDocument document, CancellationToken ct);

    /// <summary>
    /// Reverses the source's current posted entry (a mirror entry dated like it, linked both ways — FR-099): an edit is reverse + post. With
    /// <paramref name="voidReason"/> the original is marked Void with the reason. Null when the source has no posted entry (e.g. a trip saved
    /// without wage accrual).
    /// </summary>
    Task<PostedEntry?> ReverseAsync(DocumentRef source, string description, string? voidReason, CancellationToken ct);

    /// <summary>The decimals of the company's currency (amounts are rounded by the caller before posting).</summary>
    Task<int> CurrencyDecimalsAsync(CancellationToken ct);
}

/// <summary>A document's number, URL token and type, for links in lists, pending postings and audit.</summary>
public sealed record PostingSourceInfo(string Number, string? Token);

/// <summary>
/// Registered by the module that owns a document type (one per Module + Type): the pending-posting retry rebuilds the entry with it and
/// stores the entry's id on the document; lists describe the source with it (set-based: one call per type and page).
/// </summary>
public interface IPostingSourceHandler
{
    string Module { get; }

    string Type { get; }

    /// <summary>The entry the document needs now, or null when it needs none any more (e.g. voided, or already posted).</summary>
    Task<PostingDocument?> RebuildAsync(long sourceId, CancellationToken ct);

    /// <summary>Stores the posted entry's id on the document (its JournalEntryId).</summary>
    Task AttachAsync(long sourceId, long journalEntryId, CancellationToken ct);

    /// <summary>Number and token of each document that still exists.</summary>
    Task<IReadOnlyDictionary<long, PostingSourceInfo>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct);
}

/// <summary>One account-mapping key of a product (stored string never changes, design e / E-12; its prefix is the product code).</summary>
public sealed record PostingKeyDef(string Key, string NameAr, string NameEn, bool RequiredOnlyDuringSetup, bool Optional, string? TemplateAccountCode);

/// <summary>The posting keys a product owns (ACC by Accounting, TRN by Transportation, …); the mapping screen and the posting banner list them.</summary>
public interface IPostingKeyProvider
{
    string ProductCode { get; }

    IReadOnlyList<PostingKeyDef> Keys { get; }
}

/// <summary>
/// The source documents of entries as links (Stage 3 session 6c): each (module, type, id) triple with a handler of the contract becomes the
/// document's link (type, token, number); triples without a handler are left out.
/// </summary>
public interface IPostingSourceLinks
{
    Task<Dictionary<(string Module, string Type, long Id), DirectService.Accounting.Contracts.Accounting.SourceDocumentRef>> DescribeAsync(
        IEnumerable<(string Module, string? Type, long? Id)> sources, CancellationToken ct);
}

/// <summary>
/// The void reasons of a module's documents (Stage 3 session 6c): the journal shows why an automatic entry of a voided document was reversed.
/// Only documents that exist (query filters apply) are returned; a reason may be empty.
/// </summary>
public interface IDocumentVoidReasonSource
{
    /// <summary>The source types of entries (acc.JournalEntries.SourceType) the module answers for.</summary>
    IReadOnlyList<string> Types { get; }

    Task<IReadOnlyDictionary<long, string?>> VoidReasonsAsync(string type, IReadOnlyCollection<long> ids, CancellationToken ct);
}
