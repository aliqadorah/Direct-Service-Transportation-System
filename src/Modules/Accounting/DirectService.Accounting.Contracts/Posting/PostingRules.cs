namespace DirectService.Accounting.Contracts.Posting;

/// <summary>
/// The module and type codes of a party on an entry line and of an entry's source module (stored values, design e; Stage 3 session 5). Each
/// value is defined here only; <c>AccountingCodes</c> names them under its own names.
/// </summary>
public static class PartyCodes
{
    public const string ModuleTrn = "TRN";
    public const string ModuleAcc = "ACC";

    public const string Customer = "Customer";
    public const string Driver = "Driver";
    public const string Partner = "Partner";
    public const string ApArParty = "ApArParty";
}

/// <summary>
/// The posting rules a module needs without the engine (Stage 3 session 5): the lengths of an entry's texts and the control-party rule (G-17).
/// The engine applies the same rules.
/// </summary>
public static class PostingRules
{
    public const int DescriptionMax = 500;

    /// <summary>Reference / المرجع of an entry (revision 6).</summary>
    public const int ReferenceMax = 100;
    public const int NoteMax = 300;

    /// <summary>G-17: Customer = TRN customer or ACC party with role Customer; Supplier = ACC party with role Supplier; Driver/Partner = TRN.</summary>
    public static bool PartyMatches(string? controlType, string module, string type, long id, IReadOnlyDictionary<long, string> apArRoles)
    {
        ArgumentNullException.ThrowIfNull(apArRoles);
        return controlType switch
        {
            "Customer" => (module, type) is (PartyCodes.ModuleTrn, PartyCodes.Customer)
                          || ((module, type) is (PartyCodes.ModuleAcc, PartyCodes.ApArParty) && apArRoles.GetValueOrDefault(id) == "Customer"),
            "Supplier" => (module, type) is (PartyCodes.ModuleAcc, PartyCodes.ApArParty) && apArRoles.GetValueOrDefault(id) == "Supplier",
            "Driver" => (module, type) is (PartyCodes.ModuleTrn, PartyCodes.Driver),
            "Partner" => (module, type) is (PartyCodes.ModuleTrn, PartyCodes.Partner),
            _ => false,
        };
    }
}
