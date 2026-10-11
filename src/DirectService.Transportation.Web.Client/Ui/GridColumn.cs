using Microsoft.AspNetCore.Components;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// A column of <see cref="DataGrid{TItem}"/>: header text, cell value (text with isolated runs) or a template, numeric alignment,
/// server sort key, filter-row key, footer total. Columns render nothing themselves; they register with the grid.
/// </summary>
public sealed class GridColumn<TItem> : ComponentBase, IDisposable
{
    [CascadingParameter] private DataGrid<TItem>? Grid { get; set; }

    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>Key of the column in saved column views (FR-183); columns without a key are always shown.</summary>
    [Parameter] public string? ColumnKey { get; set; }
    [Parameter] public Func<TItem, string?>? Value { get; set; }
    [Parameter] public RenderFragment<TItem>? Template { get; set; }
    /// <summary>Numbers and amounts: aligned to the end, tabular figures, isolated LTR.</summary>
    [Parameter] public bool Numeric { get; set; }
    /// <summary>Codes, plates, dates: isolated LTR, start-aligned.</summary>
    [Parameter] public bool Ltr { get; set; }
    [Parameter] public string? Width { get; set; }
    [Parameter] public string? SortKey { get; set; }
    [Parameter] public string? FilterKey { get; set; }
    [Parameter] public string? FilterPlaceholder { get; set; }
    [Parameter] public string? Footer { get; set; }
    [Parameter] public bool Wrap { get; set; }
    [Parameter] public bool Clip { get; set; }
    [Parameter] public bool Visible { get; set; } = true;
    /// <summary>A visually hidden header (e.g. a row-actions column) keeps the table accessible.</summary>
    [Parameter] public bool HideTitle { get; set; }

    private string? _signature;

    protected override void OnInitialized() => Grid?.AddColumn(this);

    /// <summary>A changed title, footer total or visibility re-renders the grid (header and footer are drawn from the columns).</summary>
    protected override void OnParametersSet()
    {
        var sig = string.Join('\u001F', Title, Footer, Visible, Numeric, Ltr, Width, SortKey, FilterKey, Wrap, Clip, HideTitle);
        if (_signature is not null && sig != _signature) Grid?.ColumnChanged();
        _signature = sig;
    }

    public void Dispose() => Grid?.RemoveColumn(this);
}
