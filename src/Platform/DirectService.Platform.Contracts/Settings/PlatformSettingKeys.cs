namespace DirectService.Platform.Contracts.Settings;

/// <summary>
/// The platform's cfg.CompanySettings keys (CORE.*, values pinned, e.2). Defined here only: the module's <c>SettingKeys</c> names them under
/// the same names (Stage 2 session 13).
/// </summary>
public static class PlatformSettingKeys
{
    /// <summary>Market validation formats (7.9.1 "phone and IBAN validation formats"; launch market Jordan).</summary>
    public const string PhonePattern = "CORE.PhonePattern";
    public const string IbanPattern = "CORE.IbanPattern";

    /// <summary>Demo company flag (G-11) and setup checklist marks (G-10).</summary>
    public const string IsDemo = "CORE.IsDemo";
    public const string SetupPrefix = "CORE.Setup.";

    public const string JordanPhonePattern = "^(07[789][0-9]{7}|06[0-9]{7})$";
    public const string JordanIbanPattern = "^JO[0-9]{2}[A-Z]{4}[0-9]{4}[A-Z0-9]{18}$";
}
