namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>
/// Toasts (UX-19): a short message naming what happened, optionally with one link (e.g. "Open INV-02210"). Success toasts close after
/// 6 s; error toasts stay until dismissed. Rendered by <see cref="ToastHost"/> in a polite live region.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> _items = [];

    public IReadOnlyList<Toast> Items => _items;

    public event Action? Changed;

    public void Success(string text, string? linkText = null, string? href = null) => Add(new Toast(Guid.NewGuid(), text, false, linkText, href));

    public void Error(string text) => Add(new Toast(Guid.NewGuid(), text, true, null, null));

    public void Dismiss(Guid id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke();
    }

    private void Add(Toast t)
    {
        _items.Add(t);
        if (_items.Count > 3) _items.RemoveAt(0);
        Changed?.Invoke();
        if (!t.IsError) _ = DismissLaterAsync(t.Id);
    }

    private async Task DismissLaterAsync(Guid id)
    {
        await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        Dismiss(id);
    }
}

public sealed record Toast(Guid Id, string Text, bool IsError, string? LinkText, string? Href);
