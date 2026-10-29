using System.Net.Mail;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Email;

/// <summary>
/// The system e-mail settings as the outbox and its transports read them (DT-250…DT-253), and the address check of every mail header. Moved
/// out of <c>SystemEmailService</c> (Stage 2 session 12, DT-345), which keeps its members of the same names and forwards to these.
/// </summary>
public static class SystemEmailSettings
{
    /// <summary>The settings in use: the Active row, or — never configured — the development folder.</summary>
    public static async Task<SystemEmailSetting> ActiveAsync(IAppDbContext db, SystemEmailOptions o, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(o);
        return await db.Set<SystemEmailSetting>().AsNoTracking().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Active, ct).ConfigureAwait(false)
            ?? Default(o);
    }

    /// <summary>The settings when none were ever saved: the development folder.</summary>
    public static SystemEmailSetting Default(SystemEmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new()
        {
            Id = 0, State = EmailSettingStates.Active, Mode = EmailModes.Development, SenderMailbox = o.From, DisplayName = EmailLayout.ProductEn,
        };
    }

    /// <summary>A settings row as the transport needs it (the secret decrypted, in memory only for the call).</summary>
    public static EmailTransportSettings ToTransport(SystemEmailSetting s, IEmailProtector protector, SystemEmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(o);
        return s.Mode == EmailModes.Microsoft365
            ? new EmailTransportSettings(s.Mode, s.SenderMailbox!, s.DisplayName, s.ReplyTo, s.EntraTenantId, s.EntraClientId,
                s.ClientSecretEncrypted is { } secret ? protector.UnprotectSecret(secret) : null)
            : new EmailTransportSettings(s.Mode, o.From, s.DisplayName, s.ReplyTo);
    }

    /// <summary>Line breaks and other control characters are refused in anything that becomes a mail header (header injection).</summary>
    public static bool HasControl(string s) => s.Any(char.IsControl);

    /// <summary>One plain address ("name@example.com"), no display name, no control characters.</summary>
    public static bool IsAddress(string s) =>
        !HasControl(s) && !s.Contains(' ', StringComparison.Ordinal) && MailAddress.TryCreate(s, out var a) && string.Equals(a.Address, s, StringComparison.OrdinalIgnoreCase)
        && a.Host.Contains('.', StringComparison.Ordinal);
}
