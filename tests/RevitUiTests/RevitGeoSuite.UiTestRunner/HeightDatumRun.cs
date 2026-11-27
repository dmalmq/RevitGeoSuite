using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The survey point question in Export to Cesium (R7): a required choice, never a default. An
/// unanswered model has Export off; the export refuses an unanswered request from any caller; "No",
/// picked and exported in the dialog, writes a manifest Kanizu reads as "unknown" while the model
/// remembers "notTp", and the dialog shows that answer next time; a "TP" answer comes back picked too. Step ids R7.*. Everything it writes goes to station/ in the run folder.
/// </summary>
internal sealed partial class GisStandardRun
{
    private async Task HeightDatumSteps()
    {
        string station = Path.Combine(options.OutputFolder, "station");
        await Step("R7.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);

        await Step("R7.1", "The dialog shows the copy's answer, or none: an unanswered copy has both choices clear and Export off", async step =>
        {
            // The dialog shows a first-run note instead of a profile on a PC that never saved one.
            JsonNode? state = await Shell.RpcAsync("cesium.export.getState");
            if ((state?["floorPlanProfiles"]?.AsArray().Count ?? 0) == 0)
            {
                await SaveTestProfile(step);
            }

            string remembered = (await Shell.RpcAsync("cesium.export.getState"))?["lastHeightDatum"]?.GetValue<string>() ?? string.Empty;
            step.Note($"The copy remembers '{remembered}'. getState passes a stored answer through CesiumHeightDatums.Answer, so a stored old 'unknown' and no answer at all both read 'unknown'.");
            (bool tp, bool notTp, bool exportOff) = await OpenCesiumDialog(step, "R7-1-dialog");
            if (remembered is "TP" or "notTp")
            {
                step.Check(remembered == "TP" ? tp && !notTp : notTp && !tp, $"Its answer '{remembered}' is picked.");
                step.Note("This copy already had an answer, so the unanswered dialog was not seen here; the web test covers it.");
            }
            else
            {
                step.Check(!tp && !notTp, $"Neither choice is picked for '{remembered}' (an old 'unknown' asks again).");
                step.Check(exportOff, "Export is off until the question is answered.");
                step.Check(await Shell.ShowsAsync(Shell.Text("Answer this before you export.")), "The dialog says why.");
            }

            await Shell.Button("Close").First.ClickAsync();
        });

        await Step("R7.2", "cesium.export.run refuses a request without an answer and writes nothing", async step =>
        {
            Directory.CreateDirectory(station);
            int before = Directory.GetFileSystemEntries(station, "*", SearchOption.AllDirectories).Length;
            foreach (string datum in new[] { "unknown", "", "ellipsoidal" })
            {
                JsonNode? reply = await Shell.RpcAsync("cesium.export.run", new
                {
                    outputFolder = station,
                    floorPlanProfileName = "any",
                    push = false,
                    scope = "whole",
                    lod = "coarse",
                    preciseCrs = true,
                    heightDatum = datum,
                    keepVersions = true,
                    selectedLinkUniqueIds = Array.Empty<string>(),
                });
                string? error = reply?["error"]?.GetValue<string>();
                step.Check(error == "Answer this before you export." && reply?["jobId"] == null, $"heightDatum '{datum}' is refused ({reply?.ToJsonString()}).");
            }

            step.Check(Directory.GetFileSystemEntries(station, "*", SearchOption.AllDirectories).Length == before, "Nothing was written to the station folder.");
        });

        string? package = null;
        await Step("R7.3", "In the dialog, \"No\" and Export write heightDatum unknown in the manifest, and the model remembers notTp", async step =>
        {
            package = await ExportFromDialog(step, station, "notTp", "R7-3-exported");
            if (package == null)
            {
                return;
            }

            string? datum = await ManifestDatum(package);
            step.Check(datum == "unknown", $"The manifest's anchor.heightDatum is 'unknown' ({datum}), so Kanizu never applies these heights.");
            string remembered = (await Shell.RpcAsync("cesium.export.getState"))?["lastHeightDatum"]?.GetValue<string>() ?? string.Empty;
            step.Check(remembered == "notTp", $"The model remembers 'notTp' ({remembered}).");
        });

        await Step("R7.4", "Reopened, the dialog has \"No\" picked and Export on", async step =>
        {
            if (package == null)
            {
                step.NotRun("R7.3 did not export.");
                return;
            }

            await Shell.Page.ReloadAsync();
            (bool tp, bool notTp, bool exportOff) = await OpenCesiumDialog(step, "R7-4-reopened");
            step.Check(notTp && !tp, "\"No, or I have not checked\" is picked.");
            step.Check(!exportOff, "Export is on.");
            await Shell.Button("Close").First.ClickAsync();
        });

        await Step("R7.5", "A model that answered T.P. gets T.P. picked again, and its manifest says TP", async step =>
        {
            CesiumRun? run = await ExportCesium(step, station, heightDatum: "TP");
            if (run == null)
            {
                return;
            }

            string? datum = await ManifestDatum(run.PackageRoot);
            step.Check(datum == "TP", $"The manifest's anchor.heightDatum is 'TP' ({datum}).");
            await Shell.Page.ReloadAsync();
            (bool tp, bool notTp, bool exportOff) = await OpenCesiumDialog(step, "R7-5-tp");
            step.Check(tp && !notTp, "\"Yes, it was set from T.P.\" is picked.");
            step.Check(!exportOff, "Export is on.");
            await Shell.Button("Close").First.ClickAsync();
        });
    }

    private static async Task<string?> ManifestDatum(string packageRoot) =>
        JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(packageRoot, "cesium-package.json")))?["anchor"]?["heightDatum"]?.GetValue<string>();

    /// <summary>
    /// Exports from the dialog the way a person does: picks <paramref name="answer"/>, types the station
    /// folder, coarse, a new version each time, no push, and clicks Export. Returns the new package's
    /// folder. Export saves the viewer URL first (cesium.settings.save, which writes
    /// %APPDATA%\RevitGeoSuite\CesiumHandoff\viewer-settings.json); the run turns that one call into
    /// the read-only cesium.settings.get inside the page, so the real settings are never written.
    /// </summary>
    private async Task<string?> ExportFromDialog(StepResult step, string station, string answer, string shot)
    {
        Directory.CreateDirectory(station);
        string[] before = Manifests(station);
        await OpenCesiumDialog(step, shot + "-open");
        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        bool shimmed = await Shell.Page.EvaluateAsync<bool>(@"() => {
            const webview = window.chrome.webview;
            const post = webview.postMessage.bind(webview);
            const shim = (message) => post(message && message.method === 'cesium.settings.save' ? { ...message, method: 'cesium.settings.get', payload: {} } : message);
            shim.rgsUiTest = true;
            webview.postMessage = shim;
            return webview.postMessage.rgsUiTest === true;
        }");
        if (!step.Check(shimmed, "The page's cesium.settings.save goes to cesium.settings.get for this export, so the real viewer settings stay untouched."))
        {
            await Shell.Button("Close").First.ClickAsync();
            return null;
        }

        await dialog.Locator($"input[name='cesium-height-datum'][value='{answer}']").CheckAsync();
        await dialog.Locator("#cesiumFolderInput").FillAsync(station);
        await dialog.Locator("#cesiumLodSelect").SelectOptionAsync("coarse");
        await dialog.GetByLabel("Station folder for Kanizu: add a new version each time").CheckAsync();
        await dialog.GetByLabel(new Regex("Push to viewer after export")).UncheckAsync();
        ILocator export = dialog.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true });
        int seq = host.LastEvent();
        await export.ClickAsync();
        DateTime until = DateTime.UtcNow + CesiumWait;
        await Shell.Page.WaitForTimeoutAsync(1000);
        while (await export.IsDisabledAsync() && DateTime.UtcNow < until)
        {
            await Shell.Page.WaitForTimeoutAsync(2000);
        }

        await Shot(step, shot);
        string commits = string.Join(" / ", host.EventsAfter(seq).Where(item => item.Kind == "commit"));
        string[] added = Manifests(station).Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
        step.Note($"Exported from the dialog with '{answer}'. Transactions: {(commits.Length == 0 ? "none" : commits)}. New manifests: {string.Join(", ", added)}.");
        if (!step.Check(added.Length == 1, $"The export wrote one new package under {station}."))
        {
            step.Note($"Dialog text: {(await dialog.InnerTextAsync()).Replace('\n', ' ')}");
            await Shell.Button("Close").First.ClickAsync();
            return null;
        }

        await Shell.Button("Close").First.ClickAsync();
        return Path.GetDirectoryName(added[0]);
    }

    private static string[] Manifests(string station) =>
        Directory.Exists(station) ? Directory.GetFiles(station, "cesium-package.json", SearchOption.AllDirectories) : Array.Empty<string>();

    /// <summary>Opens Export to Cesium from the export page; returns which answer is picked and whether Export is off.</summary>
    private async Task<(bool Tp, bool NotTp, bool ExportOff)> OpenCesiumDialog(StepResult step, string shot)
    {
        await Shell.GoToAsync("/export");
        await Shell.Button(new Regex("Export to Cesium")).First.ClickAsync();
        ILocator radios = Shell.Page.Locator("input[name='cesium-height-datum']");
        if (!step.Check(await Shell.ShowsAsync(Shell.Page.GetByRole(AriaRole.Group, new() { Name = "Was the survey point elevation set from T.P. (Tokyo Peil)?" })), "The dialog asks the survey point question."))
        {
            await Shot(step, shot);
            return (false, false, true);
        }

        bool tp = await radios.And(Shell.Page.Locator("[value='TP']")).IsCheckedAsync();
        bool notTp = await radios.And(Shell.Page.Locator("[value='notTp']")).IsCheckedAsync();
        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        bool exportOff = await dialog.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true }).IsDisabledAsync();
        await Shot(step, shot);
        return (tp, notTp, exportOff);
    }
}
