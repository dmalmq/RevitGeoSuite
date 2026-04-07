using System;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapOverlayFeatureClickedEventArgs : EventArgs
{
    public MapOverlayFeatureClickedEventArgs(string featureId)
    {
        FeatureId = featureId ?? string.Empty;
    }

    public string FeatureId { get; }
}
