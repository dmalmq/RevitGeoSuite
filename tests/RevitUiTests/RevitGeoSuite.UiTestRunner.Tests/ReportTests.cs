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
        Assert.Contains("| B1.8 | Export | known product failure |  |", text);
        Assert.Contains("KNOWN PRODUCT FAILURE until B3b: warns about stairs", text);

        known.Check(false, "something else");
        Assert.Equal(Outcome.Fail, known.Outcome);
    }

    [Fact]
    public void Each_step_shows_its_seconds()
    {
        var report = new Report(Directory.CreateTempSubdirectory().FullName);
        report.Steps.Add(new StepResult("C3a.2", "Apply") { Seconds = 41.6 });

        Assert.Contains("| C3a.2 | Apply | pass | 42 |", File.ReadAllText(report.Write()));
    }

    [Fact]
    public void A_suite_summary_lists_every_scenario_with_its_minutes_and_fails_when_one_fails()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        var passed = new Report(Path.Combine(folder, "b1"));
        passed.Steps.Add(new StepResult("B1.1", "Set up"));
        var failed = new Report(Path.Combine(folder, "c3a-reshape"));
        var step = new StepResult("C3a.1", "Reshape");
        step.Check(false, "the floor follows the GDB outline");
        failed.Steps.Add(step);

        string text = File.ReadAllText(Suite.Write(folder, new[] { "Revit 2024" }, new StepResult("START", "up"),
            new[] { ("b1", passed, TimeSpan.FromSeconds(90)), ("c3a-reshape", failed, TimeSpan.FromMinutes(2)) }));

        Assert.Contains("**FAIL**: 1 scenario(s) passed, 1 failed.", text);
        Assert.Contains("| b1 | pass | 1 | 0 | 0 | 1.5 | [b1/report.md](b1/report.md) |", text);
        Assert.Contains("| c3a-reshape | **FAIL** | 0 | 1 | 0 | 2.0 | [c3a-reshape/report.md](c3a-reshape/report.md) |", text);
        Assert.Contains("2 scenario(s) in one Revit, 3.5 min in all.", text);
    }
}

public class ExportStoreTests
{
    [Fact]
    public void Refuses_a_settings_file_that_Run_UiTests_did_not_back_up()
    {
        string settings = Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.json");
        File.WriteAllText(settings, "{\"outputDirectory\":\"D:\\\\work\"}");

        Assert.Throws<ArgumentException>(() => new ExportStore(settings));
        Assert.Equal("{\"outputDirectory\":\"D:\\\\work\"}", File.ReadAllText(settings));
    }

    [Fact]
    public void Reset_empties_settings_and_puts_profiles_back_as_the_run_found_them()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        string settings = Path.Combine(folder, "settings.json");
        string profiles = Path.Combine(folder, "profiles.json");
        File.WriteAllText(settings + ".uitest-backup", "{\"real\":1}");
        File.WriteAllText(settings, "{}");
        File.WriteAllText(profiles, "{\"pc\":[]}");
        var store = new ExportStore(settings);

        File.WriteAllText(settings, "{\"outputDirectory\":\"C:\\\\dev\\\\revit-test\\\\auto\\\\1\\\\b1\"}");
        File.WriteAllText(profiles, "{\"project\":[\"UI test\"]}");
        store.Reset();

        Assert.Equal("{}", File.ReadAllText(settings));
        Assert.Equal("{\"pc\":[]}", File.ReadAllText(profiles));
        Assert.Equal("{\"real\":1}", File.ReadAllText(settings + ".uitest-backup"));
    }

    [Fact]
    public void Reset_removes_a_profiles_file_an_earlier_scenario_made()
    {
        string folder = Directory.CreateTempSubdirectory().FullName;
        string settings = Path.Combine(folder, "settings.json");
        File.WriteAllText(settings + ".uitest-absent", string.Empty);
        File.WriteAllText(settings, "{}");
        var store = new ExportStore(settings);

        File.WriteAllText(Path.Combine(folder, "profiles.json"), "{}");
        store.Reset();

        Assert.False(File.Exists(Path.Combine(folder, "profiles.json")));
    }
}
