using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Erasure of a driver's personal data on request (SEC-19, Jordan PDPL): records are never deleted, so the personal fields of an archived
/// driver are cleared in place — name, national ID, phones, address, bank details — with a reason in the audit log; earlier values in the
/// append-only audit log are masked in every view of it (<see cref="ErasedRecords"/>). Trips and documents keep their numbers and amounts.
/// The legal confirmation is the owner's (19.3). The module's part of the personal-data erasure since Stage 2 session 14 (users: the
/// platform's <c>PersonalDataService</c>).
/// </summary>
public sealed class DriverPersonalDataService(IAppDbContext db, MasterDataSupport support, IChangeContext changes)
{
    public async Task AnonymiseDriverAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        if (d.Status != MasterCodes.Archived) throw Fail.Rule(ErrorCodes.ValidationFailed);
        d.FullName = "Anonymised driver " + d.PublicId.ToString("N")[..6];
        d.NationalId = "X" + d.PublicId.ToString("N")[..9];   // the column holds 10 characters
        d.LicenceNo = d.NationalId;
        d.Phone1 = "0000000000";
        d.Phone2 = null;
        d.Address = null;
        d.Iban = null;
        d.CliqAlias = null;
        d.BankName = null;
        d.PaymentMethod = "Cash";
        d.Notes = null;
        d.CustomValues = null;
        changes.SetAction(d, ErasedRecords.EraseAction, why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
