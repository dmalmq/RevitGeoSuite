using System;
using System.IO;
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
