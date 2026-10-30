using DirectService.Platform.Contracts.Collaboration;
using DirectService.Transportation.Web.Client.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Notifications panel (FR-190, DT-189): each notification is one block (title, message, date); opening one marks it read, closes the panel
/// and goes to its screen in the company named by its URL token; a record that is gone gives a message instead; technical details stay
/// behind "Details"; "Mark all as read".
/// </summary>
public sealed class NotificationsPanelTests : PageTestBase
{
    private static readonly DateTime At = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    private static NotificationResponse Note(string id, string? link, string? problem = null, DateTime? readAt = null, string? details = null) =>
        new(id, "EINV.Rejected", "رُفضت فاتورة إلكترونية", "رفض نظام الفوترة الوطني المستند INV-00007. افتحه لتصحيحه وإعادة إرساله.", details, link, problem,
            Company, "شركة تجريبية (للتدريب)", At, readAt);

    private readonly List<bool> _openChanges = [];

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<NotificationsPanel> Panel(params NotificationResponse[] notes)
    {
        Respond("notifications", new NotificationPage(notes, notes.Count(n => n.ReadAt is null)));
        Respond("notifications/", (object?)null);
        Shell.SetNotifications(notes.Count(n => n.ReadAt is null));
        return Render<NotificationsPanel>(p => p.Add(x => x.Open, true).Add(x => x.OpenChanged, (bool open) => _openChanges.Add(open)));
    }

    [Fact]
    public void Clicking_a_notification_marks_it_read_closes_the_panel_and_opens_its_record_DT189()
    {
        var cut = Panel(Note("n1", "invoices/inv1"), Note("n2", "settings/e-invoicing"));
        cut.WaitForElement(".note-open");
        Act(() => cut.FindAll(".note-open")[0].Click());
        cut.WaitForAssertion(() => Assert.EndsWith("/c/c1/invoices/inv1", Nav.Uri, StringComparison.Ordinal));
        Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "notifications/n1/read");
        Assert.Equal([false], _openChanges);
        Assert.Equal(1, Shell.UnreadNotifications);
    }

    [Fact]
    public void The_address_uses_the_company_url_token_never_its_name_DT189()
    {
        var note = Note("n1", "einvoice-submissions/icrjEec9j2vCOI5AYzzjHw") with { Company = "It0sBQ1yFOw1sYUHoaTdpQ" };
        Assert.Equal("/c/It0sBQ1yFOw1sYUHoaTdpQ/einvoice-submissions/icrjEec9j2vCOI5AYzzjHw", NotificationsPanel.Href(note, "c1"));
        Assert.Equal("/tenant/subscription", NotificationsPanel.Href(note with { Link = "/tenant/subscription", Company = null }, "c1"));
        Assert.Equal("/c/c1/settings/users", NotificationsPanel.Href(note with { Link = "settings/users", Company = null }, "c1"));
        Assert.Null(NotificationsPanel.Href(note with { Link = null }, "c1"));
    }

    [Fact]
    public void A_record_that_no_longer_exists_gives_a_message_instead_of_a_broken_page_DT189()
    {
        var cut = Panel(Note("n1", null, "missing"));
        var start = Nav.Uri;
        cut.WaitForElement(".note-open");
        Act(() => cut.Find(".note-open").Click());
        cut.WaitForAssertion(() => Assert.Equal(L["notif.missing"], cut.Find(".note-p").TextContent));
        Assert.Equal(start, Nav.Uri);
        Assert.Empty(_openChanges);
        Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "notifications/n1/read");
    }

    [Fact]
    public void Mark_all_as_read_clears_the_unread_marks_DT189()
    {
        var cut = Panel(Note("n1", "invoices/inv1"), Note("n2", "invoices/inv2"), Note("n3", "invoices/inv3", readAt: At));
        cut.WaitForElement(".read-all");
        Assert.Equal(2, cut.FindAll(".note.unread").Count);
        Act(() => cut.Find(".read-all").Click());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note.unread")));
        Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "notifications/read-all");
        Assert.Equal(0, Shell.UnreadNotifications);
        Assert.True(cut.Find(".read-all").HasAttribute("disabled"));
    }

    [Fact]
    public void Each_notification_is_one_readable_block_with_details_on_request_DT189()
    {
        var cut = Panel(Note("n1", "invoices/inv1", details: "JoFotara refused the credentials (HTTP 401). CORE-401-001"));
        var note = cut.WaitForElement("li.note");
        // Title, message and date stacked inside one button across the panel's width.
        Assert.Equal(["note-t", "note-m", "note-w"], note.QuerySelector(".note-open")!.Children.Select(c => c.ClassName).ToArray());
        Assert.Contains("INV-00007", note.QuerySelector(".note-m")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(note.QuerySelector(".note-m bdi"));   // the code is isolated inside the Arabic sentence
        Assert.DoesNotContain("HTTP 401", note.TextContent, StringComparison.Ordinal);
        Act(() => cut.Find(".note-dt").Click());
        var details = cut.Find(".note-x");
        Assert.Equal("ltr", details.GetAttribute("dir"));
        Assert.Contains("CORE-401-001", details.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find(".note-dt").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void The_panel_styles_never_scroll_sideways_DT189()
    {
        // Long codes wrap inside the block; the button is full width and wraps (the browser check measures the real panel).
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Shared", "DirectService.Shared.UI", "wwwroot", "css", "app.css"));
        Assert.Matches(@"\.notes > \.note \{[^}]*overflow-wrap: anywhere", css);
        Assert.Matches(@"\.notes > \.note \{[^}]*min-width: 0", css);
        Assert.Matches(@"\.note-open \{[^}]*width: 100%[^}]*white-space: normal", css);
        Assert.Matches(@"\.note-x \{[^}]*white-space: pre-wrap", css);
    }
}
