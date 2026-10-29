using DirectService.Shared.Infrastructure.Reports;
using DirectService.Transportation.Application.Reports;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B10 rules without a database: pin states (FR-140) and the report-builder catalogue (FR-143).</summary>
public sealed class ReportUnitTests
{
    [Theory]
    [InlineData(null, 10, 20, "normal")]
    [InlineData(5, 10, 20, "normal")]
    [InlineData(10, 10, 20, "warning")]
    [InlineData(20, 10, 20, "exceeded")]
    [InlineData(25, null, 20, "exceeded")]
    [InlineData(25, null, null, "normal")]
    public void Pin_state_from_thresholds_FR140(int? value, int? warning, int? critical, string state) =>
        Assert.Equal(state, SavedReportService.State(value, warning, critical));

    [Fact]
    public void Ten_sources_with_a_main_date_or_none_FR143()
    {
        Assert.Equal(["trips", "invoices", "credit-notes", "receipts", "advances", "settlements", "partner-payments", "journal-lines", "drivers", "customers"],
            ReportSources.All.Select(s => s.Key).ToArray());
        Assert.All(ReportSources.All.Where(s => s.DateField is not null), s => Assert.Contains(s.Fields, f => f.Key == s.DateField && f.Type == FieldTypes.Date));
        Assert.All(ReportSources.All, s => Assert.Equal(s.Fields.Count, s.Fields.Select(f => f.Key).Distinct().Count()));
    }
}
