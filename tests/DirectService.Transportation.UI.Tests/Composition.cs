using System.Runtime.CompilerServices;

namespace DirectService.Transportation.UI.Tests;

internal static class Composition
{
    /// <summary>The browser app's composition (DT-369) before any test runs: the same menu, links, help pages and text as the running app.</summary>
#pragma warning disable CA2255 // a test assembly is the composition root here, like Program.cs in the browser app
    [ModuleInitializer]
    internal static void Apply() => DirectService.Transportation.Web.Client.ClientComposition.Apply();
#pragma warning restore CA2255
}
