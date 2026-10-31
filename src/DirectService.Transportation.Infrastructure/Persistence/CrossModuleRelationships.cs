using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// Foreign keys from trn tables to acc tables (platform design a.2, a.4): the transport entities hold only the ids (journal entry, cash box,
/// bank account, account, party), and the accounting entity types are named here by their CLR names, so this module needs no accounting
/// type. Same constraint names, required flags and delete behaviour as the navigations they replace (Stage 3 session 3). The Accounting
/// contributor is applied first, so the named types are in the model.
/// </summary>
internal static class CrossModuleRelationships
{
    private const string JournalEntry = "DirectService.Accounting.Domain.Acc.JournalEntry";
    private const string CashBox = "DirectService.Accounting.Domain.Acc.CashBox";
    private const string BankAccount = "DirectService.Accounting.Domain.Acc.BankAccount";
    private const string Account = "DirectService.Accounting.Domain.Acc.Account";
    private const string ApArParty = "DirectService.Accounting.Domain.Acc.ApArParty";

    public static void Apply(ModelBuilder modelBuilder)
    {
        var advance = modelBuilder.Entity<Advance>();
        Key(advance, CashBox, nameof(Advance.CashBoxId), false, "FK_TRN_Advances_CashBoxes_CashBoxId");
        Key(advance, BankAccount, nameof(Advance.BankAccountId), false, "FK_TRN_Advances_BankAccounts_BankAccountId");
        Key(advance, JournalEntry, nameof(Advance.JournalEntryId), false, "FK_TRN_Advances_JournalEntries_JournalEntryId");

        Key(modelBuilder.Entity<CreditNote>(), JournalEntry, nameof(CreditNote.JournalEntryId), false, "FK_TRN_CreditNotes_JournalEntries_JournalEntryId");
        Key(modelBuilder.Entity<Customer>(), ApArParty, nameof(Customer.ApArPartyId), false, "FK_TRN_Customers_ApArParties_ApArPartyId");
        Key(modelBuilder.Entity<Invoice>(), JournalEntry, nameof(Invoice.JournalEntryId), false, "FK_TRN_Invoices_JournalEntries_JournalEntryId");

        var partnerPayment = modelBuilder.Entity<PartnerPayment>();
        Key(partnerPayment, CashBox, nameof(PartnerPayment.CashBoxId), false, "FK_TRN_PartnerPayments_CashBoxes_CashBoxId");
        Key(partnerPayment, BankAccount, nameof(PartnerPayment.BankAccountId), false, "FK_TRN_PartnerPayments_BankAccounts_BankAccountId");
        Key(partnerPayment, JournalEntry, nameof(PartnerPayment.JournalEntryId), false, "FK_TRN_PartnerPayments_JournalEntries_JournalEntryId");

        var previousBalanceItem = modelBuilder.Entity<PreviousBalanceItem>();
        Key(previousBalanceItem, JournalEntry, nameof(PreviousBalanceItem.JournalEntryId), true, "FK_TRN_PreviousBalanceItems_JournalEntries_JournalEntryId");
        Key(previousBalanceItem, Account, nameof(PreviousBalanceItem.AccountId), true, "FK_TRN_PreviousBalanceItems_Accounts_AccountId");

        var receipt = modelBuilder.Entity<Receipt>();
        Key(receipt, CashBox, nameof(Receipt.CashBoxId), false, "FK_TRN_Receipts_CashBoxes_CashBoxId");
        Key(receipt, BankAccount, nameof(Receipt.BankAccountId), false, "FK_TRN_Receipts_BankAccounts_BankAccountId");
        Key(receipt, JournalEntry, nameof(Receipt.JournalEntryId), false, "FK_TRN_Receipts_JournalEntries_JournalEntryId");

        var settlement = modelBuilder.Entity<Settlement>();
        Key(settlement, CashBox, nameof(Settlement.CashBoxId), false, "FK_TRN_Settlements_CashBoxes_CashBoxId");
        Key(settlement, BankAccount, nameof(Settlement.BankAccountId), false, "FK_TRN_Settlements_BankAccounts_BankAccountId");
        Key(settlement, JournalEntry, nameof(Settlement.JournalEntryId), false, "FK_TRN_Settlements_JournalEntries_JournalEntryId");

        Key(modelBuilder.Entity<Trip>(), JournalEntry, nameof(Trip.JournalEntryId), false, "FK_TRN_Trips_JournalEntries_JournalEntryId");
    }

    /// <summary>A foreign key without navigation to the accounting entity named <paramref name="principal"/> (its key Id), no cascade.</summary>
    private static void Key(EntityTypeBuilder builder, string principal, string foreignKey, bool required, string constraint) =>
        builder.HasOne(principal, null)
            .WithMany()
            .HasForeignKey(foreignKey)
            .HasPrincipalKey("Id")
            .IsRequired(required)
            .HasConstraintName(constraint)
            .OnDelete(DeleteBehavior.NoAction);
}
