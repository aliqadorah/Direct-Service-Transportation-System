namespace DirectService.Shared.Contracts;

// Column views (FR-183): the shared engine in Shared.Infrastructure (Stage 2 session 6, DT-311).

/// <summary>One visible column of a list, in order, with an optional width in pixels.</summary>
public sealed record ColumnSetting(string? Key, int? Width);

public sealed record ColumnViewRequest(IReadOnlyList<ColumnSetting>? Columns);

/// <summary>The columns a user sees: personal view, else the company default, else none (the app's built-in default).</summary>
public sealed record ColumnViewResponse(string ViewKey, string Source, IReadOnlyList<ColumnSetting> Columns, IReadOnlyList<ColumnSetting>? CompanyDefault);
