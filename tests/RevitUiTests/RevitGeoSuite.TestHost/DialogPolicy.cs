using System;
using System.Linq;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Which Revit dialogs the test host may answer with OK during a run. Anything about saving,
/// overwriting, synchronising or the central model is left on screen for a person, so a run can
/// never save or overwrite a file by answering a prompt; the run then times out and says so.
/// Pure logic with no Revit types, so the runner's unit tests compile it too.
/// </summary>
public static class DialogPolicy
{
    private static readonly string[] Untouchable =
    {
        "save", "overwrite", "replace", "synchroniz", "synchronis", "central", "relinquish", "workshar",
        "保存", "上書き", "置き換え", "同期", "中央",
    };

    public static bool MayAnswer(bool armed, string? dialogId, string? message)
    {
        if (!armed)
        {
            return false;
        }

        string text = (dialogId ?? string.Empty) + "\n" + (message ?? string.Empty);
        return !Untouchable.Any(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
