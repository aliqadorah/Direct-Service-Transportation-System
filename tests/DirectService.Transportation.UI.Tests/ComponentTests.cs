using AngleSharp.Dom;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Behaviour and accessibility contract of the design-system components (bUnit).</summary>
public sealed class ComponentTests : UiTestBase
{
    [Fact]
    public void Buttons_render_variants_and_icon_only_buttons_carry_a_label()
    {
        var b = Render<Btn>(p => p.Add(x => x.Text, "حفظ وترحيل").Add(x => x.Primary, true).Add(x => x.Shortcut, "Ctrl S"));
        Assert.Contains("pri", b.Find("button").ClassList);
        Assert.Equal("Ctrl S", b.Find("kbd").TextContent);

        var icon = Render<Btn>(p => p.Add(x => x.Icon, "more").Add(x => x.AriaLabel, "المزيد"));
        Assert.Equal("المزيد", icon.Find("button").GetAttribute("aria-label"));
        Assert.Contains("icon-only", icon.Find("button").ClassList);

        var busy = Render<Btn>(p => p.Add(x => x.Text, "حفظ").Add(x => x.Busy, true));
        Assert.True(busy.Find("button").HasAttribute("disabled"));
        Assert.Equal("true", busy.Find("button").GetAttribute("aria-busy"));
    }

    [Fact]
    public void Status_tags_show_icon_and_text_never_colour_alone()
    {
        var tag = Render<StatusTag>(p => p.Add(x => x.Status, "breach"));
        Assert.Contains("b-err", tag.Find(".badge").ClassList);
        Assert.NotNull(tag.Find("svg.ic"));
        Assert.Equal("مخالفة تعرفة", tag.Find(".badge").TextContent.Trim());
        L.SetLanguage("en");
        tag.Render();
        Assert.Equal("Tariff breach", tag.Find(".badge").TextContent.Trim());
    }

    [Fact]
    public void Codes_inside_Arabic_text_are_rendered_as_isolated_runs()
    {
        var t = Render<Tx>(p => p.Add(x => x.Value, "أضيفت إلى الفاتورة INV-02211 بتاريخ 2026-10-09"));
        Assert.Equal(["INV-02211", "2026-10-09"], t.FindAll("bdi.num").Select(e => e.TextContent));
    }

    private sealed record Row(string No, string Name, decimal Amount, bool IsVoid);

    private static readonly IReadOnlyList<Row> Rows = [new("TRP-1", "أحمد", 10m, false), new("TRP-2", "سامي", 5.5m, true)];

    private static RenderFragment Columns => b =>
    {
        b.OpenComponent<GridColumn<Row>>(0);
        b.AddComponentParameter(1, nameof(GridColumn<Row>.Title), "رقم السند");
        b.AddComponentParameter(2, nameof(GridColumn<Row>.Value), (Func<Row, string?>)(r => r.No));
        b.AddComponentParameter(3, nameof(GridColumn<Row>.Ltr), true);
        b.AddComponentParameter(4, nameof(GridColumn<Row>.SortKey), "no");
        b.AddComponentParameter(5, nameof(GridColumn<Row>.FilterKey), "no");
        b.CloseComponent();
        b.OpenComponent<GridColumn<Row>>(10);
        b.AddComponentParameter(11, nameof(GridColumn<Row>.Title), "المبلغ");
        b.AddComponentParameter(12, nameof(GridColumn<Row>.Value), (Func<Row, string?>)(r => Fmt.Money(r.Amount)));
        b.AddComponentParameter(13, nameof(GridColumn<Row>.Numeric), true);
        b.AddComponentParameter(14, nameof(GridColumn<Row>.Footer), "15.500");
        b.CloseComponent();
    };

    [Fact]
    public void Data_grid_sorts_filters_selects_and_totals()
    {
        (string Key, bool Desc)? sorted = null;
        IReadOnlyDictionary<string, string>? filtered = null;
        IReadOnlySet<object>? selected = null;
        Row? opened = null;
        var g = Render<DataGrid<Row>>(p => p
            .Add(x => x.Items, Rows).Add(x => x.Key, r => r.No).Add(x => x.Columns, Columns).Add(x => x.Caption, "Trips")
            .Add(x => x.SortKey, "no").Add(x => x.SortDesc, false).Add(x => x.OnSort, s => sorted = s)
            .Add(x => x.ShowFilters, true).Add(x => x.OnFilter, f => filtered = f)
            .Add(x => x.Selectable, true).Add(x => x.SelectedChanged, s => selected = s)
            .Add(x => x.OnRowClick, r => opened = r)
            .Add(x => x.RowClass, r => r.IsVoid ? "void" : null));

        Assert.Equal("ascending", g.Find("th[aria-sort]").GetAttribute("aria-sort"));
        Act(() => g.Find("th button.sort").Click());
        Assert.Equal(("no", true), sorted);

        Act(() => g.Find("tr.flt input").Change("TRP-2"));
        Assert.Equal("TRP-2", filtered!["no"]);

        var rows = g.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("void", rows[1].ClassList);
        Assert.Equal("15.500", g.Find("tfoot td.n bdi").TextContent);
        Assert.Contains("n", rows[0].QuerySelectorAll("td")[2].ClassList);

        Act(() => g.Find("thead input[type=checkbox]").Change(true));
        Assert.Equal(2, selected!.Count);

        Act(() => g.FindAll("tbody tr")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        Assert.Equal("TRP-1", opened!.No);
        Assert.Equal("0", g.FindAll("tbody tr")[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void Pager_shows_the_range_and_pages_with_gaps()
    {
        int? page = null;
        var p = Render<Pager>(ps => ps.Add(x => x.Page, 1).Add(x => x.PageSize, 50).Add(x => x.Total, 1284).Add(x => x.OnPage, n => page = n));
        Assert.Contains("1,284", p.Find(".tbl-foot > span").TextContent);
        Assert.Equal(["1", "2", "26"], p.FindAll("button.chip").Select(b => b.TextContent.Trim()));
        Assert.True(p.FindAll(".pg .btn")[0].HasAttribute("disabled"));
        Assert.Equal("page", p.Find("button.chip[aria-current]").GetAttribute("aria-current"));
        Act(() => p.FindAll("button.chip")[2].Click());
        Assert.Equal(26, page);
    }

    private sealed class Model
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "أدخل الوزن")] public decimal? Weight { get; set; }
        public DateOnly? Date { get; set; }
        public string? Driver { get; set; }
        public bool Approval { get; set; }
    }

    [Fact]
    public void Number_field_accepts_Arabic_digits_and_refuses_extra_decimals_next_to_the_field()
    {
        var model = new Model();
        var cut = Render<EditForm>(p => p.Add(x => x.Model, model).Add(x => x.ChildContent, (EditContext _) => b =>
        {
            b.OpenComponent<NumberField>(0);
            b.AddComponentParameter(1, "Value", model.Weight);
            b.AddComponentParameter(2, "ValueChanged", EventCallback.Factory.Create<decimal?>(this, v => model.Weight = v));
            b.AddComponentParameter(3, "ValueExpression", (System.Linq.Expressions.Expression<Func<decimal?>>)(() => model.Weight));
            b.AddComponentParameter(4, "Label", "الوزن المحمّل");
            b.AddComponentParameter(5, "Unit", "طن");
            b.AddComponentParameter(6, "Required", true);
            b.CloseComponent();
        }));
        AngleSharp.Dom.IElement input() => cut.Find("input");
        Assert.Equal("decimal", input().GetAttribute("inputmode"));
        Assert.Equal(input().Id, cut.Find("label").GetAttribute("for"));
        Assert.Contains("-u", input().GetAttribute("aria-describedby"));

        Act(() => input().Change("٣١٫٨٥"));
        Assert.Equal(31.85m, model.Weight);

        Act(() => cut.Find("input").Change("1.23456"));
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Contains("3", cut.Find(".help.err").TextContent);
    }

    [Fact]
    public void Date_field_uses_the_users_format()
    {
        Shell.SetMe(new("u", "n", "e", "ar", "dd/MM/yyyy", null, false, null, null, []));
        DateOnly? value = new DateOnly(2026, 10, 10);
        var cut = Render<DateField>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, v => value = v)
            .Add(x => x.ValueExpression, () => value).Add(x => x.Label, "التاريخ"));
        Assert.Equal("10/10/2026", cut.Find("input.input").GetAttribute("value"));
        Act(() => cut.Find("input.input").Change("٠١/١١/٢٠٢٦"));
        Assert.Equal(new DateOnly(2026, 11, 1), value);
        Assert.Equal("فتح التقويم", cut.Find("button.pick").GetAttribute("aria-label"));
    }

    [Fact]
    public void Combo_filters_with_Arabic_folding_and_selects_with_the_keyboard()
    {
        string? value = null;
        IReadOnlyList<Option<string?>> options = [new("d1", "أحمد صالح", "10-11111"), new("d2", "سامي العبادي", "10-22222")];
        var cut = Render<ComboField<string?>>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, v => value = v)
            .Add(x => x.ValueExpression, () => value).Add(x => x.Options, options).Add(x => x.Label, "السائق"));
        AngleSharp.Dom.IElement input() => cut.Find("input");
        Assert.Equal("combobox", input().GetAttribute("role"));
        Assert.Equal("false", input().GetAttribute("aria-expanded"));

        Act(() => input().Input("احمد"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=option]")));
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-expanded"));
        Assert.Contains("10-11111", cut.Find("[role=option] small").TextContent);

        Act(() => cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        Assert.Equal("d1", value);
        Assert.Empty(cut.FindAll("[role=listbox]"));

        Act(() => cut.Find("input").Input("10-222"));
        cut.WaitForAssertion(() => Assert.Equal("سامي العبادي", cut.Find("[role=option] span").TextContent));
    }

    /// <summary>
    /// Owner feedback 13 Oct 2026 (SCR-32): an open list is placed as an overlay fixed to the window (never clipped by the journal lines' scroll
    /// box), placed again while it changes, and released when it closes.
    /// </summary>
    [Fact]
    public void Combo_list_floats_above_scrolling_containers_while_open()
    {
        string? value = null;
        IReadOnlyList<Option<string?>> options = [new("a1", "1101 · الصندوق"), new("a2", "110201 · البنك")];
        var cut = Render<ComboField<string?>>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, v => value = v)
            .Add(x => x.ValueExpression, () => value).Add(x => x.Options, options).Add(x => x.Label, "الحساب"));
        Assert.DoesNotContain(UiModule.Invocations, i => i.Identifier == "floatList");
        Act(() => cut.Find("input").Click());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[role=option]").Count));
        Assert.Contains(UiModule.Invocations, i => i.Identifier == "floatList");
        Act(() => cut.Find("input").Input("البنك"));
        var placed = UiModule.Invocations.Count(i => i.Identifier == "floatList");
        Assert.True(placed >= 2, "placed again when the list changes");
        Act(() => cut.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        Assert.Equal("a2", value);
        Assert.Contains(UiModule.Invocations, i => i.Identifier == "unfloatList");
    }

    [Fact]
    public void Error_summary_lists_field_errors_with_links_to_the_fields()
    {
        var model = new Model();
        var cut = Render<EditForm>(p => p.Add(x => x.Model, model).Add(x => x.ChildContent, (EditContext _) => b =>
        {
            b.OpenComponent<DataAnnotationsValidator>(0);
            b.CloseComponent();
            b.OpenComponent<ErrorSummary>(1);
            b.CloseComponent();
            b.OpenComponent<NumberField>(2);
            b.AddComponentParameter(3, "Value", model.Weight);
            b.AddComponentParameter(4, "ValueChanged", EventCallback.Factory.Create<decimal?>(this, v => model.Weight = v));
            b.AddComponentParameter(5, "ValueExpression", (System.Linq.Expressions.Expression<Func<decimal?>>)(() => model.Weight));
            b.AddComponentParameter(6, "Label", "الوزن");
            b.AddComponentParameter(7, "Id", "weight");
            b.CloseComponent();
        }));
        Act(() => cut.Find("form").Submit());
        var summary = cut.Find(".errsum");
        Assert.Equal("alert", summary.GetAttribute("role"));
        Assert.Equal("#weight", summary.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("أدخل الوزن", cut.Find(".help.err").TextContent.Trim());
    }

    [Fact]
    public void Api_field_errors_are_shown_next_to_their_fields()
    {
        var model = new Model();
        ProblemValidator? validator = null;
        var cut = Render<EditForm>(p => p.Add(x => x.Model, model).Add(x => x.ChildContent, (EditContext _) => b =>
        {
            b.OpenComponent<ProblemValidator>(0);
            b.AddComponentReferenceCapture(1, r => validator = (ProblemValidator)r);
            b.CloseComponent();
            b.OpenComponent<NumberField>(2);
            b.AddComponentParameter(3, "Value", model.Weight);
            b.AddComponentParameter(4, "ValueChanged", EventCallback.Factory.Create<decimal?>(this, v => model.Weight = v));
            b.AddComponentParameter(5, "ValueExpression", (System.Linq.Expressions.Expression<Func<decimal?>>)(() => model.Weight));
            b.AddComponentParameter(6, "Label", "الوزن");
            b.CloseComponent();
        }));
        string? unplaced = null;
        cut.InvokeAsync(() => unplaced = validator!.Show(new ApiProblem
        {
            Status = 422, Code = "period.closed",
            Errors = [new("weight", "validation.range", "الوزن يجب أن يكون أكبر من 0."), new("date", "period.closed", "التاريخ في فترة مغلقة.")],
        }));
        Assert.Equal("الوزن يجب أن يكون أكبر من 0.", cut.Find(".help.err").TextContent.Trim());
        Assert.Equal("التاريخ في فترة مغلقة.", unplaced);  // no date field on this form → page-level message

        // A new submit clears the server's messages, so the form can be sent again (review M2).
        model.Weight = 3m;
        Act(() => cut.Find("form").Submit());
        Assert.Empty(cut.FindAll(".help.err"));
    }

    [Fact]
    public void Pager_never_shows_a_range_past_the_total()
    {
        var p = Render<Pager>(ps => ps.Add(x => x.Page, 3).Add(x => x.PageSize, 50).Add(x => x.Total, 20));
        Assert.Contains("1–20", p.Find(".tbl-foot > span").TextContent);
        Assert.False(p.Find("button.chip").HasAttribute("aria-pressed"));
    }

    [Fact]
    public void Number_filter_outside_a_form_reports_text_it_cannot_read()
    {
        decimal? v = null;
        var cut = Render<NumberField>(p => p.Add(x => x.Value, v).Add(x => x.ValueChanged, n => v = n).Add(x => x.ValueExpression, () => v).Add(x => x.Label, "المبلغ"));
        Act(() => cut.Find("input").Change("1,5"));
        Assert.Null(v);
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Toggle_is_a_switch_and_changes_with_click()
    {
        var on = false;
        var cut = Render<ToggleField>(p => p.Add(x => x.Value, on).Add(x => x.ValueChanged, v => on = v).Add(x => x.ValueExpression, () => on)
            .Add(x => x.Label, "اعتماد الفواتير").Add(x => x.Help, "مفعّل: «حفظ» ينشئ مسودة."));
        AngleSharp.Dom.IElement sw() => cut.Find("button[role=switch]");
        Assert.Equal("false", sw().GetAttribute("aria-checked"));
        Assert.Equal(sw().Id + "-l", sw().GetAttribute("aria-labelledby"));
        Act(() => sw().Click());
        Assert.True(on);
    }

    [Fact]
    public void Tabs_move_with_arrow_keys_in_reading_order()
    {
        var selected = "a";
        IReadOnlyList<TabItem> items = [new("a", "الكل"), new("b", "غير مفوترة", 6), new("c", "رحلاتي")];
        var cut = Render<TabStrip>(p => p.Add(x => x.Items, items).Add(x => x.Selected, selected).Add(x => x.SelectedChanged, v => selected = v));
        Assert.Equal("true", cut.FindAll("[role=tab]")[0].GetAttribute("aria-selected"));
        Assert.Equal("-1", cut.FindAll("[role=tab]")[1].GetAttribute("tabindex"));
        Act(() => cut.Find("[role=tab][aria-selected=true]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" }));   // RTL: left = next
        Assert.Equal("b", selected);
        L.SetLanguage("en");
        cut.Render(p => p.Add(x => x.Selected, "b"));
        Act(() => cut.Find("[role=tab][aria-selected=true]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" }));  // LTR: right = next
        Assert.Equal("c", selected);
    }

    [Fact]
    public void Confirm_dialog_waits_for_a_reason_and_the_typed_word()
    {
        string? reason = null;
        var cut = Render<ConfirmDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "إلغاء الفاتورة").Add(x => x.Danger, true)
            .Add(x => x.AskReason, true).Add(x => x.TypedWord, "حذف").Add(x => x.OnConfirm, r => reason = r));
        Assert.Equal("alertdialog", cut.Find(".dialog").GetAttribute("role"));
        Assert.Equal("true", cut.Find(".dialog").GetAttribute("aria-modal"));
        IElement Confirm() => cut.FindAll(".df button")[1];
        Assert.True(Confirm().HasAttribute("disabled"));
        Act(() => cut.Find("textarea").Input("سعر خاطئ"));
        Assert.True(Confirm().HasAttribute("disabled"));
        Act(() => cut.Find("input").Input("حذف"));
        Assert.False(Confirm().HasAttribute("disabled"));
        Act(() => Confirm().Click());
        Assert.Equal("سعر خاطئ", reason);
    }

    [Fact]
    public void Modal_closes_with_Escape_and_labels_itself()
    {
        var open = true;
        var cut = Render<Modal>(p => p.Add(x => x.Open, open).Add(x => x.OpenChanged, v => open = v).Add(x => x.Title, "تعذّر الاعتماد"));
        AngleSharp.Dom.IElement dlg() => cut.Find("[role=dialog]");
        Assert.Equal("تعذّر الاعتماد", cut.Find("#" + dlg().GetAttribute("aria-labelledby")).TextContent);
        Act(() => dlg().KeyDown(new KeyboardEventArgs { Key = "Escape" }));
        Assert.False(open);
    }

    [Fact]
    public void State_view_shows_skeleton_error_with_reference_and_content()
    {
        var cut = Render<StateView>(p => p.Add(x => x.State, LoadState.Loading).Add(x => x.ChildContent, "<p id='c'>ok</p>"));
        Assert.Equal("true", cut.Find("[aria-busy]").GetAttribute("aria-busy"));
        Assert.NotNull(cut.Find("[role=status]"));
        cut.Render(p => p.Add(x => x.State, LoadState.Error).Add(x => x.ErrorReference, "7F3A-21C9").Add(x => x.OnRetry, () => { }));
        Assert.Contains("7F3A-21C9", cut.Find("[role=alert]").TextContent);
        cut.Render(p => p.Add(x => x.State, LoadState.Ready));
        Assert.Equal("ok", cut.Find("#c").TextContent);
    }

    [Fact]
    public void Toasts_are_announced_and_dismissable()
    {
        var host = Render<ToastHost>();
        var toasts = Services.GetService<ToastService>();
        host.InvokeAsync(() => toasts!.Error("فشل التصدير."));
        host.WaitForAssertion(() => Assert.Contains("فشل التصدير.", host.Find(".toast").TextContent));
        Assert.Equal("polite", host.Find(".toasts").GetAttribute("aria-live"));
        Act(() => host.Find(".toast .x").Click());
        host.WaitForAssertion(() => Assert.Empty(host.FindAll(".toast")));
    }
}
