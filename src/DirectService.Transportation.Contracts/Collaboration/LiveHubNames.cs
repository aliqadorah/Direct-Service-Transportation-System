namespace DirectService.Transportation.Contracts.Collaboration;

/// <summary>
/// The live-updates hub as the server and the browser app both name it (platform design §e, E-27): path, hub methods the browser calls and
/// events the server sends. Pinned literals shared by both sides; never computed from a class or method name.
/// </summary>
public static class LiveHubNames
{
    public const string Path = "/hubs/live";

    /// <summary>Group of one company's connections: this prefix + the company's internal id (server only).</summary>
    public const string CompanyGroupPrefix = "co:";

    public const string JoinCompany = "JoinCompany";
    public const string LeaveCompany = "LeaveCompany";

    public const string RecordChanged = "recordChanged";
    public const string LockChanged = "lockChanged";
    public const string Notification = "notification";
    public const string SupportEnded = "supportEnded";
}
