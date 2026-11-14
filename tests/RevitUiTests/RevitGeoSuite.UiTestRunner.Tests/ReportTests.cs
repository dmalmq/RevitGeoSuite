using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class ReportTests
{
    [Fact]
    public void A_known_product_failure_does_not_fail_the_run_but_another_failed_check_does()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        var report = new Report(folder);
        var known = new StepResult("B1.8", "Export");
        known.CheckKnown(false, "warns about stairs", "B3b");
        report.Steps.Add(known);

        string text = File.ReadAllText(report.Write());
        Assert.Contains("**PASS**: 0 passed, 0 failed, 1 known product failure(s), 0 not run.", text);
        Assert.Contains("| B1.8 | Export | known product failure |", text);
        Assert.Contains("KNOWN PRODUCT FAILURE until B3b: warns about stairs", text);

        known.Check(false, "something else");
        Assert.Equal(Outcome.Fail, known.Outcome);
    }
}
