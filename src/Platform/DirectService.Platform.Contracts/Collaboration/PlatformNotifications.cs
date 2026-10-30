using System.Globalization;
using System.Text.Json;

namespace DirectService.Platform.Contracts.Collaboration;

/// <summary>
/// The platform's notification types (stored in core.Notifications.Type; values pinned) and the stored form of a notification's message
/// (DT-189): its parameters as JSON {"p":{…}}, the text built in the reader's language when shown. Defined here only: the module's
/// <c>NotificationService</c> names the types and <c>NotificationTexts</c> stores and reads through these members (Stage 2 session 13;
/// Platform.Contracts since session 14, so a module stores and sends notifications without Platform.Application).
/// </summary>
public static class PlatformNotifications
{
    public const string ExportReady = "CORE.ExportReady";
    public const string ExportFailed = "CORE.ExportFailed";
    public const string InvitationAccepted = "CORE.InvitationAccepted";
    public const string SupportRequested = "CORE.SupportRequested";
    public const string PaymentFailed = "CORE.PaymentFailed";

    /// <summary>The length of core.Notifications.Message.</summary>
    public const int MaxMessage = 500;

    private sealed record Stored([property: System.Text.Json.Serialization.JsonPropertyName("p")] Dictionary<string, string>? P);

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The stored form of the parameters, trimmed so it fits the 500-character column (longest values shortened first).</summary>
    public static string Store(IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var p = parameters.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(new Stored(p), Json);
        while (json.Length > MaxMessage)
        {
            var longest = p.MaxBy(kv => kv.Value.Length);
            p[longest.Key] = Cut(longest.Value, Math.Max(1, longest.Value.Length - Math.Max(2, json.Length - MaxMessage)));
            json = JsonSerializer.Serialize(new Stored(p), Json);
        }
        return json;
    }

    /// <summary>A number parameter as stored (invariant digits).</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Cut(string text, int max) => text.Length <= max ? text : max <= 1 ? "…" : text[..(max - 1)] + "…";

    /// <summary>The parameters of a message in the stored form ({"p":…}); null when it cannot be read.</summary>
    public static Dictionary<string, string>? Read(string message)
    {
        try
        {
            return JsonSerializer.Deserialize<Stored>(message)?.P ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Sends in-app notifications (FR-190) to users: stored with their type and parameters, pushed after the commit. Implemented by the
/// <c>NotificationService</c>.
/// </summary>
public interface IUserNotifier
{
    /// <summary>Adds a notification for each user and saves (inside the caller's transaction when there is one), then pushes it.</summary>
    Task NotifyAsync(IEnumerable<long> userIds, long? companyId, string type, IReadOnlyDictionary<string, string?> parameters, string? link, CancellationToken ct,
        long? tenant = null);

    /// <summary>Active users holding <paramref name="permission"/> in the company (recipients of a company event).</summary>
    Task<IReadOnlyList<long>> HoldersAsync(long companyId, string permission, CancellationToken ct);

    /// <summary>
    /// Keeps ONE notification of <paramref name="type"/> per recipient and company: an existing one only gets the new parameters, the others
    /// get a new notification (e.g. e-invoicing set up wrongly, DT-189).
    /// </summary>
    Task KeepOneAsync(IEnumerable<long> userIds, long companyId, string type, IReadOnlyDictionary<string, string?> parameters, string? link, CancellationToken ct);

    /// <summary>Deletes the company's notifications of <paramref name="type"/> (the problem they report is gone).</summary>
    Task<int> ClearAsync(long companyId, string type, CancellationToken ct);
}

/// <summary>A notification as the reader sees it: a short title, one readable sentence, and the technical details (codes, raw answers) kept apart.</summary>
public sealed record NotificationText(string Title, string Message, string? Details);

/// <summary>Where a notification leads: a screen route (company-relative, or absolute for tenant screens) or why it cannot be opened.</summary>
public sealed record NotificationTarget(string? Route, string? Problem)
{
    /// <summary>The record the link names no longer exists (or never did).</summary>
    public const string Missing = "missing";

    /// <summary>The reader may not open the screen.</summary>
    public const string Forbidden = "forbidden";

    public static readonly NotificationTarget None = new(null, null);
}

/// <summary>
/// Older notifications of <paramref name="FromType"/> whose stored message contains <paramref name="MessageContains"/> are replaced, for the
/// reader in the company, by one notification of <paramref name="IntoType"/> linking to <paramref name="Link"/> (unread when any of them was
/// unread), carrying <paramref name="Parameter"/> of one of them, an unread one when there is one (DT-189).
/// </summary>
public sealed record NotificationMerge(string FromType, string MessageContains, string IntoType, string Link, string Parameter);

/// <summary>The words of one notification type: title and sentence in both languages ({name} placeholders), and the parameter shown as details.</summary>
public sealed record NotificationWords(string TitleAr, string TitleEn, string MessageAr, string MessageEn, string? DetailsParam = null);
