using DirectService.Accounting.Contracts.Accounting;
using DirectService.Accounting.Domain.Acc;

namespace DirectService.Transportation.Application.Common;

/// <summary>
/// An account as the transport reports and AP/AR screens show it (<see cref="AccountRef"/>): its URL token, code and names — as the chart of
/// accounts service gives it, without needing that service (Stage 3 session 6c).
/// </summary>
public static class AccountRefs
{
    public static AccountRef AccountRefOf(this IUrlTokenService tokens, Account a)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(a);
        return new(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn);
    }
}
