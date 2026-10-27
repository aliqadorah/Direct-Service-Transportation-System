using System.Diagnostics.CodeAnalysis;
using DirectService.Transportation.Web.Client.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// Shared behaviour of the labelled form fields (board <c>.field</c>): label linked to the control, required mark (visual + for screen
/// readers), help text or the field's error message next to the field (UX-16), optional "source" note (e.g. price from tariff), grid span,
/// <c>aria-invalid</c>/<c>aria-describedby</c>, and registration for the error summary. Works inside an <see cref="EditForm"/> or alone.
/// </summary>
public abstract class FieldBase<TValue> : InputBase<TValue>
{
    private FieldIdentifier? _registered;
    private EditContext? _registeredCtx;

    [Inject] protected Loc L { get; set; } = default!;

    [Parameter] public string? Label { get; set; }
    [Parameter] public bool Required { get; set; }
    [Parameter] public string? Help { get; set; }
    [Parameter] public string? Source { get; set; }
    [Parameter] public int Span { get; set; } = 1;
    [Parameter] public string? Id { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public string? Placeholder { get; set; }
    /// <summary>An error shown without an EditContext (e.g. a filter or a standalone field).</summary>
    [Parameter] public string? Error { get; set; }
    [Parameter] public string? FieldClass { get; set; }

    protected string ElementId { get; private set; } = "";

    /// <summary>The parse error of a field used outside a form (filters), where InputBase has no EditContext to keep it.</summary>
    protected string? LocalError { get; set; }

    protected string? ErrorText
    {
        get
        {
            if (Error is not null) return Error;
            if (EditContext is null || ValueExpression is null) return LocalError;
            return EditContext.GetValidationMessages(FieldIdentifier).FirstOrDefault();
        }
    }

    protected bool Invalid => ErrorText is not null;

    /// <summary>The typed text could not be read (e.g. not a number) — the value keeps its last good value, so a dialog must not send it.</summary>
    public bool HasInputError => LocalError is not null;

    protected virtual string? DescribedBy => Invalid ? ElementId + "-e" : Help is not null ? ElementId + "-h" : null;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (string.IsNullOrEmpty(ElementId)) ElementId = Id ?? Ids.Next("f");
        if (EditContext is not null && ValueExpression is not null && (!_registered.HasValue || !_registered.Value.Equals(FieldIdentifier) || !ReferenceEquals(_registeredCtx, EditContext)))
        {
            FieldRegistry.Register(EditContext, FieldIdentifier, ElementId);
            _registered = FieldIdentifier;
            _registeredCtx = EditContext;
        }
    }

    /// <summary>The whole field (label, the given control, messages) for components written in Razor markup.</summary>
    protected RenderFragment Frame(RenderFragment control) => b =>
    {
        OpenField(b);
        b.AddContent(16, control);
        CloseField(b);
    };

    /// <summary>Opens the field wrapper and writes the label; the caller writes the control and calls <see cref="CloseField"/>.</summary>
    protected void OpenField(RenderTreeBuilder b, bool labelFor = true)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", "field " + FieldClass);
        if (Span > 1) b.AddAttribute(2, "style", $"grid-column:span {Span}");
        if (Label is not null)
        {
            b.OpenElement(3, labelFor ? "label" : "span");
            if (labelFor) b.AddAttribute(4, "for", ElementId);
            else { b.AddAttribute(5, "class", "lbl"); b.AddAttribute(6, "id", ElementId + "-l"); }
            Bidi.Write(b, 7, Label);
            if (Required)
            {
                b.AddContent(11, " ");
                b.OpenElement(12, "span");
                b.AddAttribute(13, "class", "req");
                b.AddAttribute(14, "aria-hidden", "true");
                b.AddContent(15, "*");
                b.CloseElement();
            }
            b.CloseElement();
        }
    }

    protected void CloseField(RenderTreeBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        if (ErrorText is { } err)
        {
            b.OpenElement(20, "span");
            b.AddAttribute(21, "class", "help err");
            b.AddAttribute(22, "id", ElementId + "-e");
            b.OpenComponent<Icon>(23);
            b.AddComponentParameter(24, nameof(Icon.Name), "alert");
            b.CloseComponent();
            // One element for the text: its isolated numbers must not become separate flex items of the error line.
            b.OpenElement(25, "span");
            Bidi.Write(b, 26, err);
            b.CloseElement();
            b.CloseElement();
        }
        else if (Help is not null)
        {
            b.OpenElement(30, "span");
            b.AddAttribute(31, "class", "help");
            b.AddAttribute(32, "id", ElementId + "-h");
            Bidi.Write(b, 33, Help);
            b.CloseElement();
        }
        if (Source is not null)
        {
            b.OpenElement(40, "span");
            b.AddAttribute(41, "class", "src");
            b.OpenComponent<Icon>(42);
            b.AddComponentParameter(43, nameof(Icon.Name), "check");
            b.CloseComponent();
            Bidi.Write(b, 44, Source);
            b.CloseElement();
        }
        b.CloseElement();
    }

    /// <summary>Attributes common to every control: id, name, aria-invalid, aria-describedby, aria-required, readonly, disabled.</summary>
    protected void ControlAttributes(RenderTreeBuilder b, int seq)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.AddAttribute(seq, "id", ElementId);
        b.AddAttribute(seq + 1, "name", NameAttributeValue);   // server-rendered forms (account pages) post by name
        b.AddAttribute(seq + 2, "aria-invalid", Invalid ? "true" : null);
        b.AddAttribute(seq + 3, "aria-describedby", DescribedBy);
        b.AddAttribute(seq + 4, "aria-required", Required ? "true" : null);
        b.AddAttribute(seq + 5, "readonly", ReadOnly);
        b.AddAttribute(seq + 6, "disabled", Disabled);
        // "class" is already part of CssClass (InputBase merges it with the validation classes).
        b.AddMultipleAttributes(seq + 7, AdditionalAttributes?.Where(a => a.Key != "class"));
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
        => throw new NotSupportedException($"{GetType().Name} parses its own input.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && _registered.HasValue && _registeredCtx is not null) FieldRegistry.Unregister(_registeredCtx, _registered.Value);
        base.Dispose(disposing);
    }
}
