using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Errors;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// DT-241: messages never show an internal field name. Every field name the services put into a field error or a message argument has an
/// Arabic and English label in <see cref="FieldLabels"/>, and <see cref="ErrorCatalog.Format"/> shows the label instead of the name.
/// DT-245: only a <see cref="FieldRef"/> argument is labelled — a value that equals a field name is shown as it is.
/// </summary>
public sealed partial class FieldLabelTests
{
    /// <summary>An internal name in a message: camelCase ("entryIds"), a list item ("lines[2]") or a dotted path ("party.type").</summary>
    [GeneratedRegex(@"\b[a-z]+[A-Z][A-Za-z0-9]*\b|\b[A-Za-z]+\[\d+\]|\b[a-z]+\.[a-z][A-Za-z]*\b")]
    internal static partial Regex RawName();

    // Field names passed to the validation helpers: Fail.Field / Fail.RuleOn / new FieldError / Validator.Require, Check, MaxLength.
    [GeneratedRegex(@"(?:Fail\.Field|Fail\.RuleOn|new FieldError|\.Require|\.MaxLength)\(\s*\$?""(?<f>[^""]+)""|\.Check\([^;]*?,\s*\$?""(?<f>[^""]+)""\s*[,)]")]
    private static partial Regex FieldLiteral();

    // Interpolated or concatenated paths: $"lines[{i}].accountId", field + ".accountId" (field = $"lines[{i}]").
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Hole();

    // The server code that names fields; the Shared projects hold the engines moved out of Application (Stage 2 session 6, DT-311).
    private static readonly string[] ServerProjects =
    [
        "DirectService.Transportation.Application", "DirectService.Transportation.Web",
        Path.Combine("Shared", "DirectService.SharedKernel"), Path.Combine("Shared", "DirectService.Shared.Infrastructure"),
    ];

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src");
    }

    public static TheoryData<string> UsedFields()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var project in ServerProjects)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                foreach (Match m in FieldLiteral().Matches(File.ReadAllText(file)))
                {
                    if (m.Groups["f"].Value.StartsWith('{')) continue;   // $"{field}[{i}]": the names passed in are checked at their callers
                    var f = Hole().Replace(m.Groups["f"].Value, "0");
                    // A suffix appended to a list item built elsewhere (field + ".accountId"): checked as "lines[0].accountId".
                    found.Add(f.StartsWith('.') ? "lines[0]" + f : f);
                }
            }
        }
        // Suffixes appended to a variable (f + ".description", field + ".type", at + ".values").
        foreach (var suffix in new[] { "description", "amount", "taxRate", "deduction", "accountId", "debit", "credit", "costCentreId", "note", "months", "party", "type", "id", "invoiceId", "values", "operator" })
            found.Add("lines[0]." + suffix);
        var data = new TheoryData<string>();
        foreach (var f in found.Where(f => !f.StartsWith("customValues.", StringComparison.Ordinal))) data.Add(f);
        return data;
    }

    [Theory]
    [MemberData(nameof(UsedFields))]
    public void Every_field_the_services_name_has_a_label_in_both_languages(string field)
    {
        Assert.True(FieldLabels.TryLabel(field, "ar", out var ar), $"no label for «{field}» — add it to FieldLabels");
        Assert.True(FieldLabels.TryLabel(field, "en", out var en));
        Assert.DoesNotMatch(RawName(), ar);
        Assert.DoesNotMatch(RawName(), en);
    }

    [Fact]
    public void The_scan_finds_the_fields_of_the_services() =>
        Assert.True(UsedFields().Count > 150, "the source scan found too few field names — check the pattern");

    [Theory]
    [InlineData("ar", "entryIds", "الحقل «القيود المختارة» مطلوب.")]
    [InlineData("en", "entryIds", "Field «Selected entries» is required.")]
    [InlineData("ar", "lines[2].accountId", "الحقل «السطر 3 · الحساب» مطلوب.")]
    [InlineData("en", "lines[2].accountId", "Field «Line 3 · Account» is required.")]
    [InlineData("en", "deductions[0].note", "Field «Deduction 1 · Note» is required.")]
    public void A_required_field_is_named_by_its_label(string language, string field, string message) =>
        Assert.Equal(message, ErrorCatalog.Format(ErrorCodes.ValidationRequired, language, new FieldRef(field)));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void No_field_message_shows_a_known_name_in_any_code(string language)
    {
        foreach (var code in new[] { ErrorCodes.ValidationRequired, ErrorCodes.ValidationFormat, ErrorCodes.ValidationRange, ErrorCodes.DateFuture, ErrorCodes.ImportDuplicate, ErrorCodes.FieldUnknown })
        foreach (var name in FieldLabels.Known)
            Assert.DoesNotMatch(RawName(), ErrorCatalog.Format(code, language, new FieldRef(name)));
    }

    [Fact]
    public void Party_type_codes_and_ordinary_values_are_handled()
    {
        Assert.Equal("«Accounting party» is archived and cannot be chosen on new entries.", ErrorCatalog.Format(ErrorCodes.RecordArchived, "en", new FieldRef("ApArParty")));
        // Names, numbers and amounts that are not field names are kept as they are.
        Assert.Equal("«Al Noor Transport» is archived and cannot be chosen on new entries.", ErrorCatalog.Format(ErrorCodes.RecordArchived, "en", "Al Noor Transport"));
        Assert.Contains("12.500", ErrorCatalog.Format(ErrorCodes.ImportDuplicate, "en", "12.500"), StringComparison.Ordinal);
        Assert.False(FieldLabels.TryLabel("notAField", "en", out _));
        Assert.False(FieldLabels.TryLabel("lines[x].accountId", "en", out _));
    }

    [Theory]
    [InlineData("ar", "«amount» مؤرشف ولا يمكن اختياره في عمليات جديدة.")]
    [InlineData("en", "«amount» is archived and cannot be chosen on new entries.")]
    public void A_value_that_equals_a_field_name_is_shown_as_it_is(string language, string message)
    {
        // A cash box, customer or account named "amount": a value, not the field — never replaced by the label "Amount".
        Assert.Equal(message, ErrorCatalog.Format(ErrorCodes.RecordArchived, language, "amount"));
        Assert.Contains("“name”", ErrorCatalog.Format(ErrorCodes.TemplateNameTaken, "en", "name"), StringComparison.Ordinal);
        Assert.Contains("«lines[2].accountId»", ErrorCatalog.Format(ErrorCodes.ValidationRequired, language, "lines[2].accountId"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_reference_and_a_value_with_the_same_text_are_told_apart()
    {
        var label = ErrorCatalog.Format(ErrorCodes.ApArPartyRole, "en", "Customer", new FieldRef("Supplier"));
        Assert.Equal("Party «Customer» is not a Supplier.", label);
        Assert.Equal("الطرف «Customer» ليس المورد.", ErrorCatalog.Format(ErrorCodes.ApArPartyRole, "ar", "Customer", new FieldRef("Supplier")));
        // An unknown name in a FieldRef is shown as it is (FieldUnknown names what the user sent).
        Assert.Contains("«noSuchField»", ErrorCatalog.Format(ErrorCodes.ValidationRequired, "en", new FieldRef("noSuchField")), StringComparison.Ordinal);
    }

    /// <summary>A call that names its own field in its message arguments: Fail.Field("name", code, "name") — the field must be a FieldRef.</summary>
    [GeneratedRegex(@"(?:Fail\.Field|Fail\.RuleOn|new FieldError)\(\s*(?<f>\$?""[^""]+""|[A-Za-z_][\w.]*(?:\s*\+\s*""[^""]*"")?)\s*,\s*[^,;]+?,\s*\[?\s*\k<f>\s*\]?\s*[,)]")]
    private static partial Regex PlainFieldArgument();

    [Fact]
    public void No_service_passes_its_field_name_as_a_plain_string_argument()
    {
        var found = new List<string>();
        foreach (var project in ServerProjects)
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                foreach (Match m in PlainFieldArgument().Matches(File.ReadAllText(file))) found.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        Assert.True(found.Count == 0, "pass the field as new FieldRef(…):\n" + string.Join("\n", found));
    }
}
