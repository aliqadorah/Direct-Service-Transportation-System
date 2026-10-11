using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// Amount / weight / rate field: typed in LTR with tabular figures, Western or Arabic-Indic digits accepted, shown with thousands
/// separators and <see cref="Decimals"/> decimals once the field loses focus. Optional unit after the value (e.g. "طن", "JOD").
/// More decimals than allowed are refused here as on the server (the API checks again).
/// </summary>
public sealed class NumberField : FieldBase<decimal?>
{
    [Parameter] public int Decimals { get; set; } = 3;
    [Parameter] public decimal? Min { get; set; }
    [Parameter] public decimal? Max { get; set; }
    [Parameter] public string? Unit { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        if (Unit is not null)
        {
            builder.OpenElement(50, "div");
            builder.AddAttribute(51, "class", "adorn has-unit");
            builder.OpenElement(52, "span");
            builder.AddAttribute(53, "class", "unit");
            builder.AddAttribute(54, "id", ElementId + "-u");
            Bidi.Write(builder, 55, Unit);
            builder.CloseElement();
        }
        builder.OpenElement(60, "input");
        builder.AddAttribute(61, "type", "text");
        builder.AddAttribute(62, "inputmode", "decimal");
        builder.AddAttribute(63, "class", "input num " + CssClass);
        builder.AddAttribute(64, "autocomplete", "off");
        builder.AddAttribute(65, "placeholder", Placeholder);
        builder.AddAttribute(66, "value", CurrentValueAsString);
        builder.AddAttribute(67, "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        builder.CloseElement();
        if (Unit is not null) builder.CloseElement();
        CloseField(builder);
    }

    /// <summary>The unit is read with the field (e.g. "طن").</summary>
    protected override string? DescribedBy => Unit is null ? base.DescribedBy : string.Join(' ', new[] { ElementId + "-u", base.DescribedBy }.Where(x => x is not null));

    protected override string? FormatValueAsString(decimal? value) => value is null ? null : Fmt.Money(value, Decimals);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out decimal? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        validationErrorMessage = LocalError = null;
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Fmt.TryParseDecimal(value, out var d))
        {
            validationErrorMessage = LocalError = L.F("ui.notANumber", Label ?? "");
            return false;
        }
        if (decimal.Round(d, Decimals) != d)
        {
            validationErrorMessage = LocalError = L.F("ui.tooManyDecimals", Label ?? "", Decimals);
            return false;
        }
        if ((Min is decimal min && d < min) || (Max is decimal max && d > max))
        {
            validationErrorMessage = LocalError = L.F("ui.outOfRange", Label ?? "", Fmt.Money(Min, Decimals), Fmt.Money(Max, Decimals));
            return false;
        }
        result = d;
        return true;
    }
}

/// <summary>Whole numbers (days, counts, percentages without decimals).</summary>
public sealed class IntField : FieldBase<int?>
{
    [Parameter] public int? Min { get; set; }
    [Parameter] public int? Max { get; set; }
    [Parameter] public string? Unit { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        builder.OpenElement(60, "input");
        builder.AddAttribute(61, "type", "text");
        builder.AddAttribute(62, "inputmode", "numeric");
        builder.AddAttribute(63, "class", "input num " + CssClass);
        builder.AddAttribute(64, "autocomplete", "off");
        builder.AddAttribute(65, "value", CurrentValueAsString);
        builder.AddAttribute(66, "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        builder.CloseElement();
        CloseField(builder);
    }

    protected override string? FormatValueAsString(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out int? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        validationErrorMessage = LocalError = null;
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!int.TryParse(Fmt.Western(value).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            validationErrorMessage = LocalError = L.F("ui.notANumber", Label ?? "");
            return false;
        }
        if ((Min is int min && n < min) || (Max is int max && n > max))
        {
            validationErrorMessage = LocalError = L.F("ui.outOfRange", Label ?? "", Min?.ToString(CultureInfo.InvariantCulture) ?? "", Max?.ToString(CultureInfo.InvariantCulture) ?? "");
            return false;
        }
        result = n;
        return true;
    }
}

/// <summary>
/// Date field typed in the user's date format (default YYYY-MM-DD, DT-65 / FR-178), with Western or Arabic-Indic digits, and a calendar
/// button that opens the browser's date picker. The value is a <see cref="DateOnly"/>; dates are sent to the API as ISO dates.
/// </summary>
public sealed class DateField : FieldBase<DateOnly?>
{
    private ElementReference _native;

    [Inject] private Services.ShellState Shell { get; set; } = default!;
    [Inject] private Services.UiJs Js { get; set; } = default!;
    [Parameter] public DateOnly? Min { get; set; }
    [Parameter] public DateOnly? Max { get; set; }

    private string Format => Fmt.Safe(Shell.DateFormat);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        builder.OpenElement(50, "div");
        builder.AddAttribute(51, "class", ReadOnly || Disabled ? "adorn" : "adorn has-pick");
        builder.OpenElement(60, "input");
        builder.AddAttribute(61, "type", "text");
        builder.AddAttribute(62, "class", "input num " + CssClass);
        builder.AddAttribute(63, "autocomplete", "off");
        builder.AddAttribute(64, "placeholder", Placeholder ?? Format.ToUpperInvariant());
        builder.AddAttribute(65, "value", CurrentValueAsString);
        builder.AddAttribute(66, "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        builder.CloseElement();
        if (!ReadOnly && !Disabled)
        {
            builder.OpenElement(80, "button");
            builder.AddAttribute(81, "type", "button");
            builder.AddAttribute(82, "class", "pick");
            builder.AddAttribute(83, "aria-label", L["ui.openCalendar"]);
            builder.AddAttribute(84, "onclick", EventCallback.Factory.Create(this, async () => await Js.ShowPicker(_native)));
            builder.OpenComponent<Icon>(85);
            builder.AddComponentParameter(86, nameof(Icon.Name), "calendar");
            builder.CloseComponent();
            builder.CloseElement();
            builder.OpenElement(90, "input");
            builder.AddAttribute(91, "type", "date");
            builder.AddAttribute(92, "class", "native");
            builder.AddAttribute(93, "tabindex", "-1");
            builder.AddAttribute(94, "aria-hidden", "true");
            builder.AddAttribute(95, "min", Min?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AddAttribute(96, "max", Max?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AddAttribute(97, "value", CurrentValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.AddAttribute(98, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            {
                if (DateOnly.TryParseExact(e.Value?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) CurrentValue = d;
            }));
            builder.AddElementReferenceCapture(99, r => _native = r);
            builder.CloseElement();
        }
        builder.CloseElement();
        CloseField(builder);
    }

    protected override string? FormatValueAsString(DateOnly? value) => value?.ToString(Format, CultureInfo.InvariantCulture);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out DateOnly? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        validationErrorMessage = LocalError = null;
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var text = Fmt.Western(value).Trim();
        if (!DateOnly.TryParseExact(text, [Format, "yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            validationErrorMessage = LocalError = L.F("ui.notADate", Label ?? "");
            return false;
        }
        if ((Min is DateOnly min && d < min) || (Max is DateOnly max && d > max))
        {
            validationErrorMessage = LocalError = L.F("ui.outOfRange", Label ?? "", Fmt.Date(Min, Format), Fmt.Date(Max, Format));
            return false;
        }
        result = d;
        return true;
    }
}
