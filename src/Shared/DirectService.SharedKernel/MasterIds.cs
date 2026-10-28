using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

/// <summary>Small request helpers shared by the master-data and configuration services of every module (SharedKernel since Stage 2 session 7, DT-318).</summary>
public static class MasterIds
{
    /// <summary>
    /// The client-generated identifier of a create (API-05, G-13): a GUID that becomes the record's PublicId, so a repeated
    /// create returns the record already created. Null when not sent; 400 when not a GUID.
    /// </summary>
    public static Guid? ClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        if (!Guid.TryParse(clientId, out var id) || id == Guid.Empty) throw Fail.Field("clientId", SharedErrorCodes.ValidationFormat, new FieldRef("clientId"));
        return id;
    }

    /// <summary>Reason of a soft delete (BR-041): required, at most 500 characters kept.</summary>
    public static string Reason(string? reason)
    {
        var why = TextNormalizer.CleanText(reason) ?? throw Fail.Field("reason", SharedErrorCodes.ReasonRequired);
        return why.Length <= 500 ? why : why[..500];
    }
}
