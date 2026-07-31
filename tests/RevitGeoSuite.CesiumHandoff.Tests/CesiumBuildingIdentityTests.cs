using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumBuildingIdentityTests
{
    [Fact]
    public void CreateId_IsStableForSameInputs()
    {
        string a = CesiumBuildingIdentity.CreateId("path:C:\\MODELS\\TOWER.RVT", "Shinjuku Tower");
        string b = CesiumBuildingIdentity.CreateId("path:C:\\MODELS\\TOWER.RVT", "Shinjuku Tower");
        Assert.Equal(a, b);
    }

    [Fact]
    public void CreateId_DiffersForDifferentDocumentKeys()
    {
        string a = CesiumBuildingIdentity.CreateId("path:C:\\A.RVT", "Tower");
        string b = CesiumBuildingIdentity.CreateId("path:C:\\B.RVT", "Tower");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CreateId_StartsWithUrlSafeNameSlug()
    {
        string id = CesiumBuildingIdentity.CreateId("key", "Shinjuku Tower (Phase 2)");
        Assert.StartsWith("shinjuku-tower-phase-2-", id);
        Assert.Matches("^[a-z0-9-]+$", id);
    }

    [Fact]
    public void CreateId_NonAsciiNameStillProducesUsableId()
    {
        string id = CesiumBuildingIdentity.CreateId("key", "新宿タワー");
        Assert.Matches("^[a-z0-9-]+$", id);
        Assert.NotEqual(string.Empty, id);
    }
}
