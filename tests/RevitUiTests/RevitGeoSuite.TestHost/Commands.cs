using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>The commands the runner can send. Each one is small and does what a person at Revit would do.</summary>
internal sealed class Commands
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Slow = TimeSpan.FromMinutes(20);

    private readonly ApiQueue api;
    private readonly EventRecorder recorder;
    private readonly string root;

    public Commands(ApiQueue api, EventRecorder recorder, string root)
    {
        this.api = api;
        this.recorder = recorder;
        this.root = root;
    }

    public Dictionary<string, object?> Handle(Dictionary<string, object?> request)
    {
        string command = Text(request, "cmd");
        try
        {
            object? result = command switch
            {
                "ping" => Ping(),
                "arm" => Arm(request),
                "events" => Events(request),
                "open" => Open(request),
                "activate" => Activate(request),
                "closeOthers" => CloseOthers(),
                "state" => api.Run(State, Quick),
                "post" => Post(Text(request, "id")),
                "ribbon" => Post($"CustomCtrl_%CustomCtrl_%{Text(request, "tab")}%{Text(request, "panel")}%{Text(request, "button")}"),
                "undo" => PostOrPress("ID_EDIT_UNDO", "Undo", "元に戻す", "UNDO"),
                "params" => Params(request),
                "uniqueIds" => UniqueIds(request),
                "floorTypes" => api.Run(FloorTypes, Quick),
                "views" => api.Run(app => ViewCommands.Views(app, request), Quick),
                "levels" => api.Run(ViewCommands.Levels, Quick),
                "views3d" => api.Run(ViewCommands.Views3D, Quick),
                "ribbonItems" => api.Run(app => RibbonItems(app, Text(request, "tab")), Quick),
                "bindings" => api.Run(app => ModelCommands.Bindings(app, Strings(request, "names")), Quick),
                "elevators" => api.Run(app => ModelCommands.Elevators(app, Strings(request, "uniqueIds")), Quick),
                "doors" => api.Run(ModelCommands.Doors, Quick),
                "activateView" => api.Run(app => ViewCommands.ActivateView(app, Text(request, "name")), Quick),
                "redo" => PostOrPress("ID_EDIT_REDO", "Redo", "やり直し"),
                "elements" => api.Run(app => FloorCommands.Elements(app, Strings(request, "uniqueIds"), Longs(request, "ids")), Quick),
                "firstOf" => api.Run(app => FloorCommands.FirstOf(app, Text(request, "category")), Quick),
                "floorGeometry" => api.Run(app => FloorCommands.FloorGeometry(app, Text(request, "uniqueId")), Quick),
                "floorScan" => api.Run(FloorCommands.FloorScan, TimeSpan.FromMinutes(5)),
                "fixture" => Fixture(request),
                "newFamily" => NewFamily(request),
                "fileDialog" => Win32Dialogs.FillFileDialog(TestRoot.Require(root, Text(request, "path")), Milliseconds(request)),
                "windows" => Win32Dialogs.List(),
                "closeDialogs" => Win32Dialogs.CloseAll(Milliseconds(request)),
                "pressButton" => Win32Dialogs.Press(Text(request, "button"), Milliseconds(request)),
                "screenshot" => Screenshot(Text(request, "path")),
                _ => throw new ArgumentException($"Unknown command '{command}'."),
            };

            var reply = result as Dictionary<string, object?> ?? new Dictionary<string, object?>();
            reply["ok"] = true;
            return reply;
        }
        catch (Exception ex)
        {
            return Fail(ex);
        }
    }

    public static Dictionary<string, object?> Fail(Exception ex)
    {
        Exception inner = ex is AggregateException aggregate && aggregate.InnerException != null ? aggregate.InnerException : ex;
        return new Dictionary<string, object?> { ["ok"] = false, ["error"] = $"{inner.GetType().Name}: {inner.Message}" };
    }

    private Dictionary<string, object?> Ping()
    {
        Process process = Process.GetCurrentProcess();
        return new Dictionary<string, object?>
        {
            ["pid"] = process.Id,
            ["started"] = process.StartTime.ToUniversalTime().ToString("o"),
            ["root"] = root,
            ["armed"] = recorder.Armed,
            ["webViewArguments"] = Environment.GetEnvironmentVariable(TestHostApp.WebViewArgumentsVariable),
            ["revit"] = api.Run(app => app.Application.VersionNumber + " " + app.Application.VersionBuild, Quick),
            ["testHost"] = HostBuild(),
            ["rgs"] = TestHostApp.RgsStatus,
            ["rgsLoaded"] = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "RevitGeoSuite.Shell").Select(assembly => assembly.Location).ToList(),
        };
    }

    // Which test host the loader loaded, so a report shows that a rebuilt host ran without a new prompt.
    private static string HostBuild()
    {
        string path = typeof(Commands).Assembly.Location;
        return $"{path}, built {System.IO.File.GetLastWriteTimeUtc(path):yyyy-MM-dd HH:mm} UTC, loaded by {AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "RevitGeoSuite.TestHost.Loader")?.Location ?? "no loader"}";
    }

    private Dictionary<string, object?> Arm(Dictionary<string, object?> request)
    {
        recorder.Armed = request.TryGetValue("on", out object? on) && on is true;
        return new Dictionary<string, object?> { ["armed"] = recorder.Armed, ["last"] = recorder.LastSequence };
    }

    private Dictionary<string, object?> Events(Dictionary<string, object?> request)
    {
        int after = request.TryGetValue("after", out object? value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
        return new Dictionary<string, object?> { ["events"] = recorder.Since(after), ["last"] = recorder.LastSequence };
    }

    // Opens a model copy under the test folder and makes it active. A workshared file is detached
    // with its worksets kept, so nothing ever syncs to the original central model.
    private Dictionary<string, object?> Open(Dictionary<string, object?> request)
    {
        string path = TestRoot.Require(root, Text(request, "path"));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No model at this path.", path);
        }

        List<string> unloaded = UnloadMissingReferences(path);
        return api.Run(app =>
        {
            var options = new OpenOptions { Audit = false };
            if (BasicFileInfo.Extract(path).IsWorkshared)
            {
                options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            }

            Document document = app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options, false).Document;

            // Revit names a detached model "<name>_detached.rvt" with no folder, which resolves against
            // Revit's working directory, not the run folder. Save it once, by full path, as its own
            // central next to the copy: it then lives in the run folder with no tie to the original.
            string folder = Path.GetDirectoryName(path)!;
            if (document.IsWorkshared && (document.IsDetached || !InFolder(document.PathName, folder)))
            {
                string detached = TestRoot.Require(root, Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-detached.rvt"));
                var save = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                save.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                document.SaveAs(detached, save);
            }

            Dictionary<string, object?> described = Describe(document);
            described["unloadedReferences"] = unloaded;
            return described;
        }, Slow);
    }

    // A Revit link whose file is not on this PC makes Revit ask on open ("could not find or read").
    // Mark each such link in the copy as not to be loaded before opening and return what was done for
    // the report. Only Revit links are touched; any reference that fails is reported and skipped, and
    // the host's dialog answer stays the fallback. Only the copy in the test folder is ever written.
    private static List<string> UnloadMissingReferences(string path)
    {
        var notes = new List<string>();
        try
        {
            ModelPath model = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
            TransmissionData? data = TransmissionData.ReadTransmissionData(model);
            if (data == null)
            {
                return notes;
            }

            bool changed = false;
            foreach (ElementId id in data.GetAllExternalFileReferenceIds())
            {
                try
                {
                    ExternalFileReference reference = data.GetLastSavedReferenceData(id);
                    if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink || reference.GetLinkedFileStatus() == LinkedFileStatus.Unloaded)
                    {
                        continue;
                    }

                    string target = ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetAbsolutePath());
                    if (target.Length > 0 && File.Exists(target))
                    {
                        continue;
                    }

                    data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
                    changed = true;
                    notes.Add($"unloaded Revit link {target}");
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                {
                    notes.Add($"left reference {id.Value} as it is: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (changed)
            {
                data.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(model, data);
            }
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException or IOException)
        {
            notes.Add($"did not unload links: {ex.GetType().Name}: {ex.Message}");
        }

        return notes;
    }

    private static bool InFolder(string path, string folder)
    {
        try
        {
            return path.Length > 0 && Path.IsPathRooted(path)
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path))?.TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private Dictionary<string, object?> Activate(Dictionary<string, object?> request)
    {
        string path = TestRoot.Require(root, Text(request, "path"));
        return api.Run(app => Describe(app.OpenAndActivateDocument(path).Document), Slow);
    }

    // Closes every other open document under the test folder, without saving. Documents elsewhere
    // are left alone and listed, since they are not the test's to close.
    private Dictionary<string, object?> CloseOthers() => api.Run(app =>
    {
        Document? active = app.ActiveUIDocument?.Document;
        var closed = new List<string>();
        var kept = new List<string>();
        foreach (Document document in app.Application.Documents.Cast<Document>().ToList())
        {
            if (document.IsLinked || (active != null && document.Equals(active)))
            {
                continue;
            }

            if (IsUnderRoot(document.PathName))
            {
                closed.Add(document.Title);
                document.Close(false);
            }
            else
            {
                kept.Add(document.Title);
            }
        }

        return new Dictionary<string, object?> { ["closed"] = closed, ["kept"] = kept };
    }, Slow);

    private static Dictionary<string, object?> State(UIApplication app)
    {
        UIDocument? ui = app.ActiveUIDocument;
        if (ui == null)
        {
            return new Dictionary<string, object?> { ["document"] = null };
        }

        Dictionary<string, object?> state = Describe(ui.Document);
        View? view = ui.ActiveView;
        state["viewName"] = view?.Name;
        state["viewId"] = view?.Id.Value;
        state["selection"] = ui.Selection.GetElementIds().Select(id => id.Value).ToArray();
        UIView? uiView = view == null ? null : ui.GetOpenUIViews().FirstOrDefault(open => open.ViewId == view.Id);
        if (uiView != null)
        {
            IList<XYZ> corners = uiView.GetZoomCorners();
            state["zoom"] = corners.SelectMany(point => new[] { point.X, point.Y, point.Z }).ToArray();
        }

        return state;
    }

    /// <summary>
    /// Undo and Redo: posts the command. Revit refuses it in b1 and b2a (CanPostCommand false) and
    /// accepts it in c3a, from the same code and the same view, so the cause is outside the document.
    /// A refusal is retried once with Revit's main window in front, then by pressing the Quick Access
    /// Toolbar button through UI Automation (on this pipe thread, so Revit's UI thread is free to
    /// answer). When both fail, the error carries Revit's state at that moment.
    /// </summary>
    private Dictionary<string, object?> PostOrPress(string id, params string[] buttonNames)
    {
        try
        {
            return Post(id);
        }
        catch (InvalidOperationException refused) when (refused.Message.Contains("cannot run"))
        {
            string front = Win32Dialogs.ActivateMainWindow();
            System.Threading.Thread.Sleep(1000);
            try
            {
                Dictionary<string, object?> posted = Post(id);
                posted["after"] = front;
                return posted;
            }
            catch (InvalidOperationException again) when (again.Message.Contains("cannot run"))
            {
                string state = api.Run(app => CommandState(app, id), Quick);
                Dictionary<string, object?> pressed = Win32Dialogs.PressMainWindowButton(
                    buttonNames.Concat(new[] { id }).ToArray(), $"{refused.Message} {front}; still refused. State: {state}");
                pressed["last"] = recorder.LastSequence;
                return pressed;
            }
        }
    }

    // What decides whether Revit enables a command: the active document and whether a transaction is
    // open in it, the other open documents, a control command that is always on, the transactions the
    // test host saw last (the undo list as events), and the journal's last command lines.
    private string CommandState(UIApplication app, string id)
    {
        string Can(string command) => RevitCommandId.LookupCommandId(command) is RevitCommandId found ? (app.CanPostCommand(found) ? "yes" : "no") : "unknown";
        UIDocument? ui = app.ActiveUIDocument;
        Document? document = ui?.Document;
        string active = document == null
            ? "no active document"
            : string.Format(
                CultureInfo.InvariantCulture,
                "active '{0}' (modifiable {1}, read-only {2}, workshared {3}, detached {4}, modified {5}), view '{6}' ({7})",
                document.Title, document.IsModifiable, document.IsReadOnly, document.IsWorkshared, document.IsDetached, document.IsModified,
                ui!.ActiveView?.Name, ui.ActiveView?.ViewType);
        string documents = string.Join(", ", app.Application.Documents.Cast<Document>()
            .Select(open => open.Title + (open.IsLinked ? " (link)" : string.Empty) + (open.IsFamilyDocument ? " (family)" : string.Empty)));
        string transactions = string.Join(" / ", recorder.Since(0)
            .Where(entry => (string?)entry["kind"] != "dialog")
            .Reverse().Take(6).Reverse()
            .Select(entry => $"#{entry["seq"]} {entry["kind"]} [{string.Join(", ", (string[]?)entry["names"] ?? Array.Empty<string>())}] in {entry["document"]}"));
        return $"{active}; open: {documents}; can post {id} {Can(id)}, ID_EDIT_REDO {Can("ID_EDIT_REDO")}, ID_VIEW_DEFAULT_3DVIEW {Can("ID_VIEW_DEFAULT_3DVIEW")}; "
            + $"last transactions: {(transactions.Length == 0 ? "none" : transactions)}; journal: {JournalTail(app.Application.RecordingJournalFilename)}";
    }

    private static string JournalTail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool utf16 = stream.ReadByte() == 0xFF && stream.ReadByte() == 0xFE;
            long start = Math.Max(0, stream.Length - 256 * 1024);
            stream.Seek(utf16 ? start & ~1L : start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, utf16 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8, false);
            string[] lines = reader.ReadToEnd().Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("Jrn.", StringComparison.Ordinal) || line.IndexOf("undo", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            return lines.Length == 0 ? "no command lines" : string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - 8)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"not readable ({ex.Message})";
        }
    }

    // Each button on a ribbon tab as Revit shows it: its panel, name, text (with the line break) and tooltip.
    private static Dictionary<string, object?> RibbonItems(UIApplication app, string tab) => new()
    {
        ["items"] = app.GetRibbonPanels(tab)
            .SelectMany(panel => panel.GetItems().Select(item => new Dictionary<string, object?>
            {
                ["panel"] = panel.Name,
                ["name"] = item.Name,
                ["text"] = item.ItemText,
                ["toolTip"] = item.ToolTip,
            }))
            .ToList(),
    };

    private Dictionary<string, object?> Post(string id) => api.Run(app =>
    {
        RevitCommandId command = RevitCommandId.LookupCommandId(id)
            ?? throw new ArgumentException($"Revit has no command '{id}'.");
        if (!app.CanPostCommand(command))
        {
            throw new InvalidOperationException($"Revit cannot run '{id}' now.");
        }

        app.PostCommand(command);
        return new Dictionary<string, object?> { ["posted"] = id, ["last"] = recorder.LastSequence };
    }, Quick);

    // Reads parameters by name from each element, then from its type. Values come back as text.
    private Dictionary<string, object?> Params(Dictionary<string, object?> request)
    {
        long[] ids = Longs(request, "ids");
        string[] names = Strings(request, "names");
        return api.Run(app =>
        {
            Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
            var elements = new Dictionary<string, object?>();
            foreach (long id in ids)
            {
                Element? element = document.GetElement(new ElementId(id));
                if (element == null)
                {
                    elements[id.ToString(CultureInfo.InvariantCulture)] = null;
                    continue;
                }

                Element? type = document.GetElement(element.GetTypeId());
                var values = new Dictionary<string, object?>();
                foreach (string name in names)
                {
                    Parameter? parameter = element.LookupParameter(name) ?? type?.LookupParameter(name);
                    values[name] = parameter == null ? null : ValueOf(parameter);
                }

                elements[id.ToString(CultureInfo.InvariantCulture)] = values;
            }

            return new Dictionary<string, object?> { ["elements"] = elements };
        }, Quick);
    }

    // Every floor type with its name and how many floors use it, to check renames by element id.
    private static Dictionary<string, object?> FloorTypes(UIApplication app)
    {
        Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
        Dictionary<long, int> floors = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType()
            .GroupBy(floor => floor.GetTypeId().Value)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<long, long> firstFloor = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType()
            .GroupBy(floor => floor.GetTypeId().Value)
            .ToDictionary(group => group.Key, group => group.Min(floor => floor.Id.Value));
        return new Dictionary<string, object?>
        {
            ["floorTypes"] = new FilteredElementCollector(document).OfClass(typeof(FloorType)).Cast<FloorType>()
                .Select(type => new Dictionary<string, object?>
                {
                    ["id"] = type.Id.Value,
                    ["name"] = type.Name,
                    ["floors"] = floors.TryGetValue(type.Id.Value, out int count) ? count : 0,
                    ["firstFloor"] = firstFloor.TryGetValue(type.Id.Value, out long first) ? first : null,
                })
                .ToList(),
        };
    }

    private Dictionary<string, object?> UniqueIds(Dictionary<string, object?> request)
    {
        long[] ids = Longs(request, "ids");
        return api.Run(app =>
        {
            Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
            return new Dictionary<string, object?>
            {
                ["uniqueIds"] = ids.ToDictionary(id => id.ToString(CultureInfo.InvariantCulture), id => (object?)document.GetElement(new ElementId(id))?.UniqueId),
            };
        }, Quick);
    }

    /// <summary>
    /// Puts the test copy into the state a step needs, in one transaction named
    /// <see cref="FixtureTransaction"/>: renames element types (renames: [{from, to}]), sets text
    /// parameters (values: [{id, name, value}]), edits views and view templates (views: see
    /// <see cref="ViewCommands.Edit"/>), copies elements in place (copyInPlace: [ids]),
    /// deletes elements (delete: [ids]) and cuts floor openings (openingInEastBand: [{floor, bandM, sizeM}], see <see cref="FloorCommands.OpeningInEastBand"/>).
    /// Only ever runs on a document inside the test folder.
    /// </summary>
    public const string FixtureTransaction = "UI test: fixture";

    private Dictionary<string, object?> Fixture(Dictionary<string, object?> request)
    {
        List<Dictionary<string, object?>> renames = Objects(request, "renames");
        List<Dictionary<string, object?>> values = Objects(request, "values");
        List<Dictionary<string, object?>> viewEdits = Objects(request, "views");
        long[] copies = Longs(request, "copyInPlace");
        List<Dictionary<string, object?>> openings = Objects(request, "openingInEastBand");
        long[] deletions = Longs(request, "delete");
        return api.Run(app =>
        {
            Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
            TestRoot.Require(root, document.PathName);
            var log = new List<string>();
            using var transaction = new Transaction(document, FixtureTransaction);
            // A fixture can raise warnings on purpose (a floor copied in place: "identical instances in
            // the same place"). Shown at commit, Revit's warning dialog would hold the API thread, so
            // warnings are taken off and logged; errors still fail the commit.
            var warnings = new FixtureWarnings();
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            transaction.SetFailureHandlingOptions(options.SetFailuresPreprocessor(warnings).SetClearAfterRollback(true));
            transaction.Start();
            foreach (Dictionary<string, object?> rename in renames)
            {
                string from = Text(rename, "from");
                string to = Text(rename, "to");
                List<ElementType> types = new FilteredElementCollector(document).WhereElementIsElementType().Cast<ElementType>()
                    .Where(type => type.Name == from).ToList();
                foreach (ElementType type in types)
                {
                    type.Name = to;
                    log.Add($"renamed {type.Id.Value} '{from}' to '{to}'");
                }

                if (types.Count == 0)
                {
                    log.Add($"no type named '{from}'");
                }
            }

            foreach (Dictionary<string, object?> value in values)
            {
                long id = Convert.ToInt64(value["id"], CultureInfo.InvariantCulture);
                string name = Text(value, "name");
                Element element = document.GetElement(new ElementId(id)) ?? throw new ArgumentException($"No element {id}.");
                Parameter parameter = element.LookupParameter(name) ?? throw new ArgumentException($"Element {id} has no parameter '{name}'.");
                parameter.Set(value.TryGetValue("value", out object? text) ? Convert.ToString(text, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty);
                log.Add($"set {id} {name}");
            }

            foreach (Dictionary<string, object?> edit in viewEdits)
            {
                log.AddRange(ViewCommands.Edit(document, edit));
            }

            log.AddRange(ViewCommands.CopyInPlace(document, copies));
            foreach (long id in deletions)
            {
                ICollection<ElementId> gone = document.Delete(new ElementId(id));
                log.Add($"deleted {id} and {gone.Count - 1} element(s) with it");
            }

            foreach (Dictionary<string, object?> opening in openings)
            {
                log.Add(FloorCommands.OpeningInEastBand(
                    document,
                    Convert.ToInt64(opening["floor"], CultureInfo.InvariantCulture),
                    Convert.ToDouble(opening["bandM"], CultureInfo.InvariantCulture),
                    Convert.ToDouble(opening["sizeM"], CultureInfo.InvariantCulture)));
            }

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException($"Revit did not commit the fixture. {string.Join(" ", warnings.Errors)}");
            }

            log.AddRange(warnings.Removed.Select(text => $"removed Revit warning: {text}"));
            return new Dictionary<string, object?> { ["log"] = log };
        }, Quick);
    }

    // Makes an empty family from a Revit family template, saves it under the test folder and makes
    // it the active document, for the "no project open" check.
    private Dictionary<string, object?> NewFamily(Dictionary<string, object?> request)
    {
        string path = TestRoot.Require(root, Text(request, "path"));
        return api.Run(app =>
        {
            string template = FindTemplate(app.Application.FamilyTemplatePath);
            Document family = app.Application.NewFamilyDocument(template);
            family.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
            family.Close(false);
            return Describe(app.OpenAndActivateDocument(path).Document);
        }, Slow);
    }

    private Dictionary<string, object?> Screenshot(string path)
    {
        path = TestRoot.Require(root, path);
        Win32Dialogs.CaptureMainWindow(path);
        return new Dictionary<string, object?> { ["path"] = path };
    }

    private bool IsUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            TestRoot.Require(root, path);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string FindTemplate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Revit's family template folder '{folder}' does not exist.");
        }

        string[] templates = Directory.GetFiles(folder, "*.rft", SearchOption.AllDirectories);
        return templates.FirstOrDefault(file => Path.GetFileName(file).IndexOf("Generic Model", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? templates.FirstOrDefault()
            ?? throw new FileNotFoundException($"No family template (.rft) under '{folder}'.");
    }

    private static Dictionary<string, object?> Describe(Document document) => new()
    {
        ["document"] = document.Title,
        ["path"] = document.PathName,
        ["isFamily"] = document.IsFamilyDocument,
        ["isWorkshared"] = document.IsWorkshared,
        ["isDetached"] = document.IsDetached,
        ["centralPath"] = document.IsWorkshared
            ? ModelPathUtils.ConvertModelPathToUserVisiblePath(document.GetWorksharingCentralModelPath())
            : string.Empty,
    };

    private static string? ValueOf(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.String => parameter.AsString(),
        StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
        StorageType.Double => parameter.AsValueString() ?? parameter.AsDouble().ToString(CultureInfo.InvariantCulture),
        StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
        _ => parameter.AsValueString(),
    };

    private static List<Dictionary<string, object?>> Objects(Dictionary<string, object?> request, string key) =>
        request.TryGetValue(key, out object? value) && value is IEnumerable items and not string
            ? items.OfType<Dictionary<string, object?>>().ToList()
            : new List<Dictionary<string, object?>>();

    private static string Text(Dictionary<string, object?> request, string key) =>
        request.TryGetValue(key, out object? value) && value is string text && text.Length > 0
            ? text
            : throw new ArgumentException($"'{key}' is required.");

    private static int Milliseconds(Dictionary<string, object?> request) =>
        request.TryGetValue("timeoutMs", out object? value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 30000;

    private static long[] Longs(Dictionary<string, object?> request, string key) =>
        request.TryGetValue(key, out object? value) && value is IEnumerable items
            ? items.Cast<object>().Select(item => Convert.ToInt64(item, CultureInfo.InvariantCulture)).ToArray()
            : Array.Empty<long>();

    private static string[] Strings(Dictionary<string, object?> request, string key) =>
        request.TryGetValue(key, out object? value) && value is IEnumerable items and not string
            ? items.Cast<object>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).ToArray()
            : Array.Empty<string>();
}
