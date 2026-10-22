using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>Explicit column mapping of an import (field key → header in the file), shared by the master-data and pricing imports.</summary>
public static class ImportMapping
{
    /// <summary>Parses the mapping JSON; empty → no mapping; malformed or oversized (over 200 entries) → 400 on "mapping".</summary>
    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            if (map.Count > 200 || map.Any(m => m.Key.Length > 60 || m.Value is null || m.Value.Length > 200)) throw new JsonException();
            return new Dictionary<string, string>(map, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            throw Fail.Field("mapping", ErrorCodes.ValidationFormat, new FieldRef("mapping"));
        }
    }
}
