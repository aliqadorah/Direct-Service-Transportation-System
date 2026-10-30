namespace DirectService.Platform.Contracts.Tenants;

/// <summary>
/// A setup step (5.4, P-08): key, title, status (NotStarted / InProgress / Done), what is missing, where to do it, whether marked by hand;
/// <paramref name="Items"/> lists what is still missing in the reader's language (names, never internal codes — DT-224), shown on request.
/// </summary>
public sealed record SetupStep(string Key, string Title, string Status, string? Detail, string Link, bool Marked, IReadOnlyList<string>? Items = null);

/// <summary>The setup checklist of the company: the eight steps in any order, progress, resumable (G-10).</summary>
public sealed record SetupChecklistResponse(IReadOnlyList<SetupStep> Steps, int Done, int Total);

public sealed record SetupMarkRequest(bool Done);

/// <summary>The demo company (5.4, P-09, G-11): its token and name; it does not count toward the plan's companies.</summary>
public sealed record DemoCompanyResponse(string Id, string Name, bool Created);
