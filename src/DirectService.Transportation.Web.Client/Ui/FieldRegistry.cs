using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Forms;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// Which input element shows which model field, per form: lets the error summary link each message to its field (UX-16) and lets the
/// API's field errors (problem details <c>errors[].field</c>, camelCase) find the model field by name.
/// </summary>
public static class FieldRegistry
{
    private static readonly ConditionalWeakTable<EditContext, List<(FieldIdentifier Field, string ElementId)>> Table = new();

    public static void Register(EditContext ctx, FieldIdentifier field, string elementId)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var list = Table.GetOrCreateValue(ctx);
        lock (list)
        {
            list.RemoveAll(x => x.Field.Equals(field));
            list.Add((field, elementId));
        }
    }

    public static void Unregister(EditContext ctx, FieldIdentifier field)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!Table.TryGetValue(ctx, out var list)) return;
        lock (list) list.RemoveAll(x => x.Field.Equals(field));
    }

    public static IReadOnlyList<(FieldIdentifier Field, string ElementId)> For(EditContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (!Table.TryGetValue(ctx, out var list)) return [];
        lock (list) return [.. list];
    }

    /// <summary>
    /// The registered field for an API field name (camelCase path such as <c>lines[2].amount</c> or <c>address.city</c>): the last segment
    /// names the property; for collections the model of the n-th registered field with that name is taken.
    /// </summary>
    public static FieldIdentifier? Find(EditContext ctx, string apiField)
    {
        ArgumentNullException.ThrowIfNull(apiField);
        var segments = apiField.Split('.');
        var name = segments[^1].Split('[')[0];
        int? index = null;
        foreach (var seg in segments)
        {
            var b = seg.IndexOf('[', StringComparison.Ordinal);
            if (b >= 0 && int.TryParse(seg[(b + 1)..].TrimEnd(']'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) index = n;
        }
        var matches = For(ctx).Where(x => string.Equals(x.Field.FieldName, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Field).ToList();
        if (matches.Count == 0) return null;
        if (index is int i) return i < matches.Count ? matches[i] : null;
        return matches[0];
    }
}
