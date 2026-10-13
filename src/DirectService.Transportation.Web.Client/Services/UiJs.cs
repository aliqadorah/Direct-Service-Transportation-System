using Microsoft.JSInterop;

namespace DirectService.Transportation.Web.Client.Services;

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
    public ValueTask<string?> GetPref(string key) => Get<string?>("getPref", key);

    /// <summary>Seconds since the user last pressed a key, clicked, typed or scrolled (record-lock heartbeat, BR-039).</summary>
    public ValueTask<int> IdleSeconds() => Get<int>("idleSeconds");
    public ValueTask SetPref(string key, string value) => Call("setPref", key, value);
    public ValueTask ApplyRoot(string density, string? lang) => Call("applyRoot", density, lang);
    public ValueTask<string?> RootLang() => Get<string?>("rootLang");
    public ValueTask FocusById(string id) => Call("focusById", id);
    public ValueTask GuardComboKeys(Microsoft.AspNetCore.Components.ElementReference el) => Call("guardComboKeys", el);
    public ValueTask FocusMenu(string id, int delta) => Call("focusMenu", id, delta);
    public ValueTask FocusFirst(Microsoft.AspNetCore.Components.ElementReference el) => Call("focusFirst", el);
    public ValueTask ShowPicker(Microsoft.AspNetCore.Components.ElementReference el) => Call("showPicker", el);
    public ValueTask SaveFile(string name, string contentType, byte[] bytes) => Call("saveFile", name, contentType, bytes);
    public ValueTask PrintHtml(Microsoft.AspNetCore.Components.ElementReference el, string html) => Call("printHtml", el, html);
    public ValueTask ScrollIntoView(Microsoft.AspNetCore.Components.ElementReference el) => Call("scrollIntoView", el);

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
