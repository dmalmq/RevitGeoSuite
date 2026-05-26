using System;
using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapOverlayFeaturesRectangleSelectedEventArgs : EventArgs
{
    public MapOverlayFeaturesRectangleSelectedEventArgs(IReadOnlyList<string> featureIds)
    {
        FeatureIds = featureIds ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> FeatureIds { get; }
}
