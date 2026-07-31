using System;
using System.Globalization;

namespace RevitGeoSuite.PlateauImport;

public enum PlateauScanPhase
{
    Enumerating,
    Parsing,
    Completed
}

public sealed class PlateauScanProgress
{
    public PlateauScanProgress(PlateauScanPhase phase, int current, int total, string? currentFile)
    {
        Phase = phase;
        Current = Math.Max(0, current);
        Total = Math.Max(0, total);
        CurrentFile = currentFile ?? string.Empty;
    }

    public PlateauScanPhase Phase { get; }

    public int Current { get; }

    public int Total { get; }

    public string CurrentFile { get; }

    public double Percent => Total <= 0
        ? 0d
        : Math.Min(100d, (Current * 100d) / Total);

    public string CurrentFileName => string.IsNullOrWhiteSpace(CurrentFile)
        ? string.Empty
        : System.IO.Path.GetFileName(CurrentFile);

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}/{2} {3}",
            Phase,
            Current,
            Total,
            CurrentFileName);
    }
}
