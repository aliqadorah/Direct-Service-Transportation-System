using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// An expected failure returned to the client as problem details (API-06): the code, HTTP status,
/// optional field and message arguments. The message text is produced from
/// <see cref="ErrorCatalog"/> in the user's language by the API layer.
/// </summary>
public class AppException : Exception
{
    public AppException(string code, int status, string? field = null, params object?[] args)
        : base(code)
    {
        Code = code;
        Status = status;
        Field = field;
        Args = args;
    }

    public AppException() : this(ErrorCodes.ValidationFailed, 400) { }

    public AppException(string message) : this(message, 400) { }

    public AppException(string message, Exception innerException) : base(message, innerException)
    {
        Code = message;
        Status = 500;
        Args = [];
    }

    public string Code { get; } = ErrorCodes.ValidationFailed;
    public int Status { get; }
    public string? Field { get; }
    public object?[] Args { get; } = [];

    /// <summary>More values for the client, written as problem fields (e.g. DT-230: the amount and payment behind <c>trip.partnerPaid</c>).</summary>
    public IReadOnlyDictionary<string, object?>? Extra { get; init; }

    /// <summary>Field-level errors for a 400 validation failure.</summary>
    public IReadOnlyList<FieldError> Errors { get; init; } = [];
}

/// <summary>One field error (field name, code, arguments; an argument naming a field is a <see cref="FieldRef"/>, DT-245).</summary>
public sealed record FieldError(string Field, string Code, object?[] Args);

/// <summary>Factory helpers for the common failures.</summary>
public static class Fail
{
    public static AppException NotFound() => new(ErrorCodes.NotFound, 404);

    public static AppException Forbidden() => new(ErrorCodes.Forbidden, 403);

    /// <summary>Business-rule refusal (422).</summary>
    public static AppException Rule(string code, params object?[] args) => new(code, 422, null, args);

    /// <summary>Business-rule refusal tied to a field (422).</summary>
    public static AppException RuleOn(string field, string code, params object?[] args) => new(code, 422, field, args);

    /// <summary>Conflict (409), e.g. duplicates or plan limits.</summary>
    public static AppException Conflict(string code, params object?[] args) => new(code, 409, null, args);

    /// <summary>Single field validation failure (400).</summary>
    public static AppException Field(string field, string code, params object?[] args) =>
        new(ErrorCodes.ValidationFailed, 400, field) { Errors = [new FieldError(field, code, args)] };

    /// <summary>Several field validation failures (400).</summary>
    public static AppException Fields(IReadOnlyList<FieldError> errors) =>
        new(ErrorCodes.ValidationFailed, 400) { Errors = errors };
}

/// <summary>Collects field errors and throws them together.</summary>
public sealed class Validator
{
    private readonly List<FieldError> _errors = [];

    public Validator Require(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _errors.Add(new FieldError(field, ErrorCodes.ValidationRequired, [new FieldRef(field)]));
        return this;
    }

    public Validator Check(bool ok, string field, string code = ErrorCodes.ValidationFormat)
    {
        if (!ok) _errors.Add(new FieldError(field, code, [new FieldRef(field)]));
        return this;
    }

    public Validator MaxLength(string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
            _errors.Add(new FieldError(field, ErrorCodes.ValidationRange, [new FieldRef(field)]));
        return this;
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0) throw Fail.Fields(_errors);
    }
}
