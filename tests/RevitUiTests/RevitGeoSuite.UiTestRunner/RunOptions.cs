namespace RevitGeoSuite.UiTestRunner;

/// <summary>Command line of the runner. See ..\README.md for what each option is for.</summary>
internal sealed record RunOptions
{
    public const string GisStandardScript = "gis-standard";

    /// <summary>A GDB export folder from before Set up model, for B1's count comparison.</summary>
    public string BaselineExport { get; init; } = string.Empty;

    /// <summary>The scenario this run's steps come from, by its name in <see cref="GisStandardRun.Scenarios"/>.</summary>
    public string Script { get; init; } = GisStandardScript;

    /// <summary>
    /// Every scenario --scenario names, in order. They run one after another in the same Revit, each on a
    /// fresh copy of the model in a subfolder of --out named after it; one name runs in --out itself.
    /// </summary>
    public IReadOnlyList<string> ScenarioNames { get; init; } = new[] { GisStandardScript };

    /// <summary>
    /// The export page's settings.json that Run-UiTests.ps1 emptied for the run. Between scenarios the runner
    /// empties it again, so each scenario starts from the page's defaults as it would in a Revit of its own.
    /// </summary>
    public string ExportSettings { get; init; } = string.Empty;

    /// <summary>
    /// --scenario all: every scenario that checks RevitGeoSuite on the LUMINE copy. Left out: undo-probe (a
    /// diagnosis that types into Revit), model-facts and georef-read (they read, they check nothing).
    /// h4-shared-rules comes last, because it moves the sandbox's per-PC rules.
    /// </summary>
    public static readonly IReadOnlyList<string> AllScenarios = new[]
    {
        "gis-standard", "floor-types", "b1", "b2a", "a3-fields", "b9-groups", "c3a-reshape", "c3b-split",
        "c3c-merge", "c3d-hand-drawn", "c4-waiting", "c8-network", "h4-shared-rules",
    };

    /// <summary>c3a-reshape: a drift file to use instead of the one the scenario makes from its own export.</summary>
    public string Drift { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;

    /// <summary>--out itself: the run's folder, which holds every scenario's folder and Run-UiTests.ps1's sandbox.</summary>
    public string RunFolder { get; init; } = string.Empty;
    public string ExportView { get; init; } = string.Empty;
    public bool Excel { get; init; }
    public bool RealDrift { get; init; }
    public string RevitYear { get; init; } = "2024";
    public string DevToolsFile { get; init; } = DevToolsPort.DefaultFile;
    public IReadOnlySet<string>? Steps { get; init; }

    /// <summary>
    /// Floor types renamed before step 6 so the name rules match them. The defaults are the renames
    /// made by hand on the LUMINE copy in run 2; --rename-type replaces them.
    /// </summary>
    public IReadOnlyList<(string From, string To)> Renames { get; init; } = DefaultRenames;

    public static readonly IReadOnlyList<(string From, string To)> DefaultRenames = new[]
    {
        ("smoking room", "smoking room 店舗"),
        ("Baby room", "Baby room ラチ内"),
    };

    public bool Runs(string step) => Steps == null || Steps.Contains(step);

    public static string Usage => """
        RevitGeoSuite.UiTestRunner --model <pristine detached copy .rvt> [options]

          --scenario <names>     Which scenario to run (--script is the same). Default gis-standard. Several,
                                 comma-separated, or "all", run one after another in the same Revit, each
                                 on its own copy of the model in <out>\<name> with its own report.md, and
                                 <out>\summary.md lists them with their minutes. Several need
                                 Run-UiTests.ps1 -FloorPlanDataSandbox.
        {ScenarioList}
          --baseline-export <dir> b1: an export folder made before Set up model, to compare counts with.
          --drift <file>         c3a-reshape: use this drift file instead of making one from the run's own export.
          --model <path>         Pristine detached copy of the test model, under the test folder (C:\dev\revit-test).
                                 The runner copies it for each run and never saves either file.
          --out <folder>         Where the report, screenshots, CSVs and the model copy go.
                                 Default: C:\dev\revit-test\auto\<date-time>.
          --steps <1,2,...>      Run only these steps of the script (the rest report "not run").
          --export-view <name>   Plan view for step 16 (GeoPackage export with audit errors).
          --excel                Step 10: open the written CSV in Excel and save it back, then read it.
          --real-drift           Step 15: the model's station folder has a Kanizu drift file to accept from.
          --rename-type <a=>b>   Step 6: rename floor type a to b first, so the name rules match it. Repeat for
                                 more. Default: the two renames from run 2 (smoking room, Baby room).
          --revit-year <year>    Revit version whose journals are checked. Default 2024.
          --devtools-file <path> DevToolsActivePort file of RevitGeoSuite's WebView2, if not the default.
          --export-settings <f>  The export page's settings.json that Run-UiTests.ps1 emptied; emptied
                                 again before each scenario after the first. Run-UiTests.ps1 passes it.
        """.Replace("{ScenarioList}", string.Join(Environment.NewLine, GisStandardRun.Scenarios.Select(scenario => $"                         {scenario.Name}: {scenario.Summary}")));

    public static RunOptions Parse(string[] args)
    {
        var renames = new List<(string, string)>();
        var options = new RunOptions { OutputFolder = Path.Combine(@"C:\dev\revit-test\auto", DateTime.Now.ToString("yyyyMMdd-HHmmss")) };
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            string Value() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{name} needs a value.");
            options = name switch
            {
                "--script" or "--scenario" => WithScenarios(options, Value()),
                "--drift" => options with { Drift = Path.GetFullPath(Value()) },
                "--baseline-export" => options with { BaselineExport = Path.GetFullPath(Value()) },
                "--model" => options with { Model = Path.GetFullPath(Value()) },
                "--out" => options with { OutputFolder = Path.GetFullPath(Value()) },
                "--steps" => options with { Steps = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() },
                "--export-view" => options with { ExportView = Value() },
                "--excel" => options with { Excel = true },
                "--real-drift" => options with { RealDrift = true },
                "--rename-type" => AddRename(options, renames, Value()),
                "--revit-year" => options with { RevitYear = Value() },
                "--devtools-file" => options with { DevToolsFile = Path.GetFullPath(Value()) },
                "--export-settings" => options with { ExportSettings = SettingsFile(Value()) },
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
        }

        options = options with { RunFolder = options.OutputFolder };
        if (options.ScenarioNames.Count > 1 && !(options.ExportSettings.Length > 0 && TestPaths.IsUnder(options.ExportSettings, options.RunFolder)))
        {
            // Between scenarios the runner rewrites the export page's files; that may only ever be the run's sandbox copy.
            throw new ArgumentException("Several scenarios need --export-settings inside --out: run them with Run-UiTests.ps1 -StartRevit -FloorPlanDataSandbox, so the export store between scenarios is the run's own copy, never the roaming %APPDATA% one.");
        }

        if (renames.Count > 0)
        {
            options = options with { Renames = renames };
        }

        if (options.Model.Length == 0)
        {
            throw new ArgumentException("--model is required.");
        }

        return options;
    }

    /// <summary>The options of one scenario of the run: its own subfolder of --out when the run has several.</summary>
    public RunOptions ForScenario(string name) => this with
    {
        Script = name,
        OutputFolder = ScenarioNames.Count > 1 ? Path.Combine(OutputFolder, name) : OutputFolder,
    };

    private static RunOptions WithScenarios(RunOptions options, string value)
    {
        string[] names = value.Trim() == "all"
            ? AllScenarios.ToArray()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ScenarioName).ToArray();
        if (names.Length == 0 || names.Distinct().Count() != names.Length)
        {
            throw new ArgumentException($"--scenario needs one or more different scenario names, not '{value}'.");
        }

        return options with { Script = names[0], ScenarioNames = names };
    }

    private static string SettingsFile(string value)
    {
        string path = Path.GetFullPath(value);
        return Path.GetFileName(path).Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            ? path
            : throw new ArgumentException($"--export-settings names the export page's settings.json, not '{value}'.");
    }

    private static string ScenarioName(string value) =>
        GisStandardRun.Scenarios.Any(scenario => scenario.Name == value)
            ? value
            : throw new ArgumentException($"Unknown scenario '{value}'. Use one of: {string.Join(", ", GisStandardRun.Scenarios.Select(scenario => scenario.Name))}.");

    private static RunOptions AddRename(RunOptions options, List<(string, string)> renames, string value)
    {
        string[] parts = value.Split("=>", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ArgumentException($"--rename-type takes 'old name=>new name', not '{value}'.");
        }

        renames.Add((parts[0], parts[1]));
        return options;
    }

    /// <summary>The model and the output must both sit in the test folder that the test host allows.</summary>
    public void RequireInside(string root)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach ((string what, string path) in new[] { ("--model", Model), ("--out", OutputFolder), ("--drift", Drift) }.Where(pair => pair.Item2.Length > 0))
        {
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"{what} '{path}' is outside the test folder {root}. Tests only work on copies there.");
            }
        }
    }
}
