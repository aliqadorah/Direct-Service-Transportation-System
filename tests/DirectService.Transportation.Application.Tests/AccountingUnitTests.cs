using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B2 rules that need no database: amount in words (BR-024), the approved chart template (D-10), posting keys
/// (7.5.1, G-04), control-party matching (G-17) and money rounding (BR-020/021).</summary>
public sealed class AccountingUnitTests
{
    [Theory]
    [InlineData("817.800", "فقط ثمانمائة وسبعة عشر ديناراً وثمانمائة فلس لا غير")] // EX-2 invoice total
    [InlineData("191.100", "فقط مائة وواحد وتسعون ديناراً ومائة فلس لا غير")]      // EX-1 trip revenue
    [InlineData("815", "فقط ثمانمائة وخمسة عشر ديناراً لا غير")]                    // EX-4 settlement net
    [InlineData("1", "فقط دينار واحد لا غير")]
    [InlineData("2.500", "فقط ديناران وخمسمائة فلس لا غير")]
    [InlineData("3", "فقط ثلاثة دنانير لا غير")]
    [InlineData("100", "فقط مائة دينار لا غير")]
    [InlineData("1250", "فقط ألف ومائتان وخمسون ديناراً لا غير")]
    [InlineData("3000", "فقط ثلاثة آلاف دينار لا غير")]
    [InlineData("15000", "فقط خمسة عشر ألف دينار لا غير")]
    [InlineData("2000000", "فقط مليونا دينار لا غير")]
    [InlineData("0.250", "فقط مائتان وخمسون فلساً لا غير")]
    [InlineData("0", "فقط صفر دينار لا غير")]
    public void Amount_in_arabic_words_uses_dinar_and_fils(string amount, string expected) =>
        Assert.Equal(expected, AmountInWords.Arabic(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("817.800", "Eight hundred seventeen Jordanian dinars and 800 fils only")]
    [InlineData("1", "One Jordanian dinar only")]
    [InlineData("1250.050", "One thousand two hundred fifty Jordanian dinars and 50 fils only")]
    public void Amount_in_english_words(string amount, string expected) =>
        Assert.Equal(expected, AmountInWords.English(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Chart_template_is_a_valid_tree()
    {
        var codes = ChartTemplate.Accounts.Select(a => a.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal(49, codes.Count);   // revision 6: + 4105 Supplier deductions income; DT-256: + 5207 VAT expenses
        // DT-256: input VAT, output VAT and VAT expenses are the template's tax accounts, none of them a control account.
        Assert.Equal(["1105", "2104", "5207"], ChartTemplate.Accounts.Where(a => a.IsTax).Select(a => a.Code).Order(StringComparer.Ordinal));
        Assert.All(ChartTemplate.Accounts.Where(a => a.IsTax), a => Assert.Null(a.ControlPartyType));
        var seen = new Dictionary<string, ChartTemplate.TemplateAccount>();
        foreach (var a in ChartTemplate.Accounts)
        {
            Assert.True(ChartOfAccountsService.IsCode(a.Code), a.Code);
            Assert.Contains(a.Type, AccountingCodes.AccountTypes);
            Assert.False(string.IsNullOrWhiteSpace(a.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(a.NameEn));
            if (a.ParentCode is not null)
            {
                Assert.True(seen.ContainsKey(a.ParentCode), $"{a.Code}: parent {a.ParentCode} must come first");
                Assert.Equal(seen[a.ParentCode].Type, a.Type); // a child has its parent's type
            }
            if (a.ControlPartyType is not null)
            {
                Assert.Contains(a.ControlPartyType, AccountingCodes.ControlPartyTypes);
                Assert.True(ChartTemplate.IsLeaf(a.Code), $"{a.Code}: control accounts are postable leaves");
            }
            seen[a.Code] = a;
        }
    }

    [Fact]
    public void Template_maps_every_posting_key_to_a_leaf_account()
    {
        Assert.Equal(PostingKeys.All.Select(k => k.Key).Order(), ChartTemplate.Mapping.Keys.Order());
        foreach (var (key, code) in ChartTemplate.Mapping)
        {
            Assert.True(ChartTemplate.IsLeaf(code), $"{key} → {code} is not postable");
        }
        Assert.True(ChartTemplate.IsLeaf(ChartTemplate.MainCashBoxAccountCode));
        // Owner choices on the draft's points 1–3 (D-10)
        Assert.Equal("4104", ChartTemplate.Mapping[PostingKeys.DiscountAllowed]);
        Assert.Equal("4103", ChartTemplate.Mapping[PostingKeys.DriverDeductions]);
        Assert.Equal("Asset", ChartTemplate.Accounts.Single(a => a.Code == "1203").Type);
    }

    [Fact]
    public void Opening_difference_key_is_required_only_during_setup()
    {
        Assert.Equal(13, PostingKeys.Required(setupOpen: true).Count());
        Assert.Equal(12, PostingKeys.Required(setupOpen: false).Count());
        Assert.DoesNotContain(PostingKeys.OpeningBalanceDifference, PostingKeys.Required(setupOpen: false));
    }

    [Theory]
    [InlineData("Customer", "TRN", "Customer", null, true)]
    [InlineData("Customer", "ACC", "ApArParty", "Customer", true)]
    [InlineData("Customer", "ACC", "ApArParty", "Supplier", false)]
    [InlineData("Supplier", "ACC", "ApArParty", "Supplier", true)]
    [InlineData("Supplier", "TRN", "Customer", null, false)]
    [InlineData("Driver", "TRN", "Driver", null, true)]
    [InlineData("Driver", "TRN", "Partner", null, false)]
    [InlineData("Partner", "TRN", "Partner", null, true)]
    public void Control_party_matching_follows_G17(string controlType, string module, string partyType, string? role, bool expected)
    {
        var roles = new Dictionary<long, string>();
        if (role is not null) roles[7] = role;
        Assert.Equal(expected, PostingEngine.PartyMatches(controlType, new PostingParty(module, partyType, 7), roles));
    }

    [Theory]
    [InlineData("1.0005", "1.001")]
    [InlineData("1.0004", "1.000")]
    [InlineData("112.8", "112.800")]
    public void Money_rounds_half_up_to_fils(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Money.Round(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1.234", true)]
    [InlineData("1.2345", false)]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("999999999999999.999", true)]
    [InlineData("1000000000000000", false)]
    public void Amounts_have_at_most_the_currency_decimals(string input, bool valid) =>
        Assert.Equal(valid, Money.IsValidAmount(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2026-10", true)]
    [InlineData("2026-13", false)]
    [InlineData("2026-1", false)]
    [InlineData(null, false)]
    public void Period_month_format(string? month, bool valid) => Assert.Equal(valid, AccountingCodes.IsMonth(month));
}
