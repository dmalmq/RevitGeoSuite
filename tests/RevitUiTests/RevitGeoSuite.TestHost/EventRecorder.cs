using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Records what Revit did, in order: each committed, undone or redone transaction with its names, and
/// each dialog Revit raised. The runner reads it to prove that "the Undo list names X" without
/// reading the journal. While armed (a run is connected), TaskDialogs and message boxes are
/// answered with OK so an unattended run cannot hang on them; the text is kept for the report.
/// </summary>
internal sealed class EventRecorder
{
    private readonly object gate = new();
    private readonly List<Dictionary<string, object?>> events = new();
    private int sequence;

    public volatile bool Armed;

    public int LastSequence
    {
        get { lock (gate) { return sequence; } }
    }

    public List<Dictionary<string, object?>> Since(int after)
    {
        lock (gate)
        {
            return events.Where(entry => (int)entry["seq"]! > after).ToList();
        }
    }

    public void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        Document document = args.GetDocument();
        Add(new Dictionary<string, object?>
        {
            ["kind"] = KindOf(args.Operation),
            ["names"] = args.GetTransactionNames().ToArray(),
            ["document"] = document.Title,
            ["added"] = args.GetAddedElementIds().Count,
            ["modified"] = args.GetModifiedElementIds().Count,
            ["deleted"] = args.GetDeletedElementIds().Count,
        });
    }

    public void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs args)
    {
        var entry = new Dictionary<string, object?>
        {
            ["kind"] = "dialog",
            ["dialogId"] = args.DialogId,
            ["message"] = args switch
            {
                TaskDialogShowingEventArgs task => task.Message,
                MessageBoxShowingEventArgs box => box.Message,
                _ => string.Empty,
            },
            ["answered"] = false,
        };

        if (Armed && args is TaskDialogShowingEventArgs or MessageBoxShowingEventArgs)
        {
            entry["answered"] = args.OverrideResult((int)TaskDialogResult.Ok);
        }

        Add(entry);
    }

    private static string KindOf(UndoOperation operation) => operation switch
    {
        UndoOperation.TransactionCommitted => "commit",
        UndoOperation.TransactionUndone => "undo",
        UndoOperation.TransactionRedone => "redo",
        _ => operation.ToString(),
    };

    private void Add(Dictionary<string, object?> entry)
    {
        lock (gate)
        {
            entry["seq"] = ++sequence;
            entry["time"] = DateTime.UtcNow.ToString("o");
            events.Add(entry);
        }
    }
}
