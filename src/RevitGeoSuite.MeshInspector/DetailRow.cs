using System.ComponentModel;

namespace RevitGeoSuite.MeshInspector;

public sealed class DetailRow : INotifyPropertyChanged
{
    private string value;

    public DetailRow(string label, string value)
    {
        Label = label;
        this.value = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public string Value
    {
        get => value;
        set
        {
            if (string.Equals(this.value, value, System.StringComparison.Ordinal))
            {
                return;
            }

            this.value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
