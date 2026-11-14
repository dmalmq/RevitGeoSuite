using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The H4 check (/mnt/project-files/shared-rules/revit-scenario.md): every PC that exports a model into a
/// station folder gets the same GIS package from the station's gis-rules.json. Step ids H4.1 to H4.9 are
/// the spec's steps 1 to 9. The scenario refuses to run unless this Revit's per-PC store is the run's
/// sandbox (Run-UiTests.ps1 -FloorPlanDataSandbox), so the roaming %APPDATA% folder is never touched.
/// The filesystem actions between exports (renaming the store, holding the rules file, the lock file,
/// icacls, the model's profile field) are done here, by the runner, and always undone in a finally.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string StationRulesFile = "gis-rules.json";
    private const string PcBProfile = "H4 PC B";
    private const string OffSuffix = ".h4-off";

    private SharedRulesCase? shared;

    private async Task SharedRulesSteps()
    {
        await Step("H4.0", "Revit runs on this run's own per-PC store, and the real one is on record", SandboxGuard);
        await Step("H4.S", "The model has had Set up model once, so each level has a GIS_ plan", step => Gated(step, EnsureSetUp));
        await Step("H4.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", step => Gated(step, EnsureGeoreference));
        await Step("H4.1", "Note what the per-PC store holds", step => Gated(step, NoteStore));
        await Step("H4.2", "Run A (PC A): Cesium export into the station moves the per-PC rules and profile in", step => Gated(step, RunA));
        await Step("H4.3", "Run B (PC B, no local rules): the same hash and the same GIS package, one warning for the other EPSG", step => Gated(step, RunB));
        await Step("H4.4", "Run C: a category saved in the preview goes to the station and changes the hash", step => Gated(step, RunC));
        await Step("H4.5", "A rules file held open: the save fails and leaves nothing behind", step => Gated(step, LockedFile));
        await Step("H4.6", "A live lock file stops the save; after 30 s it is taken over", step => Gated(step, LiveLock));
        await Step("H4.7", "A station that cannot be written: the export uses this PC's rules and says so", step => Gated(step, ReadOnlyStation));
        await Step("H4.8", "A profile the station does not have stops the export with the list of profiles", step => Gated(step, MissingProfile));
        await Step("H4.9", "The real per-PC folder is unchanged", step => Gated(step, RealFolderUnchanged));
    }

    private async Task Gated(StepResult step, Func<StepResult, Task> body)
    {
        if (shared == null)
        {
            step.NotRun("H4.0 did not confirm the sandbox, so nothing in this scenario ran.");
            return;
        }

        await body(step);
    }

    private Task SandboxGuard(StepResult step)
    {
        string? inRevit = host.Call("ping")["floorPlanData"]?.GetValue<string>();
        step.Note($"{Sandbox.Variable} in Revit: {inRevit ?? "(not reported by this test host)"}.");
        string? problem = Sandbox.Problem(inRevit, options.OutputFolder);
        if (!step.Check(problem == null, problem ?? string.Empty))
        {
            return Task.CompletedTask;
        }

        string store = Sandbox.Store(options.OutputFolder);
        Directory.CreateDirectory(store);
        string real = Sandbox.RealStore();
        if (!step.Check(!TestPaths.IsUnder(store, real) && !TestPaths.IsUnder(real, store), $"The sandbox store {store} and the real folder {real} are different folders."))
        {
            return Task.CompletedTask;
        }

        string beforeFile = Path.Combine(options.OutputFolder, Sandbox.BeforeFile);
        if (!step.Check(File.Exists(beforeFile), $"{beforeFile} records the real folder from before the run (Run-UiTests.ps1 -FloorPlanDataSandbox writes it)."))
        {
            return Task.CompletedTask;
        }

        FolderSnapshot recorded = FolderSnapshot.Parse(File.ReadAllText(beforeFile));
        step.Note($"Real folder {real}: {recorded.Files.Count} file(s) on record. The sandbox store {store} is a copy of it made before Revit started.");
        shared = new SharedRulesCase(Path.Combine(options.OutputFolder, "h4-station"), store, store + OffSuffix);

        return Task.CompletedTask;
    }

    private Task NoteStore(StepResult step)
    {
        SharedRulesCase rules = shared!;
        string mapping = Path.Combine(rules.Store, "mapping-rules");
        string[] mappingFiles = Directory.Exists(mapping) ? Directory.GetFiles(mapping).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        step.Note($"mapping-rules: {mappingFiles.Length} file(s){(mappingFiles.Length > 0 ? ": " + string.Join(", ", mappingFiles) : string.Empty)}. The file name is derived from a model's project key, so the page cannot say which of them is this model's.");
        string profiles = Path.Combine(rules.Store, "profiles.json");
        if (!File.Exists(profiles))
        {
            step.Note("profiles.json: none.");
        }
        else
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(profiles, Encoding.UTF8))?.AsObject() ?? new JsonObject();
            step.Note($"profiles.json: {string.Join("; ", root.Select(pair => $"{pair.Key} {Summary(pair.Value)}"))}.");
        }

        step.Note($"All files in the store: {string.Join(", ", RelativeFiles(rules.Store))}.");
        step.Note($"Model: {modelCopy}.");
        return Task.CompletedTask;
    }

    private async Task RunA(StepResult step)
    {
        SharedRulesCase rules = shared!;
        string[] before = RelativeFiles(rules.Store);
        string[] ruleFilesBefore = before.Where(IsMappingRule).ToArray();
        CesiumRun? run = await ExportCesium(step, rules.Station);
        if (run == null)
        {
            return;
        }

        rules.A = run;
        rules.StampA = StampOf(step, run, "A");
        string rulesPath = Path.Combine(rules.Station, StationRulesFile);
        if (!step.Check(File.Exists(rulesPath), $"{rulesPath} exists."))
        {
            return;
        }

        JsonNode file = StationRules.Read(rules.Station);
        JsonNode? station = StationRules.Profile(file, "Station");
        if (step.Check(station != null, "gis-rules.json has a profile named 'Station'.") && station != null)
        {
            JsonNode? used = StationRules.Profile(file, run.Profile);
            if (used == null)
            {
                step.Note($"The profile used, '{run.Profile}', stays on this PC (global), so the Station profile cannot be compared with it here. Station: {StationRules.Settings(station)}.");
            }
            else
            {
                step.Check(station["targetEpsg"]?.ToJsonString() == used["targetEpsg"]?.ToJsonString(), $"The Station profile's targetEpsg is the used profile's ({station["targetEpsg"]} vs {used["targetEpsg"]}).");
                bool same = StationRules.Settings(station) == StationRules.Settings(used);
                step.Note(same
                    ? $"The Station profile has exactly the settings of '{run.Profile}'."
                    : $"The Station profile differs from '{run.Profile}' beyond targetEpsg. Station: {StationRules.Settings(station)}. {run.Profile}: {StationRules.Settings(used)}.");
            }
        }

        step.Note($"gis-rules.json categoryRules: {file["categoryRules"]?.AsArray().Count ?? 0}.");
        string[] after = RelativeFiles(rules.Store);
        string[] moved = ruleFilesBefore.Where(name => !after.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (string name in moved)
        {
            step.Check(after.Contains(name + ".moved-to-station", StringComparer.OrdinalIgnoreCase), $"The per-PC rules file {name} was renamed to {name}.moved-to-station.");
        }

        step.Note(moved.Length == 0
            ? "No per-PC rules file was set aside: the store held none for this model."
            : $"Set aside: {string.Join(", ", moved)}.");
        step.Note($"Renamed in the store by this export: {string.Join(", ", after.Where(name => name.EndsWith(".moved-to-station", StringComparison.OrdinalIgnoreCase) && !before.Contains(name, StringComparer.OrdinalIgnoreCase)))}.");

        string[] models = Directory.Exists(Path.Combine(rules.Station, "gis-models")) ? Directory.GetFiles(Path.Combine(rules.Station, "gis-models"), "*.gis-model.json") : Array.Empty<string>();
        if (step.Check(models.Length == 1, $"The station has one gis-model.json ({models.Length}).") && models.Length == 1)
        {
            rules.ModelFile = models[0];
            JsonNode model = JsonNode.Parse(File.ReadAllText(models[0], Encoding.UTF8))!;
            step.Check(model["links"] is JsonArray, $"{Path.GetFileName(models[0])} has links ({model["links"]?.ToJsonString() ?? "absent"}).");
        }
    }

    private async Task RunB(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.A == null || rules.StampA == null)
        {
            step.NotRun("H4.2 produced no run A.");
            return;
        }

        Directory.CreateDirectory(rules.Store);
        Directory.Move(rules.Store, rules.StoreOff);
        step.Note($"Renamed {rules.Store} to {rules.StoreOff}: this PC has no local rules and no profile.");
        CesiumRun? run;
        try
        {
            await SaveTestProfile(step, PcBProfile, "global", 6676);
            run = await ExportCesium(step, rules.Station, PcBProfile);
        }
        finally
        {
            PutStoreBack(step);
        }

        if (run == null)
        {
            return;
        }

        rules.B = run;
        rules.StampB = StampOf(step, run, "B");
        string[] differences = run.Warnings.Where(warning => warning.Contains("(yours -> station)", StringComparison.Ordinal)).ToArray();
        step.Check(differences.Length == 1, $"One warning lists 'setting: yours -> station' ({differences.Length}).");
        step.Check(rules.StampB?.Hash == rules.StampA.Hash, $"gis.rules.hash equals run A's ({rules.StampB?.Hash} vs {rules.StampA.Hash}).");
        step.Check(rules.StampB?.Source == "station", $"Run B's rules came from the station ({rules.StampB?.Source}).");
        string fieldsA = Path.Combine(rules.A.PackageRoot, "gis", "gis-fields.json");
        string fieldsB = Path.Combine(run.PackageRoot, "gis", "gis-fields.json");
        if (step.Check(File.Exists(fieldsA) && File.Exists(fieldsB), $"Both packages have gis\\gis-fields.json ({File.Exists(fieldsA)}, {File.Exists(fieldsB)})."))
        {
            step.Check(File.ReadAllBytes(fieldsA).SequenceEqual(File.ReadAllBytes(fieldsB)), "gis-fields.json is byte-equal in A and B.");
        }

        List<string> covered = GeoPackageCompare.Covered(rules.A.PackageRoot);
        step.Check(covered.Count > 0, "Run A has GeoPackage feature tables to compare.");
        List<string> tableDifferences = GeoPackageCompare.Differences(rules.A.PackageRoot, run.PackageRoot);
        step.Check(tableDifferences.Count == 0, $"Every feature table is equal row for row, ignoring fid order and gpkg_contents.last_change ({tableDifferences.Count} difference(s)).");
        step.Note($"GeoPackage comparison, A against B: {(tableDifferences.Count == 0 ? "no difference" : string.Join(" || ", tableDifferences))}. Tables compared: {string.Join(", ", covered)}.");
    }

    private async Task RunC(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.StampA == null)
        {
            step.NotRun("H4.2 produced no run A.");
            return;
        }

        if (!await OpenPreview(step, "preview-c", rules))
        {
            return;
        }

        int seq = host.LastEvent();
        AssignmentResult saved = await AssignInPreview(rules.FloorType, rules.CategoryOne);
        step.Check(saved.Saved, $"Saving {rules.FloorType} as {rules.CategoryOne} in the preview succeeds ({saved.Error}).");
        NoCommit(step, seq);
        string saidSaved = $"Saved for station {Path.GetFileName(rules.Station)}";
        step.Check(
            (await Shell.Page.InnerTextAsync("body")).Contains(saidSaved, StringComparison.Ordinal),
            $"The Category assignments dialog shows '{saidSaved}' after the save.");
        step.Note("Japanese wording of the same line: 'プレビューの床割り当てを保存しました。ステーション「" + Path.GetFileName(rules.Station) + "」に保存しました。'");
        await Shot(step, "H4-4-preview-saved");
        JsonNode? rule = StationRules.FloorRule(StationRules.Read(rules.Station), rules.FloorType);
        step.Check(rule?["value"]?.GetValue<string>() == rules.CategoryOne, $"gis-rules.json has floorCategory '{rules.FloorType}' = '{rules.CategoryOne}' ({rule?.ToJsonString() ?? "no rule"}).");

        await Shell.GoToAsync("/gis-standard");
        CesiumRun? run = await ExportCesium(step, rules.Station);
        if (run == null)
        {
            return;
        }

        rules.C = run;
        rules.StampC = StampOf(step, run, "C");
        step.Check(rules.StampC?.Source == "station", $"Run C's rules came from the station ({rules.StampC?.Source}).");
        step.Check(rules.StampC?.Hash != rules.StampA.Hash && !string.IsNullOrEmpty(rules.StampC?.Hash), $"gis.rules.hash differs from run A's ({rules.StampC?.Hash} vs {rules.StampA.Hash}).");
    }

    private async Task LockedFile(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.StampC == null)
        {
            step.NotRun("H4.4 saved no rule, so the preview's category choices are not known.");
            return;
        }

        string path = Path.Combine(rules.Station, StationRulesFile);
        if (!await OpenPreview(step, "preview-locked", rules))
        {
            return;
        }

        byte[] before = File.ReadAllBytes(path);
        int seq = host.LastEvent();
        AssignmentResult result;
        var clock = Stopwatch.StartNew();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await AssignInPreview(rules.FloorType, rules.CategoryTwo);
        }

        step.Note($"The save took {clock.Elapsed.TotalSeconds:0.0} s while the file was held open.");
        step.Check(!result.Saved && result.Error.Length > 0, $"The save fails with a message ({result.Error}).");
        step.Check(result.Error.Contains(StationRulesFile, StringComparison.Ordinal), $"The message names {StationRulesFile}.");
        await Shot(step, "H4-5-locked");
        NoCommit(step, seq);
        step.Check(File.ReadAllBytes(path).SequenceEqual(before), $"{StationRulesFile} is unchanged and readable after the handle closed.");
        CheckNothingLeft(step, rules.Station, expectLock: false);
    }

    private async Task LiveLock(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.StampC == null)
        {
            step.NotRun("H4.4 saved no rule, so the preview's category choices are not known.");
            return;
        }

        string path = Path.Combine(rules.Station, StationRulesFile);
        string lockPath = Path.Combine(rules.Station, "gis-rules.lock");
        const string lockText = "h4 test: a lock written by hand";
        if (!await OpenPreview(step, "preview-lock", rules))
        {
            return;
        }

        byte[] before = File.ReadAllBytes(path);
        File.WriteAllText(lockPath, lockText);
        DateTime created = File.GetLastWriteTimeUtc(lockPath);
        try
        {
            AssignmentResult blocked = await AssignInPreview(rules.FloorType, rules.CategoryTwo);
            step.Check(!blocked.Saved && blocked.Error.Length > 0, $"The save fails while the lock is live ({blocked.Error}).");
            step.Check(blocked.Error.Contains("gis-rules.lock", StringComparison.Ordinal) || blocked.Error.Contains("is being saved by", StringComparison.Ordinal), "The message names the holder or the lock file.");
            await Shot(step, "H4-6-live-lock");
            step.Check(File.Exists(lockPath) && File.ReadAllText(lockPath) == lockText, "The hand-made lock is still there, untouched.");
            step.Check(File.ReadAllBytes(path).SequenceEqual(before), $"{StationRulesFile} is unchanged.");

            TimeSpan wait = created.AddSeconds(32) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                step.Note($"Waiting {wait.TotalSeconds:0} s until the lock is more than 30 s old.");
                await Task.Delay(wait);
            }

            AssignmentResult saved = await AssignInPreview(rules.FloorType, rules.CategoryTwo);
            step.Check(saved.Saved, $"The save succeeds once the lock is stale ({saved.Error}).");
            step.Check(!File.Exists(lockPath), "gis-rules.lock is gone after the save.");
            JsonNode? rule = StationRules.FloorRule(StationRules.Read(rules.Station), rules.FloorType);
            step.Check(rule?["value"]?.GetValue<string>() == rules.CategoryTwo, $"gis-rules.json has '{rules.FloorType}' = '{rules.CategoryTwo}' ({rule?.ToJsonString() ?? "no rule"}).");
        }
        finally
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
                step.Note("The runner deleted the lock file that was still there.");
            }
        }

        CheckNothingLeft(step, rules.Station, expectLock: false);
    }

    private async Task ReadOnlyStation(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.StampA == null)
        {
            step.NotRun("H4.2 produced no run A.");
            return;
        }

        string path = Path.Combine(rules.Station, StationRulesFile);
        byte[] rulesBefore = File.ReadAllBytes(path);
        string[] storeBefore = RelativeFiles(rules.Store);
        var attempt = new CesiumAttempt();
        CesiumRun? run;
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        Icacls(step, rules.Station, "/deny", $"{user}:(W)");
        try
        {
            run = await ExportCesium(step, rules.Station, attempt: attempt);
        }
        finally
        {
            Icacls(step, rules.Station, "/remove:d", user);
            string listing = Icacls(step, rules.Station).Output;
            step.Check(!listing.Contains("(DENY)", StringComparison.OrdinalIgnoreCase), $"The deny entry for {user} is gone from {rules.Station}. If it is not, run: icacls \"{rules.Station}\" /remove:d \"{user}\" ({listing.Trim()}).");
        }

        step.Check(File.ReadAllBytes(path).SequenceEqual(rulesBefore), $"{StationRulesFile} is unchanged.");
        step.Check(RelativeFiles(rules.Store).SequenceEqual(storeBefore, StringComparer.OrdinalIgnoreCase), "Nothing in this PC's store was renamed.");
        if (run == null)
        {
            step.Note($"With write denied on the station folder the export did not finish: {attempt.Error ?? "see the checks above"}.");
            return;
        }

        RulesStamp? stamp = StampOf(step, run, "read-only");
        step.Check(run.Warnings.Any(warning => warning.Contains("read-only or cannot be reached", StringComparison.Ordinal)), $"A warning says the station is read-only and this PC's rules were used ({string.Join(" | ", run.Warnings)}).");
        step.Check(stamp?.Source == "local", $"gis.rules.source is 'local' ({stamp?.Source}).");
    }

    private async Task MissingProfile(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (rules.ModelFile.Length == 0)
        {
            step.NotRun("H4.2 found no gis-model.json.");
            return;
        }

        byte[] original = File.ReadAllBytes(rules.ModelFile);
        JsonNode model = JsonNode.Parse(Encoding.UTF8.GetString(original))!;
        model["profile"] = "Nope";
        File.WriteAllText(rules.ModelFile, model.ToJsonString(new() { WriteIndented = true }), new UTF8Encoding(false));
        var attempt = new CesiumAttempt();
        CesiumRun? run;
        try
        {
            run = await ExportCesium(step, rules.Station, attempt: attempt);
        }
        finally
        {
            File.WriteAllBytes(rules.ModelFile, original);
        }

        step.Check(File.ReadAllBytes(rules.ModelFile).SequenceEqual(original), "The model's gis-model.json is back as it was, without the profile field.");
        step.Check(run == null && attempt.Error != null, "The export stops.");
        string message = attempt.Error ?? string.Empty;
        step.Note($"Message: {message}");
        step.Check(message.Contains("Nope", StringComparison.Ordinal), "The message names the profile 'Nope'.");
        step.Check(Regex.IsMatch(message, "has only: .*Station") || message.Contains("has no profiles", StringComparison.Ordinal), "The message lists the profiles the station has.");
        step.Note("The runner starts the Cesium export through the bridge, so no page shows the message and there is no screenshot of it; the text above is what the export reports.");
    }

    private Task RealFolderUnchanged(StepResult step)
    {
        string beforeFile = Path.Combine(options.OutputFolder, Sandbox.BeforeFile);
        if (!File.Exists(beforeFile))
        {
            step.Check(false, $"{beforeFile} is missing, so the real folder cannot be compared. It is written only by Run-UiTests.ps1 -FloorPlanDataSandbox.");
            return Task.CompletedTask;
        }

        IReadOnlyList<string> changes = FolderSnapshot.Take(Sandbox.RealStore()).DifferencesFrom(FolderSnapshot.Parse(File.ReadAllText(beforeFile)));
        step.Check(changes.Count == 0, $"{Sandbox.RealStore()} has the same files and hashes as before the run ({(changes.Count == 0 ? "no change" : string.Join(", ", changes))}).");
        if (shared != null)
        {
            report.Footer.Add("## gis.rules of the three runs");
            report.Footer.Add(string.Empty);
            foreach ((string name, RulesStamp? stamp) in new[] { ("A", shared.StampA), ("B", shared.StampB), ("C", shared.StampC) })
            {
                report.Footer.Add($"- Run {name}: {stamp?.ToString() ?? "no run"}.");
            }
        }

        return Task.CompletedTask;
    }

    private async Task<bool> OpenPreview(StepResult step, string folderName, SharedRulesCase rules)
    {
        PlanViewRow? plan = GisPlans(Views()).FirstOrDefault();
        if (!step.Check(plan != null, "The model has a GIS_ plan."))
        {
            return false;
        }

        await OpenExport(step, plan!.Name, folderName);
        if (!step.Check(await Shell.ShowsAsync(Shell.Button(new Regex("^Category assignments")), 600000), "The preview loads."))
        {
            return false;
        }

        if (rules.FloorType.Length > 0)
        {
            return true;
        }

        JsonNode state = await Shell.RpcAsync("floorplan.preview.getInitialState") ?? throw new InvalidOperationException("floorplan.preview.getInitialState returned nothing.");
        JsonArray floorRows = state["assignmentSummary"]?["rows"]?.AsArray() ?? new JsonArray();
        JsonNode? row = floorRows.FirstOrDefault(item => item?["isUnassigned"]?.GetValue<bool>() == true) ?? floorRows.FirstOrDefault();
        string[] categories = state["supportedCategories"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
        if (!step.Check(row != null && categories.Length >= 3, $"The preview lists floor types ({floorRows.Count}) and categories ({categories.Length})."))
        {
            return false;
        }

        string current = row!["category"]?.GetValue<string>() ?? string.Empty;
        string[] choices = categories.Where(category => !string.Equals(category, current, StringComparison.OrdinalIgnoreCase)).ToArray();
        rules.FloorType = row["floorTypeName"]!.GetValue<string>();
        rules.CategoryOne = choices[0];
        rules.CategoryTwo = choices[1];
        step.Note($"Floor type '{rules.FloorType}' ({(row["isUnassigned"]?.GetValue<bool>() == true ? "no gis_category" : $"already '{current}', as no floor type was without a category")}) gets '{rules.CategoryOne}', later '{rules.CategoryTwo}'.");
        return true;
    }

    private record AssignmentResult(bool Saved, string Error);

    private async Task<AssignmentResult> AssignInPreview(string floorType, string category)
    {
        await Shell.Button(new Regex("^Category assignments")).ClickAsync();
        await Shell.Page.Locator("#assign-floor-type").SelectOptionAsync(floorType);
        await Shell.Page.Locator("#assign-category").SelectOptionAsync(category);
        ILocator add = Shell.Button("Add assignment");
        await add.ClickAsync();
        await Shell.Page.WaitForTimeoutAsync(300);
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (await add.IsDisabledAsync() && DateTime.UtcNow < end)
        {
            await Shell.Page.WaitForTimeoutAsync(200);
        }

        string error = await Shell.AlertTextAsync();
        return new AssignmentResult(error.Length == 0, error);
    }

    private static void CheckNothingLeft(StepResult step, string station, bool expectLock)
    {
        string[] temps = Directory.GetFiles(station, ".gis-rules.*.tmp");
        step.Check(temps.Length == 0, $"No .gis-rules.*.tmp is left ({string.Join(", ", temps.Select(Path.GetFileName))}).");
        step.Check(File.Exists(Path.Combine(station, "gis-rules.lock")) == expectLock, $"gis-rules.lock is {(expectLock ? "still" : "not")} there.");
    }

    private RulesStamp? StampOf(StepResult step, CesiumRun run, string label)
    {
        RulesStamp? stamp = RulesStamp.Read(run.PackageRoot);
        step.Note($"Run {label}: gis.rules {stamp?.ToString() ?? "absent"}. Warnings: {(run.Warnings.Length == 0 ? "none" : string.Join(" | ", run.Warnings))}");
        step.Check(stamp != null && stamp.Profile.Length > 0 && Regex.IsMatch(stamp.Hash, "^[0-9a-f]{64}$"), $"The manifest of run {label} has gis.rules with a profile and a sha256 hash.");
        return stamp;
    }

    // The store is put back as it was: the one PC B made goes aside, never deleted.
    private void PutStoreBack(StepResult step)
    {
        SharedRulesCase rules = shared!;
        if (!Directory.Exists(rules.StoreOff))
        {
            return;
        }

        if (Directory.Exists(rules.Store))
        {
            string aside = rules.Store + ".pc-b-" + DateTime.Now.ToString("HHmmss");
            Directory.Move(rules.Store, aside);
            step.Note($"PC B's own store was moved to {aside}.");
        }

        Directory.Move(rules.StoreOff, rules.Store);
        step.Note($"Renamed {rules.StoreOff} back to {rules.Store}.");
    }

    private (int ExitCode, string Output) Icacls(StepResult step, string folder, params string[] arguments)
    {
        if (!TestPaths.IsUnder(folder, options.OutputFolder))
        {
            throw new InvalidOperationException($"Refusing icacls on {folder}: it is not inside this run's folder {options.OutputFolder}.");
        }

        var start = new ProcessStartInfo("icacls") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(folder);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (arguments.Length > 0)
        {
            step.Check(process.ExitCode == 0, $"icacls {string.Join(' ', arguments)} on {folder} succeeds ({output.Trim()}).");
        }

        return (process.ExitCode, output);
    }

    private static string[] RelativeFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('/', '\\')).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

    private static bool IsMappingRule(string relative) =>
        relative.StartsWith("mapping-rules\\", StringComparison.OrdinalIgnoreCase) && relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string Summary(JsonNode? node) => node switch
    {
        JsonObject obj => $"{{{string.Join(", ", obj.Select(pair => $"{pair.Key}: {Summary(pair.Value)}"))}}}",
        JsonArray array => $"[{array.Count}]",
        _ => node?.ToJsonString() ?? "null",
    };
}

internal sealed class SharedRulesCase(string station, string store, string storeOff)
{
    public string Station { get; } = station;
    public string Store { get; } = store;
    public string StoreOff { get; } = storeOff;
    public string ModelFile { get; set; } = string.Empty;
    public string FloorType { get; set; } = string.Empty;
    public string CategoryOne { get; set; } = string.Empty;
    public string CategoryTwo { get; set; } = string.Empty;
    public CesiumRun? A { get; set; }
    public CesiumRun? B { get; set; }
    public CesiumRun? C { get; set; }
    public RulesStamp? StampA { get; set; }
    public RulesStamp? StampB { get; set; }
    public RulesStamp? StampC { get; set; }
}
