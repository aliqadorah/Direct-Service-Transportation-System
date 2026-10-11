using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// Files from the API saved in the browser: list exports (Excel / CSV / PDF), statements and printouts. A refusal (e.g. 403, export limit)
/// shows the server's message as an error toast.
/// </summary>
public sealed class Downloads(Api api, UiJs js, ToastService toasts, Loc loc)
{
    /// <summary>GET <paramref name="path"/> (a company path from <see cref="Api.C"/>) with <c>format</c> added to its query.</summary>
    public async Task<bool> SaveAsync(string path, string? format = null, object? body = null)
    {
        if (format is not null) path += (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "format=" + Uri.EscapeDataString(format);
        try
        {
            var file = await api.FileAsync(path, body).ConfigureAwait(false);
            await js.SaveFile(file.Name, file.ContentType, file.Bytes).ConfigureAwait(false);
            return true;
        }
        catch (ApiException e)
        {
            toasts.Error(e.Problem.Detail ?? loc["ui.loadFailed"]);
            return false;
        }
    }
}
