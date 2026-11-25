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
    [InlineData(2024, @"C:\ProgramData\Autodesk\Revit\Addins\2024", @"C:\Program Files\Autodesk\Revit\Addins\2024")]
    [InlineData(2026, @"C:\ProgramData\Autodesk\Revit\Addins\2026", @"C:\Program Files\Autodesk\Revit\Addins\2026")]
    [InlineData(2027, @"C:\Program Files\Autodesk\Revit\Addins\2027", @"C:\ProgramData\Autodesk\Revit\Addins\2027")]
    public void Install_scripts_put_the_addin_where_that_revit_year_loads_it(int revitYear, string addinsRoot, string staleRoot)
    {
        string helper = TestPaths.GetRepoPath(Path.Combine("install", "RevitAddinPaths.ps1"));
        string template = TestPaths.GetRepoPath(Path.Combine("install", "KanizuRevit.addin.template"));

        string[] output = RunPowerShell(
            $". '{helper}'; " +
            $"Get-RevitAddinsRoot -RevitYear {revitYear}; " +
            $"Get-StaleRevitAddinsRoot -RevitYear {revitYear}; " +
            $"([xml](Get-AddinManifestContents -TemplatePath '{template}' -RevitYear {revitYear})).RevitAddIns.AddIn.Assembly");

        Assert.Equal(new[] { addinsRoot, staleRoot, addinsRoot + @"\KanizuRevit\RevitGeoSuite.Shell.dll" }, output);
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
                Assert.True(process.ExitCode == 0, $"{shell} failed: {error}");
                return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
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
