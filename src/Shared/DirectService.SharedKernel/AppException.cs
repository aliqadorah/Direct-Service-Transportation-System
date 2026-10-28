namespace DirectService.SharedKernel;

/// <summary>
/// An expected failure returned to the client as problem details (API-06): the code, HTTP status,
/// optional field and message arguments. The message text is produced from
/// the error catalog in the user's language by the API layer.
/// </summary>
public class AppException : Exception
{
    /// <summary>The generic validation code (equals <c>ErrorCodes.ValidationFailed</c> of Contracts; Architecture.Tests checks it).</summary>
    public const string ValidationFailedCode = "validation.failed";

    public AppException(string code, int status, string? field = null, params object?[] args)
        : base(code)
    {
        Code = code;
        Status = status;
        Field = field;
        Args = args;
    }

    public AppException() : this(ValidationFailedCode, 400) { }

    public AppException(string message) : this(message, 400) { }

    public AppException(string message, Exception innerException) : base(message, innerException)
    {
        Code = message;
        Status = 500;
        Args = [];
    }

    public string Code { get; } = ValidationFailedCode;
    public int Status { get; }
    public string? Field { get; }
    public object?[] Args { get; } = [];

    /// <summary>More values for the client, written as problem fields (e.g. DT-230: the amount and payment behind <c>trip.partnerPaid</c>).</summary>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }

    /// <summary>Field-level errors for a 400 validation failure.</summary>
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
}

/// <summary>One field error (field name, code, arguments; an argument naming a field is a <c>FieldRef</c>, DT-245).</summary>
public sealed record FieldError(string Field, string Code, object?[] Args);
