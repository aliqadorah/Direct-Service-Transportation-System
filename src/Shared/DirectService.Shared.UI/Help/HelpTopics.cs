namespace DirectService.Shared.UI.Help;

/// <summary>The text of one help page in one language: what the flow is for, who may do it, the steps, and what to do when it goes wrong.</summary>
public sealed record HelpText(string Title, string Summary, string Who, IReadOnlyList<string> Steps, IReadOnlyList<string> Problems);

/// <summary>A screen used by the flow (route under the company, menu text key), linked from the help page.</summary>
public sealed record HelpScreen(string Route, string LabelKey);

/// <summary>A help page (D-18, REL-10): one per PRD Section 9 flow, in Arabic and English, linked from the screens of that flow.</summary>
public sealed record HelpTopic(string Key, string Prd, HelpText Ar, HelpText En, IReadOnlyList<HelpScreen> Screens);

/// <summary>
/// Help pages of the PRD Section 9 flows. Screens link to their topic with <c>&lt;PageHeader Help="…" /&gt;</c>; the menu has a Help entry
/// with the list. Button and menu names in the text are the ones the screens show (Localization/UiText*.cs) — keep them in step.
/// </summary>
public static class HelpTopics
{
    /// <summary>Help pages of the PRD Section 9 flows, given by the browser app (Stage 2 session 15, DT-369; per module in Stage 4).</summary>
    public static IReadOnlyList<HelpTopic> All { get; private set; } = [];

    /// <summary>Help pages of the provider console (shown at /console/help/{key}), given by the browser app.</summary>
    public static IReadOnlyList<HelpTopic> ConsoleTopics { get; private set; } = [];

    /// <summary>Sets the help pages once at start-up (the browser app's composition, <c>ClientComposition</c>).</summary>
    public static void Use(IReadOnlyList<HelpTopic> all, IReadOnlyList<HelpTopic> console)
    {
        All = all;
        ConsoleTopics = console;
    }

    public static HelpTopic? Find(string? key) => All.Concat(ConsoleTopics).FirstOrDefault(t => t.Key == key);

    /// <summary>A help page of the provider console (shown at /console/help/{key}; its screens are console addresses).</summary>
    public static bool IsConsole(string? key) => ConsoleTopics.Any(t => t.Key == key);
}
