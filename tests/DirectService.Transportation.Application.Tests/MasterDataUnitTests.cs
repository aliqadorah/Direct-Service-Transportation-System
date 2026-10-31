using System.Text.Json;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B3 rules that need no database: the standard-field catalogue (FR-182, US-27), custom-field value parsing (FR-179, FR-180),
/// key normalisation with Arabic digits (DT-37) and document-expiry alerts (FR-154).</summary>
public sealed class MasterDataUnitTests
{
    [Fact]
    public void Field_catalogue_keys_are_unique_and_core_fields_are_always_required()
    {
        foreach (var entity in FieldCatalog.Entities)
        {
            var fields = FieldCatalog.For(entity);
            Assert.Equal(fields.Count, fields.Select(f => f.Key).Distinct().Count());
            Assert.All(fields.Where(f => f.IsCore), f => Assert.True(f.AlwaysRequired));
            Assert.All(fields, f => Assert.False(string.IsNullOrWhiteSpace(f.LabelAr) || string.IsNullOrWhiteSpace(f.LabelEn)));
        }
        // US-27: receipt number, date, driver, customer, sites, material, loaded weight, price, wage are core trip fields.
        Assert.Equal(
            ["receiptNo", "tripDate", "driverId", "customerId", "loadingSiteId", "unloadingSiteId", "materialId", "loadedWeight", "customerPrice", "driverRate"],
            FieldCatalog.For(FieldCatalog.Trip).Where(f => f.IsCore).Select(f => f.Key));
        Assert.False(FieldCatalog.IsEntity("CORE.User"));
    }

    private static CustomFieldDefinition Def(string type, params string[] options) => new()
    {
        FieldKey = "f",
        LabelAr = "حقل",
        FieldType = type,
        Options = options.Length == 0 ? null : JsonSerializer.Serialize(options),
    };

    [Theory]
    [InlineData(CustomFieldTypes.Number, "١٢٣٫٥", "123.5")]
    [InlineData(CustomFieldTypes.Number, "1,250", "1250")]
    [InlineData(CustomFieldTypes.Date, "08/10/2026", "2026-10-08")]
    [InlineData(CustomFieldTypes.Date, "٢٠٢٦-١٠-٠٨", "2026-10-08")]
    [InlineData(CustomFieldTypes.Checkbox, "نعم", "true")]
    [InlineData(CustomFieldTypes.Checkbox, "No", "false")]
    [InlineData(CustomFieldTypes.Text, "  two   words ", "two words")]
    public void Import_cells_parse_by_field_type(string type, string cell, string expected)
    {
        Assert.True(CustomFieldValues.TryParseCell(Def(type), cell, out var value));
        Assert.Equal(expected, CustomFieldValues.Text(value!.Value));
    }

    [Theory]
    [InlineData(CustomFieldTypes.Number, "abc")]
    [InlineData(CustomFieldTypes.Date, "31/02/2026")]
    [InlineData(CustomFieldTypes.Checkbox, "maybe")]
    [InlineData(CustomFieldTypes.List, "East")]
    public void Invalid_cells_are_rejected(string type, string cell) =>
        Assert.False(CustomFieldValues.TryParseCell(Def(type, "North", "South"), cell, out _));

    [Fact]
    public void List_values_must_be_one_of_the_options_and_empty_cells_give_no_value()
    {
        Assert.True(CustomFieldValues.TryParseCell(Def(CustomFieldTypes.List, "North", "South"), "South", out var v));
        Assert.Equal("South", v!.Value.GetString());
        Assert.True(CustomFieldValues.TryParseCell(Def(CustomFieldTypes.Number), "  ", out var empty));
        Assert.Null(empty);
        Assert.Empty(CustomFieldValues.Read("not json"));
    }

    [Theory]
    [InlineData("١٢-٣٤٥٦٧", "12-34567")]
    [InlineData(" 12 - 34567 ", "12-34567")]
    [InlineData("ab 123", "AB123")]
    [InlineData("  ", null)]
    public void Plates_are_normalised(string input, string? expected) => Assert.Equal(expected, MasterDataSupport.Plate(input));

    [Fact]
    public void Names_and_codes_are_normalised_with_ascii_digits()
    {
        Assert.Equal("Site 2", MasterDataSupport.Name("  Site   ٢ "));
        Assert.Equal("0791234567", MasterDataSupport.Compact("٠٧٩ ١٢٣-٤٥٦٧"));
        Assert.Null(MasterDataSupport.Name(" "));
        Assert.Equal("abc", MasterDataSupport.SearchText("  abc "));
        Assert.Equal(100, MasterDataSupport.SearchText(new string('x', 300))!.Length);
    }

    [Fact]
    public void Document_alerts_cover_expired_and_the_next_30_days_soonest_first()
    {
        var today = new DateOnly(2026, 10, 8);
        var d = new Driver
        {
            LicenceExpiry = today.AddDays(30),        // expiring (last day included)
            RegistrationExpiry = today.AddDays(-1),   // expired
            InsuranceExpiry = today,                  // expiring today, not yet expired
            InspectionExpiry = today.AddDays(31),     // no alert
        };
        var alerts = DriverService.Alerts(d, today);
        Assert.Equal(["registration", "insurance", "licence"], alerts.Select(a => a.Document));
        Assert.Equal([true, false, false], alerts.Select(a => a.Expired));
    }
}
