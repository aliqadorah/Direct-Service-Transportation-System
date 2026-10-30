using DirectService.Platform.Application.Staff;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// The staff invitation link (owner feedback 13 Oct 2026, item 8b): its Identity token provider and purpose (golden, E-26) and its 48-hour
/// lifespan. Defined once here for the token provider of Platform.Infrastructure; <c>StaffService</c> names them under its own constants
/// until it moves to Platform (Stage 2 session 12, DT-344).
/// </summary>
public static class StaffInvitations
{
    public const string TokenProvider = "StaffInvitation";
    public const string Purpose = "StaffInvitation";
    public const int Hours = 48;
}
