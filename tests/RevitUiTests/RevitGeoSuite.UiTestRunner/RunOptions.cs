namespace RevitGeoSuite.UiTestRunner;

/// <summary>Command line of the runner. See ..\README.md for what each option is for.</summary>
internal sealed record RunOptions
{
    public const string GisStandardScript = "gis-standard";
    public const string FloorTypesScript = "floor-types";

    /// <summary>Which test script to run: the GIS standard page (run 2) or the floor type standard 2 renames.</summary>
    public string Script { get; init; } = GisStandardScript;

    public string Model { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;
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

    public const string Usage = """
        RevitGeoSuite.UiTestRunner --model <pristine detached copy .rvt> [options]

          --script <name>        gis-standard (default): the GIS standard page script, steps 1-16.
                                 floor-types: floor type standard 2, old names renamed by Fix floor types.
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
        """;

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
                "--script" => options with { Script = ScriptName(Value()) },
                "--model" => options with { Model = Path.GetFullPath(Value()) },
                "--out" => options with { OutputFolder = Path.GetFullPath(Value()) },
                "--steps" => options with { Steps = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() },
                "--export-view" => options with { ExportView = Value() },
                "--excel" => options with { Excel = true },
                "--real-drift" => options with { RealDrift = true },
                "--rename-type" => AddRename(options, renames, Value()),
                "--revit-year" => options with { RevitYear = Value() },
                "--devtools-file" => options with { DevToolsFile = Path.GetFullPath(Value()) },
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
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

    private static string ScriptName(string value) =>
        value is GisStandardScript or FloorTypesScript ? value : throw new ArgumentException($"Unknown script '{value}'. Use {GisStandardScript} or {FloorTypesScript}.");

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
        foreach ((string what, string path) in new[] { ("--model", Model), ("--out", OutputFolder) })
        {
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"{what} '{path}' is outside the test folder {root}. Tests only work on copies there.");
            }
        }
    }
}
