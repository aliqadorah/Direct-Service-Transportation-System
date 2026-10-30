using System.Net.Mail;

namespace DirectService.SharedKernel;

/// <summary>The e-mail address check of every form (accounts, users, customers, company profile); Shared since Stage 2 session 14 (was <c>AccountService.IsEmail</c>).</summary>
public static class EmailFormat
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' ', StringComparison.Ordinal)) return false;
        try
        {
            var a = new MailAddress(value);
            return a.Address == value && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
