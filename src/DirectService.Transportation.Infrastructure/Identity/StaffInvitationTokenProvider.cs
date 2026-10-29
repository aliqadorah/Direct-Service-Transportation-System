using DirectService.Transportation.Application.Platform;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.Identity;

/// <summary>Options of the staff invitation tokens: valid for 48 hours (owner feedback 13 Oct 2026, item 8b).</summary>
public sealed class StaffInvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public StaffInvitationTokenProviderOptions()
    {
        Name = StaffService.InvitationTokenProvider;
        TokenLifespan = TimeSpan.FromHours(StaffService.InvitationHours);
    }
}

/// <summary>
/// Tokens of staff invitation links: protected and time-limited like the password-reset tokens, tied to the account's security stamp, so a
/// link stops working once used (setting the password changes the stamp) or when a new link is sent.
/// </summary>
public sealed class StaffInvitationTokenProvider(IDataProtectionProvider dataProtectionProvider, IOptions<StaffInvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<User>> logger) : DataProtectorTokenProvider<User>(dataProtectionProvider, options, logger);
