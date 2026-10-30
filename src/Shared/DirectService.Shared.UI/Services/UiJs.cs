using Microsoft.JSInterop;

namespace DirectService.Shared.UI.Services;

/// <summary>Typed access to <c>wwwroot/js/ui.js</c> (loaded once as an ES module). Calls are no-ops when JS is not available (prerender, tests).</summary>
public sealed class UiJs(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/ui.js").AsTask();

    private async ValueTask Call(string name, params object?[] args)
    {
        try { await (await Module.ConfigureAwait(false)).InvokeVoidAsync(name, args).ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }

    private async ValueTask<T?> Get<T>(string name, params object?[] args)
    {
        try { return await (await Module.ConfigureAwait(false)).InvokeAsync<T>(name, args).ConfigureAwait(false); }
        catch (JSDisconnectedException) { return default; }
        catch (InvalidOperationException) { return default; }
    }

    public ValueTask TrapFocus(Microsoft.AspNetCore.Components.ElementReference el, string id) => Call("trapFocus", el, id);
    public ValueTask ReleaseFocus(string id) => Call("releaseFocus", id);
    public ValueTask RegisterShortcuts(object dotNetRef) => Call("registerShortcuts", dotNetRef);
    public ValueTask UnregisterShortcuts() => Call("unregisterShortcuts");
    public ValueTask OnOutside(Microsoft.AspNetCore.Components.ElementReference el, object dotNetRef, string id) => Call("onOutside", el, dotNetRef, id);
    public ValueTask OffOutside(string id) => Call("offOutside", id);
    public ValueTask NavFlyouts(Microsoft.AspNetCore.Components.ElementReference nav) => Call("navFlyouts", nav);
    public ValueTask PlaceFly(string id, bool focus) => Call("placeFly", id, focus);
    public ValueTask<string?> GetPref(string key) => Get<string?>("getPref", key);

    /// <summary>Seconds since the user last pressed a key, clicked, typed or scrolled (record-lock heartbeat, BR-039).</summary>
    public ValueTask<int> IdleSeconds() => Get<int>("idleSeconds");
    public ValueTask SetPref(string key, string value) => Call("setPref", key, value);
    public ValueTask ApplyRoot(string? lang) => Call("applyRoot", lang);
    public ValueTask ApplyTheme(string theme) => Call("applyTheme", theme);
    public ValueTask<string?> RootLang() => Get<string?>("rootLang");
    public ValueTask FocusById(string id) => Call("focusById", id);
    public ValueTask GuardComboKeys(Microsoft.AspNetCore.Components.ElementReference el) => Call("guardComboKeys", el);
    public ValueTask FocusMenu(string id, int delta) => Call("focusMenu", id, delta);
    public ValueTask FocusFirst(Microsoft.AspNetCore.Components.ElementReference el) => Call("focusFirst", el);
    public ValueTask ShowPicker(Microsoft.AspNetCore.Components.ElementReference el) => Call("showPicker", el);
    public ValueTask SaveFile(string name, string contentType, byte[] bytes) => Call("saveFile", name, contentType, bytes);
    public ValueTask PrintHtml(Microsoft.AspNetCore.Components.ElementReference el, string html) => Call("printHtml", el, html);
    public ValueTask ScrollIntoView(Microsoft.AspNetCore.Components.ElementReference el) => Call("scrollIntoView", el);
    /// <summary>Keyboard moves of an amount grid (DT-265): Enter / Shift+Enter to the same column below / above; a cell's text is selected on focus.</summary>
    public ValueTask AmountGrid(Microsoft.AspNetCore.Components.ElementReference el) => Call("amountGrid", el);

    /// <summary>Brings a panel into view when it is stacked under the lists (DT-246); nothing when it stands beside them.</summary>
    public ValueTask RevealStacked(string selector) => Call("revealStacked", selector);

    /// <summary>An open combo list as an overlay fixed to the window (never clipped by a scrolling table or panel); <see cref="UnfloatList"/> ends it.</summary>
    public ValueTask FloatList(Microsoft.AspNetCore.Components.ElementReference wrap, string id) => Call("floatList", wrap, id);
    public ValueTask UnfloatList(string id) => Call("unfloatList", id);

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try { await (await _module.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }
}
