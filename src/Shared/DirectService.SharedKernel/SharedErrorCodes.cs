namespace DirectService.SharedKernel;

/// <summary>
/// Problem codes (API-06) thrown by the shared building blocks (validation, access, locks, spreadsheets). Each value is defined here only:
/// <c>ErrorCodes</c> of the Contracts refers to these constants under the same names, and the Arabic and English messages stay in its error
/// catalogue (Stage 2 session 6, DT-305 / DT-310). A value never changes — it is the "code" field clients receive.
/// </summary>
public static class SharedErrorCodes
{
    public const string ValidationRequired = "validation.required";
    public const string ValidationFormat = "validation.format";
    public const string ValidationRange = "validation.range";
    public const string ValidationFailed = AppException.ValidationFailedCode;
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string NotFound = "notFound";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string DuplicateKey = "duplicate.key";
    public const string LockHeld = "lock.held";
    public const string RateLimited = "rateLimited";
    public const string ImportFileType = "import.fileType";
    public const string ImportTooLarge = "import.tooLarge";
    public const string ImportEmpty = "import.empty";
    public const string ImportMissingColumn = "import.missingColumn";
    public const string ImportFormula = "import.formula";
}
