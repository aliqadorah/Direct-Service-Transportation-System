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
}
