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
                "undo" => Post("ID_EDIT_UNDO"),
                "params" => Params(request),
                "uniqueIds" => UniqueIds(request),
                "fixture" => Fixture(request),
                "newFamily" => NewFamily(request),
                "fileDialog" => Win32Dialogs.FillFileDialog(Text(request, "path"), Milliseconds(request)),
                "windows" => Win32Dialogs.List(),
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
        };
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
    // with its worksets kept, so nothing ever syncs to a central model.
    private Dictionary<string, object?> Open(Dictionary<string, object?> request)
    {
        string path = TestRoot.Require(root, Text(request, "path"));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No model at this path.", path);
        }

        return api.Run(app =>
        {
            var options = new OpenOptions { Audit = false };
            if (BasicFileInfo.Extract(path).IsWorkshared)
            {
                options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            }

            UIDocument opened = app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options, false);
            return Describe(opened.Document);
        }, Slow);
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
    /// <see cref="FixtureTransaction"/>: renames element types (renames: [{from, to}]) and sets text
    /// parameters (values: [{id, name, value}]). Only ever runs on the per-run copy.
    /// </summary>
    public const string FixtureTransaction = "UI test: fixture";

    private Dictionary<string, object?> Fixture(Dictionary<string, object?> request)
    {
        List<Dictionary<string, object?>> renames = Objects(request, "renames");
        List<Dictionary<string, object?>> values = Objects(request, "values");
        return api.Run(app =>
        {
            Document document = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
            TestRoot.Require(root, document.PathName);
            var log = new List<string>();
            using var transaction = new Transaction(document, FixtureTransaction);
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

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the fixture.");
            }

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
