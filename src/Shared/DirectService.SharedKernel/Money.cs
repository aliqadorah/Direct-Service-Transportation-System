namespace DirectService.SharedKernel;

/// <summary>Money rules (BR-020, BR-021): half-up rounding to the currency decimals.</summary>
public static class Money
{
    public static decimal Round(decimal value, int decimals = 3) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Largest amount a DECIMAL(18,3) column holds (15 integer digits).</summary>
    public const decimal MaxAmount = 999_999_999_999_999.999m;

    /// <summary>True when the amount has no more decimals than the currency allows and fits DECIMAL(18,3) (SR-B2-01).</summary>
    public static bool IsValidAmount(decimal value, int decimals = 3) =>
        value >= 0 && value <= MaxAmount && Round(value, decimals) == value;

    public static string Format(decimal value) => value.ToString("#,##0.000", System.Globalization.CultureInfo.InvariantCulture);
}
