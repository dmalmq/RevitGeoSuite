using System.ComponentModel;

namespace RevitGeoSuite.PlateauImport;

public abstract class SelectableOptionBase : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

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
