using System.Globalization;
using System.Text.RegularExpressions;
using DirectService.Shared.UI.Localization;
using DirectService.Shared.UI.Services;

namespace DirectService.Shared.UI.Pages.Reports;

/// <summary>
/// A refused report definition as messages on the builder's settings (owner feedback 13 Oct 2026, SCR-52). The server's report.* messages
/// already name the setting and say what to change; any other field error (a generic "field X is invalid") is replaced here by the setting's
/// name, so an internal path such as "definition.rows" never reaches the screen. Each error is placed on its setting for marking.
/// </summary>
public static partial class ReportProblems
{
    /// <summary>A setting of the side panel: row (grouping n), column, measure, filter, columns, top, source, from, to, name.</summary>
    public readonly record struct Place(string Kind, int Index);

    public static Place? PlaceOf(string? field)
    {
        if (string.IsNullOrEmpty(field)) return null;
        var m = DefinitionPath().Match(field);
        if (m.Success)
        {
            var index = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return new Place(m.Groups[1].Value switch
            {
                "rows" => "row",
                "column" => "column",
                "measures" => "measure",
                "filters" => "filter",
                "columns" => "columns",
                _ => "top",
            }, index);
        }
        return field is "source" or "from" or "to" or "name" ? new Place(field, 0) : null;
    }

    /// <summary>The message to show for one field error, never an internal name.</summary>
    public static string Message(ApiFieldError e, Loc L)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(L);
        if (e.Code?.StartsWith("report.", StringComparison.Ordinal) == true && !string.IsNullOrWhiteSpace(e.Message)) return e.Message;
        var place = PlaceOf(e.Field);
        if (place is { Kind: "from" or "to" } && e.Code == "validation.range") return L["rb.periodOrder"];
        return place is { } p && Name(p, L) is { } name ? L.F("rb.checkSetting", name) : L["rb.checkSettings"];
    }

    /// <summary>The setting's name as the side panel shows it.</summary>
    public static string? Name(Place p, Loc L) => p.Kind switch
    {
        "row" => L.F("rb.groupN", p.Index + 1),
        "column" => L["rb.columnGroup"],
        "measure" => L.F("rb.measureN", p.Index + 1),
        "filter" => L.F("rb.filterN", p.Index + 1),
        "columns" => L["rb.columnsSetting"],
        "top" => L["rb.top"],
        "source" => L["rb.source"],
        "from" or "to" => L["rb.period"],
        "name" => L["md.name"],
        _ => null,
    };

    [GeneratedRegex(@"^definition\.(rows|columns|column|measures|filters|top)(?:\[(\d+)\])?", RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionPath();
}
