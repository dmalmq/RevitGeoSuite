# RevitGeoSuite UI tests in Revit

These tests drive RevitGeoSuite inside a running Revit on the build PC, the way a person does: they click the ribbon and the page's buttons, answer the file dialogs, press Undo, and check what Revit and the page report. A run writes a report with pass or fail for each step, screenshots, and the Revit journal check. A person only looks at the screenshots.

The first script is the GIS standard page test (run 2 of the M2/M3 Revit test). Steps 1 to 16 are automated. Step 15 uses a generated drift file until Kanizu writes a real one for the model.

## How it works

| Part | What it does |
|---|---|
| `RevitGeoSuite.TestHost` | A test-only Revit add-in, installed on the build PC only. It listens on the named pipe `RevitGeoSuite.TestHost`, which only the signed-in user can open. No network port is opened. |
| `RevitGeoSuite.UiTestRunner` | A .NET 8 console app. It drives the web page with Playwright over WebView2's DevTools protocol, and drives Revit through the test host. |
| `RevitGeoSuite.UiTestRunner.Tests` | Unit tests for the runner's own logic. They run in CI on Linux and Windows. |

The test host does these things:

- At Revit start-up, it sets `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=0` inside the Revit process only. Other WebView2 apps on the PC keep their settings. Chromium picks a free port, binds it to 127.0.0.1, and writes the port to `%LOCALAPPDATA%\RevitGeoSuite\WebView2\EBWebView\DevToolsActivePort`. The runner reads the port from that file.
- It opens the per-run model copy, detached from central if it is workshared. It refuses any path outside the test folder: `C:\dev\revit-test`, or `REVITGEOSUITE_TESTHOST_ROOT` when that is set.
- It clicks ribbon buttons and Undo with `PostCommand`. This is exactly what a click does.
- It records every committed, undone, and redone transaction with its name, from `DocumentChanged`. A check such as "the Undo list names Kanizu GIS: Set up model" becomes an assertion on the `undo` event with that name.
- It records every dialog Revit raises. While a run is connected, it answers TaskDialogs and message boxes with OK, so a run cannot hang on one.
- It fills in Windows Open, Save, and folder dialogs: it types the path and presses OK.
- It reads the selection, the active view and its zoom, and parameters. It takes screenshots of the Revit window.
- It writes small fixtures into the per-run copy in one transaction named `UI test: fixture`: renamed floor types and set parameter values.

Nothing is ever saved. Each run copies the pristine model into its own output folder and opens the copy.

## One-time setup on the build PC

1. Stay signed in to the build PC desktop. Revit needs an interactive session to run.
2. Install the .NET 8 SDK if it is not there yet (`dotnet --list-sdks`).
3. Close Revit. Then run `tests\RevitUiTests\Install-TestHost.ps1` in a clone of this repo.
4. Start Revit 2024 once by hand. Answer **Always Load** for "RevitGeoSuite test host". Revit asks this once for an unsigned add-in.
5. Make a pristine detached copy of the test model at `C:\dev\revit-test\auto\lumine-pristine.rvt`. The copy must never have been set up with the GIS standard. Never use the original or the central model, and never save over the pristine copy.

To remove the test host, run `Install-TestHost.ps1 -Uninstall`.

## Run

```powershell
cd C:\dev\RevitGeoSuite
.\tests\RevitUiTests\Run-UiTests.ps1 -StartRevit --model C:\dev\revit-test\auto\lumine-pristine.rvt --export-view "1F" --excel
```

The report is at `C:\dev\revit-test\auto\<date-time>\report.md`. The run folder also holds the screenshots, the CSVs, and the model copy. The exit code is 0 when no step failed.

| Option | Meaning |
|---|---|
| `--model <path>` | The pristine copy. Required. It must be under the test folder. |
| `--out <folder>` | The run folder. The default is `C:\dev\revit-test\auto\<date-time>`. |
| `--steps 1,2,10` | Runs only these steps. The steps build on each other, so a subset is for debugging. |
| `--export-view <name>` | The plan view for step 16. Without it, step 16 does not run. |
| `--excel` | Step 10 also saves the CSV again through the real Excel, then reads it back. Needs Excel. |
| `--real-drift` | Step 15 uses the model's own Kanizu drift file instead of a generated one. |
| `--rename-type "a=>b"` | Step 6 renames floor type a to b first, so the name rules match it. Repeat it for more renames. The defaults are the two renames from run 2. |
| `--revit-year <year>` | The Revit version whose journals are checked. The default is 2024. |

## What each step checks

| Step | Check |
|---|---|
| 1 | The ribbon button opens the page. It has the summary strip and the three cards, and no error banner. |
| 2 | The Set up model card offers "Apply N change(s)" with the same N as `gisStandard.inspect`. |
| 3 | The banner appears, a `commit` event named `Kanizu GIS: Set up model` arrives, the version reads `1 / 1`, and the card says "Nothing to set up". |
| 4 | Undo gives an `undo` event with that name. After Check again, the preview is the same as in step 2. |
| 5 | A second apply, then Check again, reports nothing to do. |
| 6 | The fixture renames two floor types. One row is unticked, then "Apply to N type(s)": the banner, the commit event, the fixed types leave the table, the unticked one stays, and the floor type findings drop by N. |
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

## What still needs a person

- Looking at the screenshots. The text checks are automated, but layout and look are not.
- The one-time setup above, the Revit sign-in, and the licence prompts.
- Anything on the original or central model.

## Adding a test

Most checks are a click on the page plus an assertion on what `gisStandard.inspect` or the test host reports. Prefer the role and the visible English text of a control. The runner switches the page to English for the run and switches it back afterwards. When a check needs Revit in a particular state, add it to the `fixture` command rather than editing the pristine model.

The test host runs code in an API context only while Revit is idle. While a file dialog or another modal window is open, use the commands that work on dialogs (`fileDialog`, `windows`, `pressButton`).
