namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>Unique element ids for label/field, aria-controls and aria-describedby links.</summary>
public static class Ids
{
    private static long _n;

    public static string Next(string prefix) => $"{prefix}-{Interlocked.Increment(ref _n)}";
}
