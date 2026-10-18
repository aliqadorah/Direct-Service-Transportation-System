using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B1 unit tests: preset roles (PRD 3.5), numbering format (BR-037), password policy (FR-175), text rules, error catalogue.</summary>
public sealed class FoundationUnitTests
{
    [Fact]
    public void There_are_22_permission_keys_namespaced_per_D01()
    {
        Assert.Equal(26, PermissionKeys.All.Count);   // 22 of PRD 3.4 + 3 budget keys + acct.postentry (DT-201)
        Assert.Equal(26, PermissionKeys.All.Distinct().Count());
        Assert.All(PermissionKeys.All, k => Assert.Matches("^(trn|acc|core)\\.[a-z]+\\.[a-z]+$", k));
        Assert.Equal("trips.view", PermissionKeys.PrdName(PermissionKeys.TripsView));
        Assert.Equal("acct.maint", PermissionKeys.PrdName(PermissionKeys.AcctMaint));
        Assert.Equal("tenant.manage", PermissionKeys.PrdName(PermissionKeys.TenantManage));
    }

    [Fact]
    public void Preset_roles_match_PRD_3_5()
    {
        string[] Prd(params string[] names) => names.Select(n => PermissionKeys.All.Single(k => PermissionKeys.PrdName(k) == n)).Order().ToArray();

        Assert.Equal(PermissionKeys.All.Order(), PresetRoles.Permissions[PresetRoles.Owner].Order());
        Assert.Equal(PermissionKeys.All.Where(k => k != PermissionKeys.TenantManage).Order(), PresetRoles.Permissions[PresetRoles.Administrator].Order());
        Assert.Contains(PermissionKeys.AcctMaint, PresetRoles.Permissions[PresetRoles.Administrator]);
        Assert.Equal(Prd("trips.view", "inv.view", "inv.issue", "inv.void", "rcpt.manage", "drv.view", "drv.advance", "drv.settle",
            "je.view", "je.export", "acct.view", "acct.post", "acct.apar", "reports.view", "budget.view", "budget.manage", "acct.postentry"), PresetRoles.Permissions[PresetRoles.Accountant].Order());   // + budgets (revision 6 part B), posting (DT-201)
        Assert.Equal(Prd("trips.view", "trips.edit", "trips.void", "drv.view", "masters.manage", "reports.view"), PresetRoles.Permissions[PresetRoles.Dispatcher].Order());
        Assert.Equal(Prd("trips.view", "inv.view", "drv.view", "je.view", "acct.view", "reports.view", "budget.view"), PresetRoles.Permissions[PresetRoles.Viewer].Order());
    }

    [Theory]
    [InlineData("INV-", 5, 45, "INV-00045")]
    [InlineData("STL-", 5, 1, "STL-00001")]
    [InlineData("", 3, 1234, "1234")]
    [InlineData("JV-", 10, 7, "JV-0000000007")]
    public void Document_numbers_are_prefix_plus_zero_padded_counter(string prefix, byte digits, long n, string expected) =>
        Assert.Equal(expected, DocumentTypes.Format(prefix, digits, n));

    [Fact]
    public void Approved_number_prefixes_D16()
    {
        Assert.Equal(12, DocumentTypes.DefaultPrefixes.Count);
        Assert.Equal(["ADV-", "ARD-", "BIL-", "CRN-", "INV-", "JEX-", "JV-", "PAY-", "PPY-", "RCV-", "RTN-", "STL-"],
            DocumentTypes.DefaultPrefixes.Values.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("abcdef1", false)]        // 8 chars needed
    [InlineData("abcdefg1", true)]        // exactly 8
    [InlineData("abcdefgh", false)]       // digit needed
    [InlineData("12345678", false)]       // letter needed
    [InlineData("abcdefg1x", true)]
    [InlineData("كلمةسر12", true)]        // Arabic letters count as letters
    [InlineData("abcdefg١", true)]        // Arabic-Indic digits count as digits
    public void Default_password_policy_FR175(string password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.Default.IsSatisfiedBy(password));

    [Fact]
    public void Tenant_password_policy_can_drop_rules_but_not_length()
    {
        var p = new PasswordPolicy(12, false, false);
        Assert.False(p.IsSatisfiedBy("abcdefghijk"));
        Assert.True(p.IsSatisfiedBy("abcdefghijkl"));
    }

    [Theory]
    [InlineData("١٢٣-ب٤", "123-ب4")]
    [InlineData("۰۱۲", "012")]
    [InlineData("abc", "abc")]
    public void Arabic_indic_digits_become_ascii(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.ToAsciiDigits(input));

    [Theory]
    [InlineData(" ab 12 ", "AB12")]
    [InlineData("tr-١٢", "TR-12")]
    public void Uniqueness_key_ignores_case_and_spaces(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Key(input));

    [Fact]
    public void Every_error_code_has_arabic_and_english_messages()
    {
        var codes = typeof(ErrorCodes).GetFields().Select(f => (string)f.GetValue(null)!).ToList();
        Assert.NotEmpty(codes);
        foreach (var code in codes)
        {
            Assert.True(ErrorCatalog.IsKnown(code), $"{code} has no message");
            Assert.NotEqual(ErrorCatalog.Format(code, "ar", "x", "y"), ErrorCatalog.Format(code, "en", "x", "y"));
        }
        Assert.Equal("Field «Phone» is required.", ErrorCatalog.Format(ErrorCodes.ValidationRequired, "en", "Phone"));
    }

    /// <summary>Security review 13 Oct 2026: names typed by users cannot add markup to e-mails; the text part shows them as typed.</summary>
    [Fact]
    public void Email_templates_encode_names_in_html_and_keep_them_readable_in_text()
    {
        var m = Notifications.EmailTemplates.StaffInvitation("x@example.test", "<a href=\"https://evil.test\">Click</a>", "Rana & Co", 48,
            "https://app.test/accept-staff-invitation?u=a&t=b");
        Assert.DoesNotContain("<a href=\"https://evil.test\">", m.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;a href=", m.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Rana &amp; Co", m.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Rana & Co", m.TextBody, StringComparison.Ordinal);
        Assert.Contains("48", m.TextBody, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://evil.test\">Click</a>", Notifications.EmailTemplates.Invitation("x@example.test", "<a href=\"https://evil.test\">Click</a>", "R", 7, "https://app.test/x").TextBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<a href=\"https://evil.test\">", Notifications.EmailTemplates.VerifyEmail("x@example.test", "<a href=\"https://evil.test\">", "https://app.test/v").HtmlBody,
            StringComparison.Ordinal);
    }

    /// <summary>Owner feedback 13 Oct 2026: every import fix exists in Arabic and English; a code without its own fix gets the general one.</summary>
    [Fact]
    public void Every_import_fix_has_both_languages_and_unknown_codes_get_the_default()
    {
        foreach (var (code, (ar, en)) in ImportFixCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ar), code);
            Assert.False(string.IsNullOrWhiteSpace(en), code);
            Assert.NotEqual(ar, en);
            Assert.True(code.StartsWith("fix.", StringComparison.Ordinal) || ErrorCatalog.IsKnown(code), $"{code} is not an error code");
        }
        Assert.Contains("البيانات الأساسية", ImportFixCatalog.For(ErrorCodes.ImportUnknown, "ar"), StringComparison.Ordinal);
        Assert.Contains("master data", ImportFixCatalog.For(ErrorCodes.ImportUnknown, "en"), StringComparison.Ordinal);
        Assert.Equal(ImportFixCatalog.For(ImportFixCatalog.Default, "en"), ImportFixCatalog.For("some.other", "en"));
    }

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("name.surname@company.jo", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("a b@c.d", false)]
    [InlineData("a@b", false)]
    public void Email_format(string value, bool ok) => Assert.Equal(ok, AccountService.IsEmail(value));

    [Fact]
    public void Market_patterns_for_Jordan()
    {
        var phone = new System.Text.RegularExpressions.Regex(SettingKeys.JordanPhonePattern);
        Assert.Matches(phone, "0791234567");
        Assert.Matches(phone, "0771234567");
        Assert.Matches(phone, "061234567");
        Assert.DoesNotMatch(phone, "0761234567");
        Assert.DoesNotMatch(phone, "079123456");
        var iban = new System.Text.RegularExpressions.Regex(SettingKeys.JordanIbanPattern);
        Assert.Matches(iban, "JO94CBJO0010000000000131000302");
        Assert.Equal(30, "JO94CBJO0010000000000131000302".Length);
        Assert.DoesNotMatch(iban, "JO94CBJO001000000000013100030");
    }

    [Fact]
    public void Printouts_embed_the_brand_fonts_without_any_network_request()
    {
        // Open issue B6: the fonts are bundled; the PDF engine loads only data: resources.
        var css = BrandFonts.Css;
        Assert.Equal(9, css.Split("@font-face").Length - 1);
        Assert.Contains("font-family:'IBM Plex Sans Arabic'", css, StringComparison.Ordinal);
        Assert.Contains("font-family:'Inter'", css, StringComparison.Ordinal);
        Assert.DoesNotContain("url(http", css, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(9, css.Split("data:font/woff2;base64,").Length - 1);
    }
}
