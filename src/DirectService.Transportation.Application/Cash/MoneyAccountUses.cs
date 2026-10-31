using DirectService.Accounting.Contracts.Modules;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// The transport documents that use a cash box or bank account (<see cref="IMoneyAccountUseSource"/>, Stage 3 session 6c): receipts, driver
/// advances, settlements and partner payments, in that order (the cash boxes' and bank accounts' delete check, BR-040).
/// </summary>
public sealed class MoneyAccountUses(IAppDbContext db) : IMoneyAccountUseSource
{
    public async Task<IReadOnlyList<RecordUse>> UsesAsync(long? cashBoxId, long? bankAccountId, CancellationToken ct)
    {
        var uses = new List<RecordUse>();
        void Add(string type, int count) { if (count > 0) uses.Add(new(type, count)); }
        if (cashBoxId is long box)
        {
            Add("Receipts", await db.Set<Receipt>().CountAsync(x => x.CashBoxId == box, ct).ConfigureAwait(false));
            Add("Advances", await db.Set<Advance>().CountAsync(x => x.CashBoxId == box, ct).ConfigureAwait(false));
            Add("Settlements", await db.Set<Settlement>().CountAsync(x => x.CashBoxId == box, ct).ConfigureAwait(false));
            Add("PartnerPayments", await db.Set<PartnerPayment>().CountAsync(x => x.CashBoxId == box, ct).ConfigureAwait(false));
        }
        else if (bankAccountId is long bank)
        {
            Add("Receipts", await db.Set<Receipt>().CountAsync(x => x.BankAccountId == bank, ct).ConfigureAwait(false));
            Add("Advances", await db.Set<Advance>().CountAsync(x => x.BankAccountId == bank, ct).ConfigureAwait(false));
            Add("Settlements", await db.Set<Settlement>().CountAsync(x => x.BankAccountId == bank, ct).ConfigureAwait(false));
            Add("PartnerPayments", await db.Set<PartnerPayment>().CountAsync(x => x.BankAccountId == bank, ct).ConfigureAwait(false));
        }
        return uses;
    }
}
