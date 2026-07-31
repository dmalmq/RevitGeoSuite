using System.Text.RegularExpressions;
using RevitGeoSuite.RevitInterop.Storage;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class RevitStorageSchemaNamesTests
{
    [Fact]
    public void Storage_schema_names_use_revit_safe_identifier_characters()
    {
        Regex safePattern = new Regex("^[A-Za-z0-9_]+$");

        Assert.Matches(safePattern, RevitStorageSchemaNames.GeoProjectInfo);
        Assert.Matches(safePattern, RevitStorageSchemaNames.PlacementAudit);
        Assert.DoesNotContain('.', RevitStorageSchemaNames.GeoProjectInfo);
        Assert.DoesNotContain('.', RevitStorageSchemaNames.PlacementAudit);
    }
}
