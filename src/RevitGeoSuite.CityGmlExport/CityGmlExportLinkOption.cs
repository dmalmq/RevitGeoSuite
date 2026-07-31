using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportLinkOption : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ElementId LinkInstanceId { get; set; } = ElementId.InvalidElementId;

    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
