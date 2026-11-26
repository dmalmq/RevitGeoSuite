using System;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# RAII guard that temporarily sets an external string setting (such as Revit's global
/// <c>Application.SharedParametersFilename</c>) and deterministically restores the original value
/// on <see cref="Dispose"/>, even when an exception is thrown mid-operation.
/// </summary>
public sealed class ScopedRestoreGuard : IDisposable
{
    private readonly Action<string> _setter;
    private readonly string _originalValue;
    private bool _disposed;

    public ScopedRestoreGuard(Func<string> getter, Action<string> setter, string temporaryValue)
    {
        if (getter is null)
        {
            throw new ArgumentNullException(nameof(getter));
        }

        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _originalValue = getter() ?? string.Empty;
        TemporaryValue = temporaryValue ?? throw new ArgumentNullException(nameof(temporaryValue));
        _setter(TemporaryValue);
    }

    public string OriginalValue => _originalValue;

    public string TemporaryValue { get; }

    public bool IsRestored => _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setter(_originalValue);
    }
}
