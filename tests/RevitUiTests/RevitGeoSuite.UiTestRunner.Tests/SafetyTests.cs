using RevitGeoSuite.TestHost;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class DialogPolicyTests
{
    [Fact]
    public void Answers_nothing_while_no_runner_is_connected() =>
        Assert.False(DialogPolicy.MayAnswer(armed: false, "TaskDialog_Kanizu", "Open a project model to check it."));

    [Fact]
    public void Answers_an_ordinary_message_during_a_run() =>
        Assert.True(DialogPolicy.MayAnswer(armed: true, "", "Open a project model to check it against the Kanizu GIS standard."));

    [Theory]
    [InlineData("TaskDialog_Save_File", "")]
    [InlineData("", "Do you want to save changes to LUMINE.rvt?")]
    [InlineData("", "The file already exists. Do you want to overwrite it?")]
    [InlineData("", "Replace the existing file?")]
    [InlineData("TaskDialog_Synchronize_With_Central", "")]
    [InlineData("", "This model is a central model. Detach it?")]
    [InlineData("", "Relinquish all elements and worksets?")]
    [InlineData("", "LUMINE.rvt への変更を保存しますか?")]
    [InlineData("", "ファイルを上書きしますか?")]
    [InlineData("", "中央モデルと同期しますか?")]
    public void Never_answers_a_save_overwrite_or_central_prompt(string dialogId, string message) =>
        Assert.False(DialogPolicy.MayAnswer(armed: true, dialogId, message));
}

public class TestCopyTests
{
    private const string Run = "/tmp/revit-test/auto/20261113-0100";
    private const string Copy = Run + "/model/lumine-pristine-uitest.rvt";

    [Fact]
    public void Accepts_the_runs_own_copy() => Assert.Null(TestCopy.Problem(Copy, Copy, Run, workshared: false, ""));

    [Fact]
    public void Accepts_a_workshared_copy_that_is_its_own_central_in_the_run_folder()
    {
        string detached = Run + "/model/lumine-pristine-uitest-detached.rvt";
        Assert.Null(TestCopy.Problem(detached, detached, Run, workshared: true, detached));
    }

    [Fact]
    public void Refuses_another_active_document() =>
        Assert.Contains("not this run's copy", TestCopy.Problem("/tmp/models/LUMINE_EST.rvt", Copy, Run, false, ""));

    [Fact]
    public void Refuses_a_detached_model_without_a_path() =>
        Assert.Contains("no path", TestCopy.Problem("", Copy, Run, true, ""));

    [Fact]
    public void Refuses_a_copy_still_tied_to_the_original_central() =>
        Assert.Contains("not detached", TestCopy.Problem(Copy, Copy, Run, workshared: true, @"//server/central/LUMINE_EST.rvt"));

    [Fact]
    public void Refuses_a_copy_outside_the_run_folder() =>
        Assert.Contains("not inside the run folder", TestCopy.Problem("/tmp/revit-test/other.rvt", "/tmp/revit-test/other.rvt", Run, false, ""));
}

public class TestPathsTests
{
    [Theory]
    [InlineData("/tmp/revit-test/run-1/export", "/tmp/revit-test/run-1", true)]
    [InlineData("/tmp/revit-test/run-1/", "/tmp/revit-test/run-1", true)]
    [InlineData("/tmp/revit-test/run-10/export", "/tmp/revit-test/run-1", false)]
    [InlineData("/tmp/revit-test/run-1/../lumine-count/before", "/tmp/revit-test/run-1", false)]
    [InlineData("/tmp/revit-test/lumine-count/before", "/tmp/revit-test/run-1", false)]
    public void Only_the_run_folder_is_writable(string path, string folder, bool expected) =>
        Assert.Equal(expected, TestPaths.IsUnder(path, folder));

    [Theory]
    [InlineData(@"C:\dev\revit-test\run\export", @"C:\dev\revit-test\run\export", true)]
    [InlineData(@"c:\dev\revit-test\run\export\ ", @"C:\dev\revit-test\run\export", true)]
    [InlineData(@"C:\dev\revit-test\lumine-count\before", @"C:\dev\revit-test\run\export", false)]
    [InlineData("", @"C:\dev\revit-test\run\export", false)]
    [InlineData(null, @"C:\dev\revit-test\run\export", false)]
    public void The_field_must_show_the_runs_folder(string? shown, string folder, bool expected) =>
        Assert.Equal(expected, TestPaths.Same(shown, folder));
}
