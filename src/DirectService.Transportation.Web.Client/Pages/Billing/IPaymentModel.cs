namespace DirectService.Transportation.Web.Client.Pages.Billing;

/// <summary>The payment part of a cash document form (<see cref="PaymentFields"/>); only the account that fits the method is sent.</summary>
public interface IPaymentModel
{
    string? PaymentMethod { get; set; }
    string? CashBoxId { get; set; }
    string? BankAccountId { get; set; }
    string? Reference { get; set; }
}

public static class PaymentModelExtensions
{
    public static string? CashBox(this IPaymentModel m) => m.PaymentMethod == Contracts.Cash.CashCodes.Cash ? m.CashBoxId : null;

    public static string? Bank(this IPaymentModel m) => m.PaymentMethod == Contracts.Cash.CashCodes.Cash ? null : m.BankAccountId;

    public static string? Ref(this IPaymentModel m) => string.IsNullOrWhiteSpace(m.Reference) ? null : m.Reference.Trim();
}
