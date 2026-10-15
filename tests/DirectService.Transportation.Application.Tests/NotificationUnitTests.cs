using DirectService.Transportation.Application.Collaboration;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// Notification texts (FR-190, DT-189): stored as type + parameters and built in the reader's language when shown; older stored texts
/// (bilingual JSON or plain text) are read back and translated too; technical codes stay in the details.
/// </summary>
public sealed class NotificationUnitTests
{
    private static Dictionary<string, string?> P(params (string Key, string? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    [Fact]
    public void Long_parameters_fit_500_characters_and_Arabic_stays_readable_FR190()
    {
        var m = NotificationService.Message(P(("number", "INV-00007"), ("reason", new string('ع', 700) + new string('e', 700))));
        Assert.True(m.Length <= NotificationService.MaxMessage);
        Assert.Contains("عع", m, StringComparison.Ordinal);   // not \u-escaped
        Assert.Contains("INV-00007", m, StringComparison.Ordinal);       // short values kept whole
        Assert.Equal("{\"p\":{\"file\":\"تقرير.xlsx\"}}", NotificationService.Message(P(("file", "تقرير.xlsx"))));
    }

    /// <summary>Parameters each type is created with (as the services pass them).</summary>
    public static TheoryData<string, string> Types() => new()
    {
        { NotificationService.EInvoiceRejected, NotificationService.Message(P(("number", "INV-00007"), ("reason", "E-INV-BUYER: buyer data refused."))) },
        { NotificationService.EInvoiceSetupError, NotificationService.Message(P(("reason", "JoFotara refused the credentials (HTTP 401). CORE-401-001: Invalid client credentials"))) },
        { NotificationService.PostingFailed, NotificationService.Message(P(("description", "INV-00007"))) },
        { NotificationService.ExportReady, NotificationService.Message(P(("file", "report.xlsx"))) },
        { NotificationService.ExportFailed, NotificationService.Message(P()) },
        { NotificationService.InvitationAccepted, NotificationService.Message(P(("name", "Sara Ali"))) },
        { NotificationService.SupportRequested, NotificationService.Message(P(("reason", "Ticket 42"))) },
        { NotificationService.PaymentFailed, NotificationService.Message(P(("days", "7"))) },
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_type_reads_in_Arabic_and_English_DT189(string type, string stored)
    {
        var ar = NotificationTexts.Render(type, stored, "ar", new Dictionary<string, string> { ["count"] = "3" });
        var en = NotificationTexts.Render(type, stored, "en", new Dictionary<string, string> { ["count"] = "3" });
        Assert.Matches("[؀-ۿ]", ar.Title);
        Assert.Matches("[؀-ۿ]", ar.Message);
        Assert.DoesNotMatch("[؀-ۿ]", en.Title + en.Message);
        Assert.DoesNotContain("{", ar.Message + en.Message, StringComparison.Ordinal);   // every placeholder filled
        Assert.DoesNotContain("HTTP", ar.Message + en.Message, StringComparison.Ordinal); // technical codes only in the details
        Assert.DoesNotContain("CORE-401", ar.Message + en.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_type_has_words_DT189() =>
        Assert.Equal(Types().Select(t => t.Data.Item1).Order(StringComparer.Ordinal), NotificationTexts.Types.Order(StringComparer.Ordinal));

    [Fact]
    public void Old_bilingual_rejection_is_translated_and_codes_hidden_in_details_DT189()
    {
        // As stored by the earlier version: English shown on Arabic screens because the text was fixed at creation.
        const string old = "{\"Ar\":\"رُفضت فاتورة إلكترونية: INV-00007 — E-INV-TOTALS: Simulated rejection: totals do not add up.\","
            + "\"En\":\"An e-invoice was rejected: INV-00007 — E-INV-TOTALS: Simulated rejection: totals do not add up.\"}";
        var ar = NotificationTexts.Render(NotificationService.EInvoiceRejected, old, "ar");
        Assert.Equal("رُفضت فاتورة إلكترونية", ar.Title);
        Assert.Equal("رفض نظام الفوترة الوطني المستند INV-00007. افتحه لتصحيحه وإعادة إرساله.", ar.Message);
        Assert.Equal("E-INV-TOTALS: Simulated rejection: totals do not add up.", ar.Details);
        var en = NotificationTexts.Render(NotificationService.EInvoiceRejected, old, "en");
        Assert.Equal("The national e-invoicing system refused INV-00007. Open it to correct and resubmit.", en.Message);
    }

    [Theory]
    [InlineData(NotificationService.ExportReady, "{\"Ar\":\"التصدير جاهز: a.xlsx\",\"En\":\"Export ready: a.xlsx\"}", "الملف a.xlsx جاهز للتنزيل.")]
    [InlineData(NotificationService.PostingFailed, "{\"Ar\":\"x\",\"En\":\"An entry could not be posted and waits in pending postings: Trip 12\"}", "ينتظر القيد في الترحيلات المعلّقة: Trip 12")]
    [InlineData(NotificationService.SupportRequested, "{\"Ar\":\"x\",\"En\":\"Provider support requests access to your data: Ticket 9\"}", "يطلب الدعم الفني لدى مزوّد الخدمة الوصول إلى بياناتكم: Ticket 9")]
    [InlineData(NotificationService.PaymentFailed, "{\"Ar\":\"x\",\"En\":\"The renewal payment failed; you have 7 days to update the payment method.\"}", "تعذّر تحصيل قيمة التجديد؛ لديكم 7 أيام لتحديث وسيلة الدفع.")]
    [InlineData(NotificationService.ExportFailed, "{\"Ar\":\"x\",\"En\":\"The export could not be prepared.\"}", "تعذّر إعداد التصدير. أعد المحاولة أو ضيّق الفترة.")]
    [InlineData(NotificationService.InvitationAccepted, "Sara Ali قبل الدعوة / accepted the invitation", "قبل Sara Ali الدعوة وانضم إلى الشركة.")]
    public void Old_stored_texts_of_every_type_are_rebuilt_in_the_readers_language_DT189(string type, string stored, string arabic)
    {
        Assert.Equal(arabic, NotificationTexts.Render(type, stored, "ar").Message);
        Assert.DoesNotMatch("[؀-ۿ]", NotificationTexts.Render(type, stored, "en").Message);
    }

    [Fact]
    public void Unknown_wording_falls_back_to_the_stored_language_DT189()
    {
        var t = NotificationTexts.Render("X.Unknown", "{\"Ar\":\"نص\",\"En\":\"Text\"}", "en");
        Assert.Equal("Text", t.Message);
        Assert.Equal("Notification", t.Title);
        Assert.Equal("نص", NotificationTexts.Render("X.Unknown", "{\"Ar\":\"نص\",\"En\":\"Text\"}", "ar").Message);
    }
}
