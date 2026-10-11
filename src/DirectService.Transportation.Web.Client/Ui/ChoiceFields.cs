using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>Native select for short fixed lists (payment method, status, layout). Values map to options by position.</summary>
public sealed class SelectField<TValue> : FieldBase<TValue>
{
    [Parameter, EditorRequired] public IReadOnlyList<Option<TValue>> Options { get; set; } = [];
    /// <summary>Text of the empty first option; omit for lists that always have a value.</summary>
    [Parameter] public string? Empty { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        builder.OpenElement(60, "select");
        builder.AddAttribute(61, "class", "input " + CssClass);
        builder.AddAttribute(62, "value", CurrentValueAsString);
        builder.AddAttribute(63, "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        if (Empty is not null)
        {
            builder.OpenElement(80, "option");
            builder.AddAttribute(81, "value", "");
            builder.AddContent(82, Empty);
            builder.CloseElement();
        }
        for (var i = 0; i < Options.Count; i++)
        {
            builder.OpenElement(90, "option");
            builder.AddAttribute(91, "value", i.ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(92, "disabled", Options[i].Disabled);
            builder.AddContent(93, Options[i].Text);
            builder.CloseElement();
        }
        builder.CloseElement();
        CloseField(builder);
    }

    protected override string? FormatValueAsString(TValue? value)
    {
        for (var i = 0; i < Options.Count; i++)
            if (EqualityComparer<TValue>.Default.Equals(Options[i].Value, value)) return i.ToString(CultureInfo.InvariantCulture);
        return "";
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        validationErrorMessage = null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < Options.Count)
        {
            result = Options[i].Value;
            return true;
        }
        result = default!;
        return true;
    }
}

/// <summary>Checkbox with its label to the start (board <c>.chk</c>).</summary>
public sealed class CheckField : FieldBase<bool>
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", "chk " + FieldClass);
        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "type", "checkbox");
        builder.AddAttribute(4, "checked", CurrentValue);
        builder.AddAttribute(5, "onchange", EventCallback.Factory.CreateBinder<bool>(this, v => CurrentValue = v, CurrentValue));
        builder.SetUpdatesAttributeName("checked");
        ControlAttributes(builder, 10);
        builder.CloseElement();
        Bidi.Write(builder, 20, Label);
        builder.CloseElement();
        if (Help is not null)
        {
            builder.OpenElement(30, "div");
            builder.AddAttribute(31, "class", "help");
            builder.AddAttribute(32, "id", ElementId + "-h");
            Bidi.Write(builder, 33, Help);
            builder.CloseElement();
        }
    }
}

/// <summary>
/// On/off setting (board <c>.toggle</c>): a switch button (role=switch, Space/Enter toggle) with its label and help text, as on the
/// company settings screen. The state is also given in text for screen readers.
/// </summary>
public sealed class ToggleField : FieldBase<bool>
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "switch " + FieldClass);
        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "type", "button");
        builder.AddAttribute(4, "class", "toggle");
        builder.AddAttribute(5, "role", "switch");
        builder.AddAttribute(6, "aria-checked", CurrentValue ? "true" : "false");
        builder.AddAttribute(7, "aria-labelledby", ElementId + "-l");
        builder.AddAttribute(8, "aria-describedby", Help is null ? null : ElementId + "-h");
        builder.AddAttribute(9, "id", ElementId);
        builder.AddAttribute(10, "disabled", Disabled || ReadOnly);
        builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, () => CurrentValue = !CurrentValue));
        builder.AddMultipleAttributes(12, AdditionalAttributes);
        builder.CloseElement();
        builder.OpenElement(20, "div");
        builder.OpenElement(21, "div");
        builder.AddAttribute(22, "id", ElementId + "-l");
        Bidi.Write(builder, 23, Label);
        builder.CloseElement();
        if (Help is not null)
        {
            builder.OpenElement(30, "div");
            builder.AddAttribute(31, "class", "help");
            builder.AddAttribute(32, "id", ElementId + "-h");
            Bidi.Write(builder, 33, Help);
            builder.CloseElement();
        }
        builder.CloseElement();
        builder.CloseElement();
    }
}
