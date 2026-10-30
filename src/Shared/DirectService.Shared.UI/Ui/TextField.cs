using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DirectService.Shared.UI.Ui;

/// <summary>
/// Single-line text field. <see cref="Type"/>: text, email, tel, password, search. Left-to-right values (email, phone, IBAN, plate, tax
/// number) set <see cref="Ltr"/> so they are typed and shown in LTR inside the Arabic form.
/// </summary>
public sealed class TextField : FieldBase<string?>
{
    [Parameter] public string Type { get; set; } = "text";
    [Parameter] public int? MaxLength { get; set; }
    [Parameter] public string? Autocomplete { get; set; }
    [Parameter] public bool Ltr { get; set; }
    [Parameter] public bool BindOnInput { get; set; }
    [Parameter] public string? Icon { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        if (Icon is not null)
        {
            builder.OpenElement(50, "div");
            builder.AddAttribute(51, "class", "adorn");
            builder.OpenElement(52, "span");
            builder.AddAttribute(53, "class", "start");
            builder.OpenComponent<Icon>(54);
            builder.AddComponentParameter(55, nameof(Ui.Icon.Name), Icon);
            builder.CloseComponent();
            builder.CloseElement();
        }
        builder.OpenElement(60, "input");
        builder.AddAttribute(61, "type", Type);
        builder.AddAttribute(62, "class", "input " + CssClass);
        builder.AddAttribute(63, "maxlength", MaxLength);
        builder.AddAttribute(64, "autocomplete", Autocomplete);
        builder.AddAttribute(65, "placeholder", Placeholder);
        builder.AddAttribute(66, "dir", Ltr ? "ltr" : null);
        builder.AddAttribute(67, "value", CurrentValueAsString);
        builder.AddAttribute(68, BindOnInput ? "oninput" : "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        builder.CloseElement();
        if (Icon is not null) builder.CloseElement();
        CloseField(builder);
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = string.IsNullOrEmpty(value) ? null : value;
        validationErrorMessage = null;
        return true;
    }
}

/// <summary>Multi-line text (notes, reasons, addresses).</summary>
public sealed class TextAreaField : FieldBase<string?>
{
    [Parameter] public int? MaxLength { get; set; }
    [Parameter] public int Rows { get; set; } = 3;
    [Parameter] public bool BindOnInput { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        OpenField(builder);
        builder.OpenElement(60, "textarea");
        builder.AddAttribute(61, "class", "input " + CssClass);
        builder.AddAttribute(62, "rows", Rows);
        builder.AddAttribute(63, "maxlength", MaxLength);
        builder.AddAttribute(64, "placeholder", Placeholder);
        builder.AddAttribute(65, "value", CurrentValueAsString);
        builder.AddAttribute(66, BindOnInput ? "oninput" : "onchange", EventCallback.Factory.CreateBinder<string?>(this, v => CurrentValueAsString = v, CurrentValueAsString));
        builder.SetUpdatesAttributeName("value");
        ControlAttributes(builder, 70);
        builder.CloseElement();
        CloseField(builder);
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = string.IsNullOrEmpty(value) ? null : value;
        validationErrorMessage = null;
        return true;
    }
}
