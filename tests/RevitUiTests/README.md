# RevitGeoSuite UI tests in Revit

These tests drive RevitGeoSuite inside a running Revit on the build PC, the way a person does: they click the ribbon and the page's buttons, answer the file dialogs, press Undo, and check what Revit and the page report. A run writes a report with pass or fail for each step, screenshots, and the Revit journal check. A person only looks at the screenshots.

There are eight scenarios. `--scenario` picks one, and `--script` is the same option.

- `gis-standard` (the default) is the GIS standard page test, run 2 of the M2/M3 Revit test. Steps 1 to 16 are automated. Step 15 uses a generated drift file until Kanizu writes a real one for the model.
- `floor-types` checks floor type standard 2 (RevitGeoSuite #52). Set up model leaves old-named floor types alone. The audit names each old name. Fix floor types renames the old names, and a shop type takes one of the two shop types. The export carries the new categories and restrictions. It needs a build with #52 installed.
- `b1` checks that Set up model adds the GIS export views (RevitGeoSuite #53): the `GIS Export` view template, a `GIS_` plan for each level with floors, the crops, and a `level_id` on every level. The default export then takes only the `GIS_` plans, and every stair and escalator unit has a `to_level`.
- `b2a` checks the export audit and the template repair (RevitGeoSuite #54): the findings of the topics ids, stairs, escalators, levels and views, Set up model putting back what someone changed in `GIS Export`, a `GIS_` plan without the template, and the duplicate id error of a floor pasted in place. It expects a model on which Set up model has run once, and runs it first when it has not.
- `c3a-reshape` checks that Review GDB edits reshapes a floor to the GDB outline (RevitGeoSuite #57). It makes its own drift file from its own export, so the build PC runs one command. With `--drift <file>`, it uses that file instead.
- `c3b-split`, `c3c-merge` and `c3d-hand-drawn` check that Review GDB edits splits a floor by the GDB's pieces, merges floors that the GDB merged, and creates a floor for a unit drawn by hand in the GDB (C3b, C3c, C3d). They prepare themselves the same way.
- `a3-fields` checks that the GeoPackage export writes elevator `levels_served`, the door fields and the GDB-owned field rows (RevitGeoSuite #45). It runs with whatever build `-RgsBuild` names.
- `c4-waiting` checks that GDB edits waiting in the station's drift file show on the GIS standard ribbon button ("· 1 GDB edit") and as a warning on the export page, and that both go once the edit is applied (C4). It makes its own station folder with a Cesium export, which gives the copy a model id, and copies a one-shape drift file into the folder's `gis-drift` folder under the model's key. On a PC where the Cesium export's first-run setup never ran, the scenarios that export to Cesium (`c4-waiting`, `c8-network`) first save a project profile named "UI test" (GIS_ plans, EPSG:6677, output in the run folder); `Run-UiTests.ps1` restores `profiles.json` when the run ends.
- `c8-network` checks the GIS standard page's Network tab, which shows Kanizu's walking network in Revit (C8). It makes its own station folder in the run folder with a Cesium export and writes the C8 sample network there, fitted to two adjacent levels of the model.

## How it works

| Part | What it does |
|---|---|
| `RevitGeoSuite.TestHost.Loader` | The DLL that the Revit manifest names. It holds no test logic. In a Revit started for a run, it loads the test host next to it. Otherwise it loads nothing. |
| `RevitGeoSuite.TestHost` | A test-only Revit add-in, installed on the build PC only. It listens on the named pipe `RevitGeoSuite.TestHost`, which only the signed-in user can open. No network port is opened. |
| `RevitGeoSuite.UiTestRunner` | A .NET 8 console app. It drives the web page with Playwright over WebView2's DevTools protocol, and drives Revit through the test host. |
| `RevitGeoSuite.UiTestRunner.Tests` | Unit tests for the runner's own logic. They run in CI on Linux and Windows. `ShellPageRpcTests` also sends bridge requests through `ShellPage` to a stand-in page that copies each message through JSON as WebView2 does; it runs only where a Chromium is found (`REVITGEOSUITE_TEST_CHROMIUM`, or `/opt/pw-browsers/chromium` in a cloud session, with `dotnet test -p:PlaywrightPlatform=linux`). |

The test host does nothing at all unless Revit was started with `REVITGEOSUITE_UITEST=1`. `Run-UiTests.ps1 -StartRevit` sets that variable for the one Revit it starts. In any other Revit session on the build PC, the add-in still loads but opens no pipe, adds no event hooks, and opens no DevTools port. When a Revit was started for tests, the test host does these things:

- It sets `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=0` inside that Revit process only. Other WebView2 apps on the PC keep their settings. Chromium picks a free port, binds it to 127.0.0.1, and writes the port to `%LOCALAPPDATA%\RevitGeoSuite\WebView2\EBWebView\DevToolsActivePort`. The runner reads the port from that file. The port stays open on localhost until that Revit closes.
- It opens the per-run model copy. It refuses any path outside the test folder: `C:\dev\revit-test`, or `REVITGEOSUITE_TESTHOST_ROOT` when that is set. A workshared copy is detached and saved once as its own central model inside the run folder, so it has a path and no tie to the original central model.
- It clicks ribbon buttons and Undo with `PostCommand`. This is exactly what a click does.
- It records every committed, undone, and redone transaction with its name, from `DocumentChanged`. A check such as "the Undo list names Kanizu GIS: Set up model" becomes an assertion on the `undo` event with that name.
- It records every dialog Revit raises. Only while a runner is connected does it answer TaskDialogs and message boxes with OK, so that a run cannot hang on one. `DialogPolicy` never answers a dialog about saving, overwriting, replacing, synchronising, worksharing, or the central model, in English or Japanese. Such a dialog stays on screen, and the run times out with a message saying so.
- It fills in Windows Open, Save, and folder dialogs only with paths inside the test folder: it types the path and presses OK. The runner deletes a target file before saving it, so no overwrite prompt appears.
- It reads the selection, the active view and its zoom, and parameters. It takes screenshots of the Revit window.
- It writes small fixtures into the per-run copy in one transaction named `UI test: fixture`: renamed floor types and set parameter values. It refuses to do this when the active document is outside the test folder.

The runner checks the active document after it opens the model and before every step. If the active document is not this run's copy in the run folder, or if a workshared copy is still tied to another central model, the runner stops the whole run.

The test code never reaches the installer. All four projects build into `bin\Tests\<project>\` (`tests\Directory.Build.props`). `install\build-release.ps1`, the installer and `install.ps1` build nothing themselves and pack only `bin\Deploy\`. The test host, the loader and the runner fail their build if their output path contains `Deploy`.

## One-time setup on the build PC

1. Stay signed in to the build PC desktop. Revit needs an interactive session to run.
2. Install the .NET 8 SDK if it is not there yet. Both scripts find it themselves: they try `DOTNET_ROOT`, then `%USERPROFILE%\.dotnet`, then each `dotnet` on PATH, then `C:\Program Files\dotnet`, and use the first that lists a .NET 8 or later SDK. A `dotnet` on PATH that is only a runtime is skipped. When none has the SDK, the script stops and names what it tried. To pick an install, set `DOTNET_ROOT` to its folder.
3. Close Revit. Then run `tests\RevitUiTests\Install-TestHost.ps1` in a clone of this repo. It copies the loader, the test host and their manifest to `%APPDATA%\Autodesk\Revit\Addins\2024\RevitGeoSuite.TestHost\`. Revit reads manifests only at the top of `Addins\2024`, so it does not see this one there.
4. At the first `Run-UiTests.ps1 -StartRevit`, answer **Always Load** for "RevitGeoSuite test host". Revit asks this once for an unsigned add-in.

Nothing of the tests stays where Revit or another PC looks. Daniel's profile roams, so everything under `%APPDATA%` follows him to his other PCs. `Run-UiTests.ps1 -StartRevit` copies the manifest to the top of `Addins\2024`, starts Revit, waits until the test host's pipe is up, and deletes the manifest again. If the pipe is not up within `-StartMinutes` (5), or Revit exits first, the script writes a FAIL report with the journal's dialog lines into a run folder of its own, closes Revit and exits 1, so a modal start-up prompt never leaves a run hanging. A run that was stopped half way leaves no manifest for long: the next run, and `Install-TestHost.ps1`, delete any manifest they find. Another PC therefore only ever receives the DLLs, which no manifest names, and a Revit started by hand does not load the test host. Even when loaded, the loader does nothing unless that Revit was started with `REVITGEOSUITE_UITEST=1`. It references only `mscorlib` and `RevitAPIUI`.

Why not `%LOCALAPPDATA%`, which does not roam: on the build PC, Revit reported the loader there as not found although the file was present (journal: `TaskDialog_External_Tools_Add_In_Assembly_Not_Found`), while DLLs under `%APPDATA%` load. `-Target` installs elsewhere, and `Run-UiTests.ps1 -TestHost` then names the same folder.

The answer covers the DLL that the manifest names, which is the loader. The loader does not change when the test host changes. It builds deterministically, without a PDB, so two builds of the same source give the same bytes. `Install-TestHost.ps1` also leaves an installed loader of the same FileVersion untouched. Rebuilding and reinstalling the test host therefore needs no new click. The script prints whether the loader was left as it is or newly installed. Only a new loader FileVersion, which follows a change to the loader's source, makes Revit ask once more. Each report's header names the test host build that ran.

A RevitGeoSuite build that is not installed, such as a PR build, never gets a manifest. `Run-UiTests.ps1 -StartRevit -RgsBuild <build folder> ...` copies it to `C:\dev\revit-test\rgs\2024` (`-RgsTarget` for another folder), and the test host starts `RevitGeoSuite.Shell.App` from there inside that Revit, as Revit would from a manifest. Revit asks Always Load only for a DLL that a manifest names, and the only one is the trusted loader, so no RevitGeoSuite build ever prompts. The report header says which build started and from where. The script refuses while RevitGeoSuite is installed, in `%ProgramData%` or `%APPDATA%`, because two copies would build the same ribbon. Do not install a test build into `%APPDATA%\Autodesk\Revit\Addins` with a manifest: it would roam to other PCs and load there.

A pyRevit extension folder for the tests goes the same way: `-PyRevitExtension <folder>` adds it to `userextensions` in `%APPDATA%\pyRevit\pyRevit_config.ini` (which roams) just before Revit starts and takes it out again with the manifest. pyRevit reads the list at Revit start, so a pyRevit reload during the run drops the folder. Every run also removes entries under `C:\dev\rgs-uitests` and `C:\dev\revit-test` from that list, because those folders exist only on the build PC.
5. Make a pristine detached copy of the test model at `C:\dev\revit-test\auto\lumine-pristine.rvt`. The copy must never have been set up with the GIS standard. Never use the original or the central model, and never save over the pristine copy.

To remove the test host, close Revit and run `Install-TestHost.ps1 -Uninstall`. That deletes the copied DLLs and their manifest, and any manifest a stopped run left in `Addins\2024`.

## Run

Close Revit first. The runner only reaches a Revit that `-StartRevit` started.

```powershell
cd C:\dev\RevitGeoSuite
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --model C:\dev\revit-test\auto\lumine-pristine.rvt --export-view "1F" --excel
```

The report is at `C:\dev\revit-test\auto\<date-time>\report.md`. The run folder also holds the screenshots, the CSVs, and the model copy. The exit code is 0 when no step failed.

- **A stopped run fails.** When the run stops early, for example because the active model is not the run's copy, the report gets a failed `STOP` step, says FAIL, and the exit code is 1.
- **Revit closes on every exit.** `-StartRevit` closes the Revit it started when the runner ends, however it ends. It stops the process, which closes without saving and cannot meet a "save changes?" prompt.
- **Revit starts in `C:\dev\revit-test`.** Revit names a detached model `<name>_detached.rvt` without a folder. The test host therefore saves the detached copy once, by full path, as `<copy>-detached.rvt` in the run folder.
- **Missing links are unloaded first.** Before the model is opened, every link whose file is not on the PC is marked not to load in the run's copy, so Revit has no "could not find or read" question to ask. The report header lists those links, and any dialog Revit showed while opening.
- **Exports write only to the run folder.** The GeoPackage export page starts on the folder it remembers from the last export, which can be someone's own data. Every export types its own folder under the run folder into the output field (Browse... and the folder picker are the fallback) and reads the field back. When the field shows anything else, the step fails before Preview, so nothing is exported. The page keeps that folder in the roaming `%APPDATA%\RevitGeoSuite\FloorPlanExport\settings.json`; With `-StartRevit`, `Run-UiTests.ps1` empties `settings.json` (`{}`) before Revit starts, so the page shows a first-time user's defaults: no remembered views, folder or options, and the default view choice (the model's `gis-model.json`, else every `GIS_` plan). It puts that file and `profiles.json` back as they were when it exits. Profiles are kept for the run, because the Cesium export uses the PC's first floor plan profile.
- **Undo and Redo wait for Revit.** Revit refuses a posted Undo in some runs (b1 and b2a) and accepts it in others (c3a), with the same view active. The cause is outside the model. When Revit refuses, the test host brings Revit's main window to the front (joining input to the window in front when Windows refuses, since on the build PC the Claude app holds the foreground) and posts again, then presses the Quick Access Toolbar's Undo or Redo button through UI Automation. The runner retries this for up to 15 s. The last error names Revit's state: the active document (modifiable, read-only, workshared, detached, modified), the open documents, whether Redo and Default 3D View can be posted, the last transactions the test host saw, and the journal's last command lines. In b1, b2a and c3a a refusal is a note, not a failure. The step then checks that the transaction committed as one transaction, checks the model as it is, and puts back with a fixture what Undo would have taken back.
- **Known product failures.** A check that RevitGeoSuite fails until a named PR lands reports "known product failure" and does not fail the run. B1.8's to_level warning is one, until the B3b PR.
- **Fixtures raise no warning dialogs.** The `fixture` transaction takes Revit's warnings off at commit (a floor copied in place warns about identical instances) and lists them in its log; an error rolls the fixture back and fails the call.
- **A step leaves no dialog behind.** After every step the runner closes any dialog still open in Revit (Cancel, or the window's close) and, after a failure, takes the page back to the GIS standard page. A modal dialog holds Revit's API thread, so without this every later step would time out. A step that left a dialog open fails, and the report lists what was closed.
- **No pyRevit test extension by default.** The KanizuGIS pyRevit extension shows a startup dialog on a model that is not set up, so a run loads it only when `-PyRevitExtension` is passed.

| Option | Meaning |
|---|---|
| `--scenario <name>` | `gis-standard` (the default), `floor-types`, `b1`, `b2a`, `c3a-reshape`, `c3b-split`, `c3c-merge`, `c3d-hand-drawn`, `a3-fields`, `c4-waiting` or `c8-network`. `--script` is the same option. |
| `--drift <file>` | `c3a-reshape` only: the drift file to reshape from. It must be under the test folder. Without it, the scenario writes one from its own export. |
| `--model <path>` | The pristine copy. Required. It must be under the test folder. |
| `--out <folder>` | The run folder. The default is `C:\dev\revit-test\auto\<date-time>`. |
| `--steps 1,2,10` | Runs only these steps. The steps build on each other, so a subset is for debugging. |
| `--export-view <name>` | The plan view for step 16. Without it, step 16 does not run. In `floor-types`, it limits step F6 to this view. Without it, F6 exports all plan views. In `b2a`, it is the view step B2.8 exports. Without it, B2.8 keeps the default view choice. |
| `--baseline-export <folder>` | `b1` only: an export of the same views made before Set up model, with the run 2 build. Step B1.9 compares the row count of every feature table with it. Without it, B1.9 does not run. |
| `--excel` | Step 10 also saves the CSV again through the real Excel, then reads it back. Needs Excel. |
| `--real-drift` | Step 15 uses the model's own Kanizu drift file instead of a generated one. |
| `--rename-type "a=>b"` | Before step 6, renames floor type a to b, so the name rules match it. This happens only when fewer than two proposals start ticked, as on floor type standard 1. Repeat the option for more renames. The defaults are the two renames from run 2. |
| `--revit-year <year>` | The Revit version whose journals are checked. The default is 2024. |

## What each step checks

| Step | Check |
|---|---|
| 1 | The ribbon button opens the page. It has the summary strip and the three cards, and no error banner. |
| 2 | The Set up model card offers "Apply N change(s)" with the same N as `gisStandard.inspect`. |
| 3 | The banner appears, a `commit` event named `Kanizu GIS: Set up model` arrives, the version reads `1 / 1`, and the card says "Nothing to set up". |
| 4 | Undo gives an `undo` event with that name. After Check again, the preview is the same as in step 2. |
| 5 | A second apply, then Check again, reports nothing to do. |
| 6 | If needed, the fixture renames two floor types first. One pre-ticked row is unticked, then "Apply to N type(s)": the banner, the commit event, the fixed types leave the table, the unticked one stays, and the floor type findings drop by N. |
| 7 | Undo gives the `undo` event. The proposals are back. |
| 8 | An audit row's Select button and an unmatched type's Select button each select exactly those elements in Revit. The active view, the zoom, and a Revit screenshot are recorded. |
| 10 | Write CSV saves through the Save dialog. The file has a UTF-8 byte order mark, and the row count matches the page. With `--excel`, an Excel round trip reads back with no changes. |
| 11 | The runner edits the CSV the way step 11 of the script does and saves it as Japanese Excel does (Shift-JIS). The preview has 3 changes, shows the Japanese name, and lists `'shop' is not a unit category`. |
| 12 | Writing all but one change: the banner, the commit event, and the Japanese name on the floor. Undo restores the names. Reading the same edits saved as UTF-8 proposes them again. |
| 13 | The GDB edits tab shows rows, or says why there are none. |
| 14 | Picking the conformance drift file for another model shows the warning, and the accept button is disabled with 0 changes. A copy that was never exported has no model id, so the page says "picked by hand" instead. The report notes which one it saw. |
| 15 | A one-row drift file for a floor of this copy (or the real one, with `--real-drift`) is accepted. The floor's name changes, the `Kanizu GIS: Review GDB edits` commit and undo arrive, and the row is proposed again. |
| 16 | If the audit has no errors, a fixture gives one floor an unknown category. The export dialog lists the errors, with "Export anyway" unticked and its button disabled. After the dialog is closed, the box is unticked again. Exporting anyway records "Exported with N GIS audit error(s) accepted". |
| 9 | Runs last. With a new family active, the ribbon button shows "Open a project model …". |
| J | The journals written during the run contain every committed transaction name, and no `HANDLER_ERROR`, unhandled exception, or `Revit did not commit` line. |

## The floor-types script

| Step | Check |
|---|---|
| F1 | Set up model plans and creates none of j 喫煙所_床, j ベビー休憩室_床, j 屋外_床, j ラチ外店舗_床, or j ラチ内店舗_床 while the old-named type is in use. The model goes to standard version 2. j その他_床 has gis_category nonpublic and an empty gis_restriction. |
| F2 | There is one "uses an old name" warning per old-named type in use, naming the new name. The j 店舗_床 warning names both shop types and says that all its floors follow the choice. If j 店舗_床 already has a gis_restriction, it names only that side, and the card shows one row for it. |
| F3 | smoking room, Baby room, and Roof are proposed as old names that rename to the new names with the right categories, and start ticked. The two j 店舗_床 rows start unticked, and ticking one unticks the other. Ticking j ラチ外店舗_床 and applying gives the banner, the commit event, and a "Renamed X to Y" line for each type. |
| F4 | Each renamed type keeps its element id and its floor count in Revit. |
| F5 | After Check again, no old-name warning is left and nothing more is proposed for those types. Set up model now offers to create j ラチ内店舗_床. |
| F6 | The export's unit tables have shop units with restriction rachi_gai and roof units with category unenclosedarea. |
| F7 | Not run: the pyRevit Fix floor types dialog is still a manual check. |

## The b1 script

Each step id starts with `B1.`, so `--steps 1,B1.2,B1.3` runs those steps. Step 1 opens the page and is always needed.

| Step | Check |
|---|---|
| B1.1 | Records the `GIS_` plans with their levels, crops and templates, whether `GIS Export` exists, the levels that hold floors, and the levels without a `level_id`. |
| B1.2 | The Set up lines hold `Create view template GIS Export` (unless the model has it), a `Create plan GIS_… for level <level>` line for each level with floors and no `GIS_` plan, a `Crop plan … to the floors` line for each `GIS_` plan with its crop off, and `Give N level(s) a level_id: …`. `The crop holds every floor plus 5 m: about W m by H m` is under 1 km each way. |
| B1.3 | Apply gives the banner and the `Kanizu GIS: Set up model` commit. The change list has `Created view template GIS Export`, a `Created plan` line for each new plan, a `Cropped plan` line for each crop, and `Gave N level(s) a level_id`. |
| B1.4 | After Check again, no view, crop or `level_id` line is left. |
| B1.5 | `GIS Export` is Coarse, Coordination, cut plane 1200 mm, bottom and view depth 0 at the level. Each new `GIS_` plan uses it, with its crop on. Every level has a `level_id`, and every level with floors has a `GIS_` plan. |
| B1.6 | Opens two `GIS_` plans in Revit and takes a screenshot of each. A person checks them. |
| B1.7 | One Undo gives the `undo` event, and the B1.1 Set up lines are back. Redo gives the `redo` event, and nothing about views is left. |
| B1.8 | The export page preselects only `GIS_` plans, and all of them. The export finishes, and every stair and escalator unit has a `to_level`. The report lists the counts with and without one, and the rows per table. |
| B1.9 | With `--baseline-export`, every feature table has the same row count as the baseline, or the report lists the differences. |

## The b2a script

| Step | Check |
|---|---|
| B2.0 | If Set up model has something to do, it runs, so the model is as B1 leaves it. |
| B2.1 | Records each finding of the topics ids, stairs, escalators, levels and views: severity, message and the number of elements. There is no views finding. |
| B2.2 | For the first finding of each topic that has elements, its Select button selects exactly those elements in Revit. A Revit screenshot of the first one. |
| B2.3 | A fixture hides Floors, shows the line style `<Thin Lines>` (`<細線>`) and shows Walls in `GIS Export`. The Set up lines hold `Update view template GIS Export: …`, which shows Floors, hides Thin Lines and says nothing about Walls. A views warning names the same changes. |
| B2.4 | Apply gives the commit, and the change list has `Updated view template GIS Export: …` with the same changes. In the template, Floors are shown, Thin Lines hidden, and Walls still shown. |
| B2.5 | After Check again, there is no `Update view template` line, no views finding, and nothing to set up. |
| B2.6 | One Undo puts the template back as B2.3 left it (Floors hidden). Redo shows Floors again. |
| B2.7 | A fixture sets one `GIS_` plan's view template to None. A views finding names the plan, and no Set up line mentions it. Undo gives the template back. |
| B2.8 | A fixture copies a floor onto itself, as Paste Aligned to Same Place does. If the floor has no `id`, the same fixture gives it one first, because a copy only shares an id its original has. The audit has one ids error `2 elements share id …` with both element ids. The export asks about the audit errors, and the dialog is closed. Undo removes the copy, and the error is gone. |
| B2.9 | Records each stairs and escalators finding. A person judges whether each message is right. |

Not checked: a template that someone else made and named `GIS Export` is taken as Set up's own template. Revit view names are unique, so there is only ever one, but the runner cannot tell them apart.

## The c3a-reshape scenario

```powershell
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --scenario c3a-reshape --model C:\dev\revit-test\auto\lumine-pristine.rvt
```

The scenario does the check list's Prepare part itself. It exports the GeoPackage with the default view choice to `<run>\export`. It reads the floor units of that export and picks the first retail floor, or else the first walkway floor, whose sketch has no arc, no extra sketch line and no shape-edited points. It then writes `<run>\reshape.gis-drift.json` with that floor's east edge 1.0 m further east. The runner has its own copy of `make_reshape_fixture.py` in `ReshapeFixture.cs`, so the build PC needs no Python. On the same export, both give the same drift file.

| Step | Check |
|---|---|
| C3a.0 | If Set up model has something to do, it runs, so each level has a `GIS_` plan. |
| C3a.P | The export, the drift file, and floor F: its UniqueId, element id, type and bounding box. |
| C3a.1 | Picking the file shows "Shape edits (1)". The card names F's element id, and the box of its dashed outline is larger than the grey one's. `gisDrift.inspect` lists no problem about shapes. |
| C3a.2 | "Show in Revit" makes the `GIS_` plan of F's level the active view, and no transaction commits. A Revit screenshot shows the outlines. |
| C3a.3 | "Hide in Revit": no transaction, and a Revit screenshot without the outlines. |
| C3a.4 | "Apply 1 shape edit(s)", confirming the dialog if one opens. The banner reads "1 floor(s) reshaped". A commit named `Reshape floor <id> (<label>) to the GDB outline` arrives, and no commit has another name. F keeps its UniqueId, element id and type, and its box changes. The box change is recorded in model feet. The project CRS east the fixture grew is not model X on a rotated model, so the 1 cm check is in C3a.7. |
| C3a.5 | The shape edit card is gone after the apply, and after another Check again. |
| C3a.P, C3a.4 | Floor F's sketch profile loops and top-face loops (model feet, tessellated), the project position of the model axes, and the drift file go to `C3a-floor-F-geometry.json` in the run folder, after C3a.P and again just before Apply. C3a.9 writes the same for floor H, with the shrink drift file, to `C3a-floor-H-geometry.json`. |
| C3a.6 | One Undo gives the `undo` event of that name, and F's box is back. |
| C3a.7 | Redo, then an export to `<run>\export2`. F's exported outline is the GDB outline within 1 cm. This is the largest distance from a vertex of either outline to the other's edges. |
| C3a.8 | A floor with a slope arrow or another extra sketch line, given a reshape, shows up in the problems with that reason, and no card appears. Not run when no exported floor has one. |
| C3a.9 | This step finds a floor H with an opening or a hosted family within 1 m of its east edge. If no floor has one, a fixture cuts a 0.4 m opening into a retail floor, inside the 1 m band along its east edge. The step cuts a 1 m strip along H's east side off the export polygon and applies it. Holes are kept only when they lie wholly west of the cut, because a hole the strip reaches is no longer inside the outline. The report says how often the outline crosses the cut and how many holes were dropped. When the page shows no shape edit card, the report lists the problems instead. When an opening lies in the strip, Revit would refuse the outline ("Can't make cut-out"), so the step expects RevitGeoSuite #57's problem line naming the opening, no card, no transaction and every dependent still there. The banner must read "1 floor(s) reshaped". Each "Revit deleted … <id> …" line names an element that is gone, and no element still there is listed. The report records what Revit did to each dependent (deleted, or kept) and each "Revit warned:" line. Undo brings every dependent back. |
| C3a.10 | A floor with an arc in its sketch, given a reshape, shows up in the problems with the curved-edges reason. No card appears, and the floor does not change. Not run when no exported floor has an arc. |

East is the project CRS east the export writes. The test host computes it from the active project location, the same way RevitGeoSuite's `SharedCoordinateProjector` does.

## The c3b-split, c3c-merge and c3d-hand-drawn scenarios

```powershell
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --scenario c3b-split --model C:\dev\revit-test\auto\lumine-pristine.rvt
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --scenario c3c-merge --model C:\dev\revit-test\auto\lumine-pristine.rvt
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --scenario c3d-hand-drawn --model C:\dev\revit-test\auto\lumine-pristine.rvt
```

Both export to `<run>\export` and write their drift files with `ReshapeFixture.cs`. That file gives the same JSON as `make_reshape_fixture.py --split`, `--merge-with` and `--hand-drawn [--no-type]`.

| Step | Check |
|---|---|
| C3b.P | Picks the first retail floor, else the first walkway floor, that has no holes, can be redrawn, and splits into two pieces over 1 m² each. It cuts the floor at the middle of its east-west extent. The east piece carries `gdbName` "<name> east", `gdbAltName` "E" and the unit's category. It records F's id, type, level, height offset, area, `name`, `short_name`, `category`, `id`, `gis_key`, and the number of floors on F's level. |
| C3b.1 | One card reads "Split floor <id> into 2 floors", with two dashed pieces. |
| C3b.2 | "Show in Revit" makes the `GIS_` plan of F's level active, with no transaction, and takes a Revit screenshot. |
| C3b.3 | Apply. The banner appears, and a "New floor <id> …" line is in its list. If Revit refuses, the step fails and quotes the page's error. |
| C3b.4 | F keeps its UniqueId, and its area is the west piece's within 1 %. The new floor has the east piece's area, sits on F's level with F's height offset, and is one more floor on that level. Its `name` is "<F's name> east", its `short_name` is "E", and its `category` is F's. Its `id` and `gis_key` are not F's. If the result names a missing standard type, the new floor has F's type. Otherwise the report records the type it took. |
| C3b.5 | Check again offers nothing. |
| C3b.6 | One Undo gives the `undo` event of the split, removes the new floor and gives F its old outline. |
| C3b.7 | Redo, then export to `<run>\export2`. F's unit is the west piece within 1 cm, and the unit keyed by the new floor is the east piece within 1 cm. |
| C3c.P | Picks floors K and G on one level. G hosts nothing. The step prefers two retail floors, and among those, a pair that touches. K keeps its own outline, and G is taken in. |
| C3c.1 | One card reads "Merge 2 floors into floor <K id>". |
| C3c.2 | "Show in Revit" on K's level, with no transaction, and a Revit screenshot. |
| C3c.3 | Apply. The list has "Deleted floor <G id>.". |
| C3c.4 | G is gone. K keeps its UniqueId, element id and box. The level has one floor fewer. |
| C3c.5 | Check again offers nothing. |
| C3c.6 | One Undo gives the `undo` event of the merge, and G is back with its UniqueId. |
| C3c.7 | A merge that takes in a room is listed as "element <id> is not a floor, so the merge does not delete it. Merge it in Revit by hand.", with no card and no change. |
| C3c.8 | A fixture cuts an opening in G, and the same merge is applied again. G and the opening are deleted, the result names the opening, and one Undo brings both back. |

| C3d.P | Picks the first retail floor, or else walkway floor, F that hosts nothing. It deletes F through the test host, because a floor already there counts as the unit drawn before. It writes F's unit as drawn by hand, as "<name> (drawn)", once with its category and restriction and once without. |
| C3d.1 | One card reads "New floor drawn in the GDB", its "Floor type" select already has a type, and Apply is enabled. |
| C3d.2 | "Show in Revit" on F's level, with no transaction, and a Revit screenshot. |
| C3d.3 | Apply. The list has "New floor <id> (<type>). The next Kanizu apply pairs the hand-drawn unit with it.", and a commit is named "Create a floor for the hand-drawn unit <name> (drawn) from the GDB". |
| C3d.4 | The new floor is on F's level, has the unit's area within 1 %, and has the type that the log and the card named. Its level has one floor more. |
| C3d.5 | Check again offers nothing. |
| C3d.6 | One Undo removes the new floor. |
| C3d.7 | Redo, then export to `<run>\export2`. The unit keyed by the new floor is the drawn outline within 1 cm. |
| C3d.8 | The file without a category: the card has no type, and Apply stays disabled. Picking the first type enables Apply, and the new floor gets that type. Undo. |
| C3d.9 | A drawn unit on a level_id that no level has is listed as "unit <name> handDrawn: no level of this model has level_id …, so there is no level to draw it on. Draw it in Revit by hand.", with no card and no change. |

Each Revit assumption in the C3b evidence table is checked by a step:

| Assumption | Step |
|---|---|
| `SketchEditScope` keeps the floor's UniqueId | C3a.4 |
| `SketchEditScope` can run inside a `TransactionGroup` | C3b.3, C3c.3, and the one-Undo checks C3b.6 and C3c.6 |
| `Floor.Create` with GDB curve loops gives the piece's outline within 1 cm | C3b.4 and C3d.4 (area), C3b.7 and C3d.7 (outline) |
| DirectContext3D lines show in the `GIS_` plan | C3a.2, C3b.2, C3c.2, C3d.2 (screenshots, checked by eye) |
| Deleting a merged floor deletes its hosted elements, and nothing blocks it | C3c.8 |

The commit events of each apply are recorded. One undo step is proven by pressing Undo once, not by counting events. Revit can raise one DocumentChanged event per inner transaction of an assimilated group.

## The a3-fields scenario

From RevitGeoSuite #45 head c1c09d0 on, `gis_levels_served` is an instance parameter (Generic Models, Specialty Equipment, Mechanical Equipment): A3.1 empties it and A3.3 fills it on the elements of E's shaft and on E itself, and rule C checks that every other shaft keeps its span value.

The spec is `/mnt/project-files/revit-template-review/a3-ui-scenario.md`.

```powershell
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit -RgsBuild <#45 build> --scenario a3-fields --model C:\dev\revit-test\auto\lumine-pristine.rvt
```

| Step | Checks |
| --- | --- |
| A3.0 | Runs Set up model when it has changes, and records its log. |
| A3.A | `gis_levels_served` is a type parameter on Generic Models and Specialty Equipment. `gis_door` and `gis_access_con` are type parameters on Doors. `name` is an instance parameter that includes Doors. |
| A3.1 | Lists every elevator object by shaft, with its family, type, level and box in metres. An elevator object is a family instance whose type has `gis_kind` or `gis_category` set to `elevator`. A shaft is a group of objects whose plan boxes overlap by at least half of the smaller box, joined transitively, as #45 does. The step flags two objects on the same level whose boxes overlap, which would mean two cars were joined. E is the lowest object of the first shaft that has objects on two or more levels. `gis_levels_served` is emptied on that shaft's types. |
| A3.2 | Exports `span` to `<run>\a3\span`: every `Copy 1` plan view, Per level GeoPackage, Shared coordinates, EPSG 6677. Records the elevator warnings and the warnings about missing door fields. |
| A3.3 | The test door is the first door, by element id, that the span export writes as an opening. Its type gets `gis_door` = `automatic` and `gis_access_con` = `test`, and the door gets `name` = `A3 test door`. E's type gets `gis_levels_served` = the two lowest exported levels plus `ZZ`. |
| A3.4 | Exports `override` with the same settings. |
| A3.B to A3.F | Pass rules B to F of the spec. B (span) checks that every row of a shaft has the same value. That value is the shaft's exported levels, lowest first, or it is empty, with the "no elevator object in the same plan spot" warning, for a lone object. C (override) checks every row of E's shaft. D reads `override`. E (the `Schema field '…' … could not resolve its mapped source` warnings) and F read both exports. |

The scenario fills the door after the span export, not before it as the spec has it. Only an export says which doors are written as openings, so rules D and E read the override export. Level ids and elevations come from the model's levels: their `level_id` and their project elevation.

## The c8-network scenario

The script is `/mnt/project-files/revit-template-review/c8/ui-tests/c8-network.md`. It needs a RevitGeoSuite build with the Network tab and a floor plan export profile on the PC, which the Cesium export uses.

```powershell
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --scenario c8-network --model C:\dev\revit-test\auto\lumine-pristine.rvt
```

C8.G gives the copy RevitGeoSuite's georeference record, which the Cesium export needs ("3D Tiles export needs a saved CRS and origin"). The GeoPackage export does not read that record, so the other scenarios do not need it; the status bar's "Blocked · Missing geo metadata" does not stop a GeoPackage export. C8.G does what "Confirm existing setup" on the Georeference page does, with EPSG:6677: RevitGeoSuite stores the CRS with the Survey Point and Project Base Point that the model's Site Location and shared coordinates already give, and leaves Revit's coordinates unchanged. The step checks that the Project Base Point is within 5 km of Shinjuku station first, and that the project position and both points are the same afterwards. When the Site Location is not LUMINE's, the step fails with the values it read.

C8.P exports GIS to `<run>\export`. It then exports to Cesium as a station folder at `<run>\station`, coarse, with the first floor plan profile, through the page's bridge. That export gives the model a model id and records `<run>\station` as its station folder, so nothing is written to a real station folder. Next, it picks the adjacent pair of levels, A below B, that both have a `level_id` and a `GIS_` plan, preferring the pair whose lower level has the most floor units. It writes `fixtures\network-sample.gis-network.json` to `<run>\station\gis-network\<model key>.gis-network.json`, with these changes:

- `lvl-1f` becomes A and `lvl-2f` becomes B. `lvl-b1` stays unmatched.
- `epsg` becomes the export's EPSG.
- Every position moves so that the sample's (-7130, -34560) lands on the middle of A's largest floor unit.

The sample file is a copy of the C8 conformance sample, until Kanizu writes the file. The expected counts come from the fitted file. For the sample they are "3 link(s), 3 level connector(s) and 1 finding(s) on a floor." and "3 item(s) sit on a level_id …".

| Step | Checks |
| --- | --- |
| C8.1 | The Network tab shows the counts line and the unknown-level line. |
| C8.2 | Show in Revit with `GIS_<A>` active: the button turns into Hide in Revit, and no transaction is committed. A Revit screenshot shows the result for a person to judge. |
| C8.3, C8.4 | `GIS_<B>` and a 3D view (a `GIS_*` 3D view if there is one): no transaction, plus Revit screenshots. |
| C8.5 | GIS and Cesium exports with the network shown have the same GeoPackage row counts as those before Show. The tile file counts are recorded. |
| C8.6 | Link 3 (B's link) is dropped from the file. Switching to A and then B needs no Read again. The screenshots show the result. |
| C8.7 | `epsg` + 1 and Read again: a page line names both EPSG codes. |
| C8.8 | `version: 2` and Read again: the banner "… is version 2; this tool reads version 1. Update RevitGeoSuite.". The file is then put back. |
| C8.9 | The audit has two `network` warnings. The first names no element. |
| C8.10 | Hide in Revit: the button turns back, no transaction is committed, and screenshots of A and B show the result. |

Showing the network adds no element, because adding an element needs a transaction, and the steps check that none was committed. A person judges what is drawn from the screenshots.

## The georef-read scenario

`--scenario georef-read --model <file>` opens a copy of the model in the run folder, like every scenario, and writes RevitGeoSuite's georeference record and Revit's Site Location, project position, Survey Point and Project Base Point to the report. It changes nothing. Use it on the georeferenced LUMINE file to compare with what C8.G records on the pristine copy.

## What still needs a person

- Looking at the screenshots. The text checks are automated, but layout and look are not.
- The one-time setup above, the Revit sign-in, and the licence prompts.
- Anything on the original or central model.
- The pyRevit tab's dialogs (step F7).

## Adding a scenario

A scenario is one entry in `GisStandardRun.Scenarios` (name, one-line summary, steps method) and a partial file with its steps, like `ReshapeRun.cs`. `--scenario`, the usage text and the name check all read that list. Step 1 (open the page) and the English page come with every scenario. Give step ids a prefix, such as `C3b.1`, so `--steps` can pick them. To reuse a shared step, call it from the new steps method, as `c3a-reshape` does with Set up model (`EnsureSetUp`), `OpenExport` and `ExportNow`.

## Adding a test

Most checks are a click on the page plus an assertion on what `gisStandard.inspect` or the test host reports. Prefer the role and the visible English text of a control. The runner switches the page to English for the run and switches it back afterwards. When a check needs Revit in a particular state, add it to the `fixture` command rather than editing the pristine model.

The test host runs code in an API context only while Revit is idle. While a file dialog or another modal window is open, use the commands that work on dialogs (`fileDialog`, `windows`, `pressButton`, `closeDialogs`).
