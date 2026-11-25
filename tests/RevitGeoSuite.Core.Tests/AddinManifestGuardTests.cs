using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class AddinManifestGuardTests
{
    [Fact]
    public void Addin_manifests_have_real_rfc4122_v4_guid_and_kanizu_revit_branding()
    {
        string shellAddinPath = TestPaths.GetRepoPath(Path.Combine("src", "RevitGeoSuite.Shell", "KanizuRevit.addin"));
        string templateAddinPath = TestPaths.GetRepoPath(Path.Combine("install", "KanizuRevit.addin.template"));

        Assert.True(File.Exists(shellAddinPath), "Expected src/RevitGeoSuite.Shell/KanizuRevit.addin to exist.");
        Assert.True(File.Exists(templateAddinPath), "Expected install/KanizuRevit.addin.template to exist.");
        Assert.False(File.Exists(TestPaths.GetRepoPath(Path.Combine("src", "RevitGeoSuite.Shell", "RevitGeoSuite.addin"))));
        Assert.False(File.Exists(TestPaths.GetRepoPath(Path.Combine("install", "RevitGeoSuite.addin.template"))));

        (string shellId, string shellName, string shellClass) = ReadManifest(shellAddinPath);
        (string templateId, string templateName, string templateClass) = ReadManifest(templateAddinPath);

        Assert.Equal("Kanizu Revit", shellName);
        Assert.Equal("Kanizu Revit", templateName);
        Assert.Equal("RevitGeoSuite.Shell.App", shellClass);
        Assert.Equal("RevitGeoSuite.Shell.App", templateClass);
        Assert.Equal(shellId, templateId);
        Assert.NotEqual("A1B2C3D4-E5F6-7890-ABCD-EF1234567890", shellId, StringComparer.OrdinalIgnoreCase);

        Guid parsed = Guid.Parse(shellId);
        byte[] bytes = parsed.ToByteArray();
        // RFC-4122 version 4: high nibble of octet 6 (byte[7] in .NET's little-endian layout) is 4.
        Assert.Equal(4, (bytes[7] >> 4) & 0x0F);
        // RFC-4122 variant: top two bits of octet 8 (byte[8]) are 10.
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    [Fact]
    public void Installer_scripts_cleanly_remove_legacy_revitgeosuite_addin()
    {
        string installScript = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "install.ps1")));
        string issScript = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "RevitGeoSuite.iss")));

        Assert.Contains("RevitGeoSuite.addin", installScript, StringComparison.Ordinal);
        Assert.Contains("KanizuRevit.addin", installScript, StringComparison.Ordinal);
        Assert.Contains("RevitGeoSuite.addin", issScript, StringComparison.Ordinal);
        Assert.Contains("KanizuRevit.addin", issScript, StringComparison.Ordinal);
        Assert.Contains("UsePreviousAppDir=no", issScript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2024, true)]
    [InlineData(2026, true)]
    [InlineData(2027, false)]
    public void Install_scripts_put_the_addin_where_that_revit_year_loads_it(int revitYear, bool usesProgramData)
    {
        string helper = TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1"));
        string template = TestPaths.GetRepoPath(Path.Combine("install", "KanizuRevit.addin.template"));
        string programFiles = Environment.GetEnvironmentVariable("ProgramFiles") is { Length: > 0 } pf ? pf : @"C:\Program Files";
        string programData = Environment.GetEnvironmentVariable("ProgramData") is { Length: > 0 } pd ? pd : @"C:\ProgramData";
        string yearText = revitYear.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string addinsRoot = Path.Combine(usesProgramData ? programData : programFiles, "Autodesk", "Revit", "Addins", yearText);
        string staleRoot = Path.Combine(usesProgramData ? programFiles : programData, "Autodesk", "Revit", "Addins", yearText);

        string[] output = RunPowerShell(
            $". '{helper}'; " +
            $"Get-RevitAddinsRoot -RevitYear {revitYear}; " +
            $"Get-StaleRevitAddinsRoot -RevitYear {revitYear}; " +
            $"([xml](Get-AddinManifestContents -TemplatePath '{template}' -RevitYear {revitYear})).RevitAddIns.AddIn.Assembly");

        Assert.Equal(new[] { addinsRoot, staleRoot, @"KanizuRevit\RevitGeoSuite.Shell.dll" }, output);
    }

    [Fact]
    public void Install_scripts_resolve_program_files_and_program_data_from_environment()
    {
        string helper = TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1"));
        string[] output = RunPowerShell(
            "$env:ProgramFiles = 'D:\\Program Files'; " +
            "$env:ProgramData = 'D:\\ProgramData'; " +
            $". '{helper}'; " +
            "Get-RevitAddinsRoot -RevitYear 2024; " +
            "Get-StaleRevitAddinsRoot -RevitYear 2024; " +
            "Get-RevitAddinsRoot -RevitYear 2027; " +
            "Get-StaleRevitAddinsRoot -RevitYear 2027");

        Assert.Equal(
            new[]
            {
                @"D:\ProgramData\Autodesk\Revit\Addins\2024",
                @"D:\Program Files\Autodesk\Revit\Addins\2024",
                @"D:\Program Files\Autodesk\Revit\Addins\2027",
                @"D:\ProgramData\Autodesk\Revit\Addins\2027",
            },
            output);
    }

    [Fact]
    public void Install_scripts_share_the_addins_folder_rule()
    {
        foreach (string script in new[] { "install.ps1", "uninstall.ps1", "build-release.ps1" })
        {
            string text = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", script)));
            Assert.Contains(". (Join-Path $PSScriptRoot \"RevitAddinPaths.ps1\")", text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\ProgramData", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Program Files", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Build_output_path_is_isolated_per_revit_year()
    {
        XDocument props = XDocument.Load(TestPaths.GetRepoPath("Directory.Build.props"));
        string? outputPath = props.Root?.Elements("PropertyGroup")
            .Select(group => group.Element("OutputPath")?.Value)
            .SingleOrDefault(value => value is not null);
        Assert.Equal(@"$(RepositoryRoot)bin\Deploy\$(RevitYear)\", outputPath);

        string helper = TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1"));
        string[] deployDirs = RunPowerShell(
            $". '{helper}'; " +
            @"Get-RevitDeployDirectory -RepoRoot 'C:\repo' -RevitYear 2024; " +
            @"Get-RevitDeployDirectory -RepoRoot 'C:\repo' -RevitYear 2027");
        Assert.Equal(new[] { @"C:\repo\bin\Deploy\2024", @"C:\repo\bin\Deploy\2027" }, deployDirs);

        string buildRelease = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "build-release.ps1")));
        string installScript = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "install.ps1")));
        string verifyContracts = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("tools", "VerifyContracts.ps1")));

        Assert.Contains("Get-RevitDeployDirectory", buildRelease, StringComparison.Ordinal);
        Assert.Contains("Get-RevitDeployDirectory", installScript, StringComparison.Ordinal);
        Assert.Contains(@"bin\Deploy\$RevitYear\", verifyContracts, StringComparison.Ordinal);
        Assert.DoesNotContain(@"bin\Deploy""", buildRelease, StringComparison.Ordinal);
        Assert.DoesNotContain(@"bin\Deploy""", installScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Inno_setup_uses_program_files_from_the_same_revit_year_as_the_scripts()
    {
        string helper = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1")));
        string iss = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "RevitGeoSuite.iss")));

        Assert.Equal("2027", Regex.Match(helper, @"\$FirstProgramFilesAddinsYear = (\d+)").Groups[1].Value);
        Match rule = Regex.Match(
            iss,
            @"#if Int\(RevitYear\) >= (?<year>\d+)\s+#define AddinsRoot ""(?<newer>[^""]+)"" \+ RevitYear\s+#define StaleAddinsRoot ""(?<newerStale>[^""]+)"" \+ RevitYear\s+" +
            @"#else\s+#define AddinsRoot ""(?<older>[^""]+)"" \+ RevitYear\s+#define StaleAddinsRoot ""(?<olderStale>[^""]+)"" \+ RevitYear\s+#endif");

        Assert.True(rule.Success, "RevitGeoSuite.iss must choose AddinsRoot and StaleAddinsRoot by Revit year.");
        Assert.Equal("2027", rule.Groups["year"].Value);
        Assert.Equal(@"{commonpf64}\Autodesk\Revit\Addins\", rule.Groups["newer"].Value);
        Assert.Equal(@"{commonappdata}\Autodesk\Revit\Addins\", rule.Groups["newerStale"].Value);
        Assert.Equal(@"{commonappdata}\Autodesk\Revit\Addins\", rule.Groups["older"].Value);
        Assert.Equal(@"{commonpf64}\Autodesk\Revit\Addins\", rule.Groups["olderStale"].Value);
        Assert.Contains(@"Type: filesandordirs; Name: ""{#StaleAddinsRoot}\KanizuRevit""", iss, StringComparison.Ordinal);
        Assert.Contains(@"Type: files; Name: ""{#StaleAddinsRoot}\KanizuRevit.addin""", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void Inno_setup_refuses_mismatched_dist_revit_year()
    {
        string iss = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "RevitGeoSuite.iss")));
        Assert.Contains("RevitYear.txt", iss, StringComparison.Ordinal);
        Assert.Contains("FileOpen(DistRevitYearFile)", iss, StringComparison.Ordinal);
        Assert.Contains("DistRevitYear != RevitYear", iss, StringComparison.Ordinal);
        Assert.Contains("#error", iss, StringComparison.Ordinal);

        string buildInstaller = File.ReadAllText(TestPaths.GetRepoPath(Path.Combine("install", "build-installer.ps1")));
        Assert.Contains(". (Join-Path $PSScriptRoot \"RevitAddinPaths.ps1\")", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("Assert-RevitPayloadMatchesYear", buildInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_validation_accepts_matching_year_and_refuses_mismatched_year()
    {
#if NETFRAMEWORK
        const int matchingYear = 2024;
        const int wrongYear = 2027;
        const string expectedTfm = ".NETFramework,Version=v4.8";
#else
        const int matchingYear = 2027;
        const int wrongYear = 2024;
        const string expectedTfm = ".NETCoreApp,Version=v10.0";
#endif

        string helper = TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1"));
        string tempDir = Path.Combine(Path.GetTempPath(), "rgs-payload-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string fakeShellDll = Path.Combine(tempDir, "RevitGeoSuite.Shell.dll");
            File.Copy(typeof(AddinManifestGuardTests).Assembly.Location, fakeShellDll, overwrite: true);

            string[] detected = RunPowerShell(
                $". '{helper}'; " +
                $"Get-AssemblyTargetFramework -AssemblyPath '{fakeShellDll}'; " +
                $"Assert-RevitPayloadMatchesYear -SourceDirectory '{tempDir}' -RevitYear {matchingYear}; " +
                $"Write-RevitYearStamp -Directory '{tempDir}' -RevitYear {matchingYear} | Out-Null; " +
                $"Assert-RevitPayloadMatchesYear -SourceDirectory '{tempDir}' -RevitYear {matchingYear} -RequireYearStamp; " +
                "'OK'");
            Assert.Equal(new[] { expectedTfm, "OK" }, detected);

            // RevitYear.txt must be plain ASCII/UTF-8 without BOM so Inno Setup ISPP FileRead matches cleanly.
            byte[] stampBytes = File.ReadAllBytes(Path.Combine(tempDir, "RevitYear.txt"));
            Assert.Equal(
                System.Text.Encoding.ASCII.GetBytes(matchingYear.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                stampBytes);

            // Mismatched RevitYear.txt stamp is refused.
            (int wrongStampExit, _, string wrongStampErr) = RunPowerShellResult(
                $". '{helper}'; " +
                $"Write-RevitYearStamp -Directory '{tempDir}' -RevitYear {wrongYear} | Out-Null; " +
                $"Assert-RevitPayloadMatchesYear -SourceDirectory '{tempDir}' -RevitYear {matchingYear}");
            Assert.NotEqual(0, wrongStampExit);
            Assert.Contains("RevitYear.txt", Regex.Replace(wrongStampErr, @"\s+", " "), StringComparison.OrdinalIgnoreCase);

            // Missing stamp is refused when -RequireYearStamp is passed (protects dist/ installs).
            File.Delete(Path.Combine(tempDir, "RevitYear.txt"));
            (int missingStampExit, _, string missingStampErr) = RunPowerShellResult(
                $". '{helper}'; " +
                $"Assert-RevitPayloadMatchesYear -SourceDirectory '{tempDir}' -RevitYear {matchingYear} -RequireYearStamp");
            Assert.NotEqual(0, missingStampExit);
            Assert.Contains("missing RevitYear.txt", Regex.Replace(missingStampErr, @"\s+", " "), StringComparison.OrdinalIgnoreCase);

            // Mismatched TargetFrameworkAttribute on the DLL itself is refused even without RevitYear.txt.
            (int wrongTfmExit, _, string wrongTfmErr) = RunPowerShellResult(
                $". '{helper}'; " +
                $"Assert-RevitPayloadMatchesYear -SourceDirectory '{tempDir}' -RevitYear {wrongYear}");
            Assert.NotEqual(0, wrongTfmExit);
            Assert.Contains("target framework mismatch", Regex.Replace(wrongTfmErr, @"\s+", " "), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("2024", "net48")]
    [InlineData("2027", "net10.0-windows")]
    public void Each_revit_year_builds_for_the_framework_that_revit_year_loads(string revitYear, string targetFramework)
    {
        XDocument props = XDocument.Load(TestPaths.GetRepoPath("Directory.Build.props"));
        XElement? group = props.Root?.Elements("PropertyGroup")
            .SingleOrDefault(element => (string?)element.Attribute("Condition") == $"'$(RevitYear)' == '{revitYear}'");

        Assert.NotNull(group);
        Assert.Equal(targetFramework, group!.Element("RevitTargetFramework")?.Value);
    }

    private static string[] RunPowerShell(string command)
    {
        (int exitCode, string[] output, string error) = RunPowerShellResult(command);
        Assert.True(exitCode == 0, $"PowerShell failed: {error}");
        return output;
    }

    private static (int ExitCode, string[] StdOut, string StdErr) RunPowerShellResult(string command)
    {
        foreach (string shell in new[] { "pwsh", "powershell" })
        {
            ProcessStartInfo startInfo = new(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"";

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                continue;
            }

            using (process)
            {
                string output = process!.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (
                    process.ExitCode,
                    output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries),
                    error);
            }
        }

        throw new InvalidOperationException("Neither pwsh nor powershell was found to run the install scripts.");
    }

    private static (string AddInId, string Name, string FullClassName) ReadManifest(string path)
    {
        XDocument doc = XDocument.Load(path);
        XElement addIn = doc.Root?.Element("AddIn") ?? throw new InvalidDataException($"Missing <AddIn> in {path}.");
        return (
            addIn.Element("AddInId")?.Value.Trim() ?? string.Empty,
            addIn.Element("Name")?.Value.Trim() ?? string.Empty,
            addIn.Element("FullClassName")?.Value.Trim() ?? string.Empty);
    }
}
