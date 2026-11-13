namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The one document a run may change: its own copy of the model, in its own run folder. A workshared
/// copy must be its own central (the test host detaches it and saves it once in the run folder), so it
/// has no tie to the original central model.
/// </summary>
internal static class TestCopy
{
    /// <summary>Why the active document is not the run's copy, or null when it is.</summary>
    public static string? Problem(string activePath, string copyPath, string runFolder, bool workshared, string centralPath)
    {
        if (activePath.Length == 0)
        {
            return "The active document has no path, so it is not this run's model copy.";
        }

        string active = Path.GetFullPath(activePath);
        string copy = Path.GetFullPath(copyPath);
        string folder = Path.GetFullPath(runFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!copy.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            return $"The model copy {copy} is not inside the run folder {folder}.";
        }

        if (!string.Equals(active, copy, StringComparison.OrdinalIgnoreCase))
        {
            return $"The active document is {active}, not this run's copy {copy}.";
        }

        if (workshared && (centralPath.Length == 0 || !string.Equals(Path.GetFullPath(centralPath), copy, StringComparison.OrdinalIgnoreCase)))
        {
            return $"{copy} is workshared with central model '{centralPath}', not detached into the run folder.";
        }

        return null;
    }
}

internal sealed class NotTheTestCopyException(string message) : Exception(message);
