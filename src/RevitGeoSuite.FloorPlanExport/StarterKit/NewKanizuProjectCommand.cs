using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitGeoSuite.FloorPlanExport.Commands;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.UI;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// Revit <see cref="IExternalCommand"/> entry point for T9 ("New Kanizu Project").
/// Supports BOTH interactive UI execution (via <see cref="NewKanizuProjectDialog"/> + bilingual
/// EN/JA summary <see cref="TaskDialog"/>) and headless/batch automation via the
/// <see cref="NewKanizuProjectOptions.DefaultConfigEnvironmentVariable"/> JSON configuration path.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class NewKanizuProjectCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData?.Application?.Application is null)
        {
            message = "Revit Application context is unavailable.";
            return Result.Failed;
        }

        UIApplication uiApp = commandData.Application;
        Application app = uiApp.Application;

        // Headless / automation entry point when KANIZU_NEW_PROJECT_CONFIG is set.
        string? configPath = Environment.GetEnvironmentVariable(NewKanizuProjectOptions.DefaultConfigEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return ExecuteHeadless(uiApp, app, configPath!, ref message);
        }

        return ExecuteInteractive(uiApp, app, ref message);
    }

    /// <summary>Headless execution path usable from automated test harnesses or batch scripts.</summary>
    public static NewKanizuProjectResult RunHeadless(
        Application application,
        NewKanizuProjectOptions options,
        Document? activeDocument = null,
        StarterKitSpec? kitSpec = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        NewKanizuProjectResult result;
        if (options.InitializationMode == NewKanizuProjectInitializationMode.CreateFromTemplate)
        {
            bool closeAfterSave = !string.IsNullOrWhiteSpace(options.SaveAsRvtPath);
            (_, result) = NewKanizuProjectBootstrapper.CreateNewProject(
                application,
                options,
                kitSpec,
                options.SaveAsRvtPath,
                closeAfterSave: closeAfterSave);
        }
        else
        {
            if (activeDocument is null)
            {
                throw new InvalidOperationException("InitializationMode.InitializeOpenDocument requires an active Revit project document.");
            }

            result = NewKanizuProjectBootstrapper.InitializeOpenDocument(activeDocument, options, kitSpec);
        }

        if (!string.IsNullOrWhiteSpace(options.ResultOutputPath))
        {
            string outPath = Path.GetFullPath(options.ResultOutputPath!.Trim());
            string? dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(outPath, result.ToJson(indented: true), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return result;
    }

    private static Result ExecuteHeadless(UIApplication uiApp, Application app, string configPath, ref string message)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                message = $"New Kanizu Project config file not found at '{configPath}'.";
                return Result.Failed;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            NewKanizuProjectOptions options = NewKanizuProjectOptions.FromJson(json);
            NewKanizuProjectResult result = RunHeadless(app, options, uiApp.ActiveUIDocument?.Document);
            if (!result.Succeeded)
            {
                message = $"New Kanizu Project post-init readiness failed ({result.PostInitReadiness.FailCount} failure(s)).";
                return Result.Failed;
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"New Kanizu Project failed: {ex.Message}";
            return Result.Failed;
        }
    }

    private static Result ExecuteInteractive(UIApplication uiApp, Application app, ref string message)
    {
        NewKanizuProjectLanguage language = CommandLanguageResolver.Resolve() == UiLanguage.Japanese
            ? NewKanizuProjectLanguage.Japanese
            : NewKanizuProjectLanguage.English;

        Document? activeDoc = uiApp.ActiveUIDocument?.Document;
        bool hasEditableActiveDoc = activeDoc != null && !activeDoc.IsFamilyDocument && !activeDoc.IsReadOnly;
        StaleIdentitySnapshot? staleSnapshot = hasEditableActiveDoc
            ? NewKanizuProjectBootstrapper.ReadStaleIdentitySnapshot(activeDoc!)
            : null;

        try
        {
            NewKanizuProjectDialog dialog = new(language, staleSnapshot, hasEditableActiveDoc);
            if (uiApp.MainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(dialog).Owner = uiApp.MainWindowHandle;
            }

            if (dialog.ShowDialog() != true || dialog.SelectedOptions is null)
            {
                return Result.Cancelled;
            }

            NewKanizuProjectOptions options = dialog.SelectedOptions;
            NewKanizuProjectResult result;
            if (options.InitializationMode == NewKanizuProjectInitializationMode.CreateFromTemplate)
            {
                if (string.IsNullOrWhiteSpace(options.SaveAsRvtPath))
                {
                    NewKanizuProjectPlan previewPlan = NewKanizuProjectPlanner.Plan(options);
                    SaveFileDialog saveDialog = new()
                    {
                        Title = NewKanizuProjectStrings.Select(
                            language,
                            "Save New Kanizu Project (.rvt)",
                            "新規 Kanizu プロジェクト (.rvt) の保存先"),
                        Filter = "Revit Project (*.rvt)|*.rvt",
                        DefaultExt = ".rvt",
                        FileName = previewPlan.ProjectKey + ".rvt",
                    };
                    if (saveDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(saveDialog.FileName))
                    {
                        return Result.Cancelled;
                    }

                    options.SaveAsRvtPath = saveDialog.FileName;
                }

                (_, NewKanizuProjectResult createdResult) = NewKanizuProjectBootstrapper.CreateNewProject(
                    app,
                    options,
                    saveAsRvtPath: options.SaveAsRvtPath,
                    closeAfterSave: true);
                result = createdResult;

                if (!string.IsNullOrWhiteSpace(options.SaveAsRvtPath) && File.Exists(options.SaveAsRvtPath))
                {
                    uiApp.OpenAndActivateDocument(options.SaveAsRvtPath);
                }
            }
            else
            {
                result = NewKanizuProjectBootstrapper.InitializeOpenDocument(activeDoc!, options);
            }

            string summaryBody = NewKanizuProjectStrings.FormatCompletionDialogBody(
                language,
                result.ModelId,
                result.Plan.KitVersion,
                result.Plan.GisStandardVersion,
                result.Levels.Select(l => (l.LevelName, l.Ordinal, l.ElevationMeters, l.LevelId)).ToList(),
                result.CreatedWorksets,
                result.CreatedViews,
                result.PostInitReadiness.IsReady,
                result.Warnings);

            TaskDialog.Show(NewKanizuProjectStrings.CommandTitle(language), summaryBody);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(
                NewKanizuProjectStrings.CommandTitle(language),
                NewKanizuProjectStrings.Select(
                    language,
                    $"Could not initialize New Kanizu Project:\n{ex.Message}",
                    $"新規 Kanizu プロジェクトの初期化に失敗しました:\n{ex.Message}"));
            return Result.Failed;
        }
    }
}
