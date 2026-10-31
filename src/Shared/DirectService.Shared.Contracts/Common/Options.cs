namespace DirectService.Shared.Contracts;

/// <summary>
/// An active record offered in pick lists of forms (FR-162 "archived records hidden from new-entry lists", FR-167); shared by every module
/// (moved from the transport master data in Stage 3 session 6, same name: the OpenAPI schema is unchanged).
/// </summary>
public sealed record MasterOption(string Id, string Name);
