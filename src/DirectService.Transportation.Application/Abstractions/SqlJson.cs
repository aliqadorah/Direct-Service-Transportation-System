namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// SQL Server JSON functions used in queries on the custom values of trips, drivers and customers (report builder, FR-143). Mapped in the
/// database context; calling them outside a query is an error.
/// </summary>
public static class SqlJson
{
    /// <summary>JSON_VALUE(json, path) as text.</summary>
    public static string? Value(string? json, string path) => throw new NotSupportedException("Only for database queries.");

    /// <summary>TRY_CONVERT(decimal(18,4), JSON_VALUE(json, path)): the number, or null when absent or not a number.</summary>
    public static decimal? Number(string? json, string path) => throw new NotSupportedException("Only for database queries.");

    /// <summary>TRY_CONVERT(date, JSON_VALUE(json, path)): the date (stored yyyy-MM-dd), or null.</summary>
    public static DateOnly? Date(string? json, string path) => throw new NotSupportedException("Only for database queries.");
}
