using System;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapPointSelectedEventArgs : EventArgs
{
    public MapPointSelectedEventArgs(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}
