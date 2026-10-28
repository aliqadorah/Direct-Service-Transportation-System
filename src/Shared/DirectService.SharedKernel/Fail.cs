using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

/// <summary>Factory helpers for the common failures (shared by every module since Stage 2 session 6, DT-310).</summary>
public static class Fail
{
    public static AppException NotFound() => new(SharedErrorCodes.NotFound, 404);

    public static AppException Forbidden() => new(SharedErrorCodes.Forbidden, 403);

    /// <summary>Business-rule refusal (422).</summary>
    public static AppException Rule(string code, params object?[] args) => new(code, 422, null, args);

    /// <summary>Business-rule refusal tied to a field (422).</summary>
    public static AppException RuleOn(string field, string code, params object?[] args) => new(code, 422, field, args);

    /// <summary>Conflict (409), e.g. duplicates or plan limits.</summary>
    public static AppException Conflict(string code, params object?[] args) => new(code, 409, null, args);

    /// <summary>Single field validation failure (400).</summary>
    public static AppException Field(string field, string code, params object?[] args) =>
        new(SharedErrorCodes.ValidationFailed, 400, field) { Errors = [new FieldError(field, code, args)] };

    /// <summary>Several field validation failures (400).</summary>
    public static AppException Fields(IReadOnlyList<FieldError> errors) =>
        new(SharedErrorCodes.ValidationFailed, 400) { Errors = errors };
}

/// <summary>Collects field errors and throws them together.</summary>
public sealed class Validator
{
    private readonly List<FieldError> _errors = [];

    public Validator Require(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _errors.Add(new FieldError(field, SharedErrorCodes.ValidationRequired, [new FieldRef(field)]));
        return this;
    }

    public Validator Check(bool ok, string field, string code = SharedErrorCodes.ValidationFormat)
    {
        if (!ok) _errors.Add(new FieldError(field, code, [new FieldRef(field)]));
        return this;
    }

    public Validator MaxLength(string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
            _errors.Add(new FieldError(field, SharedErrorCodes.ValidationRange, [new FieldRef(field)]));
        return this;
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0) throw Fail.Fields(_errors);
    }
}
