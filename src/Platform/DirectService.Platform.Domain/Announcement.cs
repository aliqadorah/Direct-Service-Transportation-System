// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Platform.Domain;

/// <summary>
/// core.Announcements â€” Platform-wide.
/// </summary>
[StoredName("CORE.Announcement")]
[TokenLabel("Announcement")]
public class Announcement : AuditableEntity
{
    /// <summary>Notice title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Notice text.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Show from.</summary>
    public DateTime StartsAt { get; set; }
    /// <summary>Show until.</summary>
    public DateTime EndsAt { get; set; }
}
