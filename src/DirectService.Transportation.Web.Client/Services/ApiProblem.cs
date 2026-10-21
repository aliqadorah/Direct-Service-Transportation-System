using System.Text.Json.Serialization;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// RFC 9457 problem details as the API writes them (API-06, ADR-14): <c>detail</c> is already in the user's language; <c>field</c> names the
/// single field of a business refusal; <c>errors</c> lists field validation errors; <c>traceId</c> is the reference shown to the user.
/// </summary>
public sealed record ApiProblem
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("status")] public int Status { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("field")] public string? Field { get; init; }
    [JsonPropertyName("traceId")] public string? TraceId { get; init; }
    [JsonPropertyName("errors")] public IReadOnlyList<ApiFieldError>? Errors { get; init; }

    /// <summary>A closed support window (DT-220): when it ended (UTC) and who ended it early.</summary>
    [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; init; }
    [JsonPropertyName("endedBy")] public string? EndedBy { get; init; }

    /// <summary>A partner trip already paid (DT-230): the amount the partner would owe, the trip's cost and the partner's last payment.</summary>
    [JsonPropertyName("overpaid")] public decimal? Overpaid { get; init; }
    [JsonPropertyName("tripCost")] public decimal? TripCost { get; init; }
    [JsonPropertyName("paymentId")] public string? PaymentId { get; init; }
    [JsonPropertyName("paymentNumber")] public string? PaymentNumber { get; init; }
    [JsonPropertyName("paymentAmount")] public decimal? PaymentAmount { get; init; }

    /// <summary>A short reference for support (the last part of the trace id).</summary>
    public string? Reference => TraceId is { Length: > 9 } t ? t[^9..].TrimStart(':').ToUpperInvariant() : TraceId;
}

public sealed record ApiFieldError(
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message);
