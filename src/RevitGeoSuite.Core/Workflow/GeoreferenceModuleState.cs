using System;

namespace RevitGeoSuite.Core.Workflow;

public sealed class GeoreferenceModuleState
{
    public WorkingProjectBasePointReference? WorkingProjectBasePoint { get; set; }

    public DateTime? LastUpdatedUtc { get; set; }
}
