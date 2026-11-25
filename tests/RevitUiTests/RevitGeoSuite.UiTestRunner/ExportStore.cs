namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The export page's remembered files in the store Run-UiTests.ps1 prepared for the run: settings.json, which it
/// emptied, and profiles.json beside it. Before each scenario after the first, <see cref="Reset"/> puts both back
/// as the run found them, so a scenario starts as it would in a Revit of its own: no folder or views an earlier
/// scenario remembered, and no profile an earlier scenario saved. Run-UiTests.ps1 still restores the real files
/// when the run ends.
/// </summary>
internal sealed class ExportStore
{
    private readonly string settings;
    private readonly string profiles;
    private readonly byte[]? profilesAtStart;

    public ExportStore(string settingsFile)
    {
        // Run-UiTests.ps1 keeps the real file as settings.json.uitest-backup (or an .uitest-absent marker) until it
        // puts it back. Without one, nothing would bring this file back: refuse rather than empty someone's settings.
        if (!File.Exists(settingsFile + ".uitest-backup") && !File.Exists(settingsFile + ".uitest-absent"))
        {
            throw new ArgumentException($"--export-settings {settingsFile} has no .uitest-backup or .uitest-absent beside it, so Run-UiTests.ps1 did not prepare it. Only Run-UiTests.ps1 -StartRevit passes this option.");
        }

        settings = settingsFile;
        profiles = Path.Combine(Path.GetDirectoryName(settingsFile)!, "profiles.json");
        profilesAtStart = File.Exists(profiles) ? File.ReadAllBytes(profiles) : null;
    }

    public string Reset()
    {
        File.WriteAllText(settings, "{}");
        if (profilesAtStart == null)
        {
            File.Delete(profiles);
        }
        else
        {
            File.WriteAllBytes(profiles, profilesAtStart);
        }

        return $"Emptied {settings} again and put {profiles} back as the run found it ({(profilesAtStart == null ? "none" : $"{profilesAtStart.Length} bytes")}), so this scenario starts from the export page's defaults.";
    }
}
