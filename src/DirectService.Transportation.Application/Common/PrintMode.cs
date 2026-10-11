namespace DirectService.Transportation.Application.Common;

/// <summary>
/// Whether the current request renders a document for the on-screen print preview (SCR-09, <c>?preview=true</c> on a print endpoint):
/// the document is shown exactly as it would print, including a "Copy" mark when it was printed before, but no print is logged.
/// </summary>
public sealed class PrintMode
{
    public bool Preview { get; set; }
}
