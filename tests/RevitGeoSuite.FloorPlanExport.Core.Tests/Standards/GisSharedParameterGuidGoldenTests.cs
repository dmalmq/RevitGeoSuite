using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

/// <summary>
/// Revit binds shared parameters by GUID, so changing one disconnects the values in existing models.
/// A failure here means the change must be fixed, not these expected values.
/// </summary>
public sealed class GisSharedParameterGuidGoldenTests
{
    private static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>
    {
        ["id"] = "64e7d743-c3ae-4831-a208-33c2762ac50a",
        ["level_id"] = "84f1637a-1421-4c6c-9670-c000206ffea0",
        ["name"] = "47b8db20-e756-4280-a842-f570dc847db5",
        ["short_name"] = "03f1849b-ac06-42c8-aff7-51a973f0a730",
        ["category"] = "7fb0aa6b-c570-437d-a86a-f25f02537165",
        ["ordinal"] = "6724ae86-07fe-44ba-b29b-1e9298bc3c8a",
        ["gis_category"] = "d4ed1842-369c-4064-97fd-93fab33f0a87",
        ["gis_restriction"] = "6df45ec8-c9ee-4b56-a038-c8c125b3ae1e",
        ["gis_kind"] = "ff63e172-c407-4265-b075-4e1d5e9822b8",
        ["gis_direction"] = "343f3126-afff-422c-99c1-0d1d4f1b1c95",
        ["gis_levels_served"] = "26b16279-ebcb-4baa-9a7e-b07addf4e5b4",
        ["gis_door"] = "e413bbb1-dab0-4acd-9b16-eeac608dca9b",
        ["gis_access_con"] = "0156f4b3-7806-4033-baa0-09838c299ddb",
        ["gis_key"] = "0816cab3-3717-4632-854f-c6c7c11e0881",
        ["gis_standard_version"] = "a5edd0e8-7877-4498-91c6-29691a6a01f5",
    };

    [Fact]
    public void All_15_parameter_guids_are_frozen()
    {
        Dictionary<string, Guid> actual = GisSharedParameters.All.ToDictionary(parameter => parameter.Name, parameter => parameter.Guid);

        Assert.Equal(15, actual.Count);
        Assert.Equal(Expected.Keys.OrderBy(name => name, StringComparer.Ordinal), actual.Keys.OrderBy(name => name, StringComparer.Ordinal));
        foreach (KeyValuePair<string, string> parameter in Expected)
        {
            Assert.Equal(new Guid(parameter.Value), actual[parameter.Key]);
        }
    }
}
