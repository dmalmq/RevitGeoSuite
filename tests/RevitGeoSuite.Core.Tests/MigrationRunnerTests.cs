using System;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Versioning;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public void Migrate_upgrades_v0_payload_to_current_schema()
    {
        GeoProjectInfo legacyInfo = new GeoProjectInfo
        {
            SchemaVersion = 0,
            SetupSource = "LegacyFixture"
        };

        GeoProjectInfo migrated = MigrationRunner.Migrate(legacyInfo);

        Assert.Equal(SchemaVersion.Current, migrated.SchemaVersion);
        Assert.Equal("LegacyFixture", migrated.SetupSource);
    }

    [Fact]
    public void Migrate_rejects_future_schema_versions()
    {
        GeoProjectInfo futureInfo = new GeoProjectInfo { SchemaVersion = SchemaVersion.Current + 1 };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => MigrationRunner.Migrate(futureInfo));

        Assert.Contains("newer", error.Message);
    }
}
