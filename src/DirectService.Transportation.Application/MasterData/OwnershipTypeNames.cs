using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Platform;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// The built-in ownership type is stored as the code "Company" (DT-15) and shown in the reader's language everywhere it appears — lists,
/// pick lists, driver records, exports, reports (owner feedback 13 Oct 2026). A user type can never carry that name (in either language),
/// so the stored code identifies the built-in type; its Arabic name is also accepted when it is typed back (forms, imports).
/// </summary>
public static class OwnershipTypeNames
{
    public const string ArabicName = "الشركة";

    /// <summary>The name to show: the built-in type in the reader's language, any other type as stored.</summary>
    public static string Display(string? name, string? language) =>
        name == CompanyProvisioningService.BuiltInOwnershipType ? (language == "en" ? CompanyProvisioningService.BuiltInOwnershipType : ArabicName) : name ?? string.Empty;

    /// <summary>A typed name meaning the built-in type ("Company", "الشركة", "شركة"), compared like other names (case, spaces, Arabic forms).</summary>
    public static bool IsBuiltIn(string? text) =>
        TextNormalizer.Key(text) is var k && (k == TextNormalizer.Key(CompanyProvisioningService.BuiltInOwnershipType) || k == TextNormalizer.Key(ArabicName) || k == TextNormalizer.Key("شركة"));
}
