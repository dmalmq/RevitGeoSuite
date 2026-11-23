using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Versioning;
using RevitGeoSuite.Core.Workflow;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class JapanCrsEpsgMigrationTests
{
    [Fact]
    public void Registry_holds_exactly_the_19_official_japan_zones()
    {
        int[] codes = new CrsRegistry().GetAvailableDefinitions().Select(definition => definition.EpsgCode).ToArray();

        Assert.Equal(Enumerable.Range(6669, 19), codes);
    }

    [Fact]
    public void Every_legacy_jgd2024_code_migrates_to_its_official_zone()
    {
        CrsRegistry registry = new CrsRegistry();

        for (int offset = 0; offset < 19; offset++)
        {
            GeoProjectInfo legacy = new GeoProjectInfo
            {
                SchemaVersion = 1,
                ProjectCrs = new CrsReference
                {
                    EpsgCode = 10267 + offset,
                    NameSnapshot = $"JGD2024 / Japan Plane Rectangular CS {offset + 1}"
                }
            };

            GeoProjectInfo migrated = MigrationRunner.Migrate(legacy);

            Assert.Equal(6669 + offset, migrated.ProjectCrs!.EpsgCode);
            Assert.True(registry.TryGetByEpsgCode(migrated.ProjectCrs.EpsgCode, out CrsDefinition? definition));
            Assert.Equal(offset + 1, definition!.JapanZoneNumber);
            Assert.Equal(definition.Name, migrated.ProjectCrs.NameSnapshot);
        }
    }

    [Fact]
    public void Every_legacy_code_in_the_working_project_base_point_migrates()
    {
        for (int offset = 0; offset < 19; offset++)
        {
            GeoreferenceModuleState state = new GeoreferenceModuleState
            {
                WorkingProjectBasePoint = new WorkingProjectBasePointReference
                {
                    ProjectCrs = new CrsReference { EpsgCode = 10267 + offset, NameSnapshot = "JGD2024" }
                }
            };

            GeoreferenceModuleState migrated = MigrationRunner.Migrate(state);

            Assert.Equal(6669 + offset, migrated.WorkingProjectBasePoint!.ProjectCrs!.EpsgCode);
        }
    }

    [Fact]
    public void Module_state_without_a_working_point_is_unchanged()
    {
        GeoreferenceModuleState state = new GeoreferenceModuleState();

        Assert.Null(MigrationRunner.Migrate(state).WorkingProjectBasePoint);
    }

    [Fact]
    public void Every_legacy_code_in_an_undo_snapshot_migrates()
    {
        for (int offset = 0; offset < 19; offset++)
        {
            GeoreferenceUndoSnapshot snapshot = new GeoreferenceUndoSnapshot
            {
                PreviousGeoInfo = new GeoProjectInfo
                {
                    ProjectCrs = new CrsReference { EpsgCode = 10267 + offset, NameSnapshot = "JGD2024" }
                }
            };

            GeoreferenceUndoSnapshot migrated = MigrationRunner.Migrate(snapshot);

            Assert.Equal(6669 + offset, migrated.PreviousGeoInfo!.ProjectCrs!.EpsgCode);
        }
    }

    [Fact]
    public void Undo_snapshot_from_a_newer_schema_still_loads()
    {
        GeoreferenceUndoSnapshot snapshot = new GeoreferenceUndoSnapshot
        {
            PreviousGeoInfo = new GeoProjectInfo
            {
                SchemaVersion = SchemaVersion.Current + 1,
                ProjectCrs = new CrsReference { EpsgCode = 10275, NameSnapshot = "JGD2024" }
            }
        };

        GeoreferenceUndoSnapshot migrated = MigrationRunner.Migrate(snapshot);

        Assert.Equal(SchemaVersion.Current + 1, migrated.PreviousGeoInfo!.SchemaVersion);
        Assert.Equal(6677, migrated.PreviousGeoInfo.ProjectCrs!.EpsgCode);
    }

    [Theory]
    [InlineData(6677)]
    [InlineData(10266)]
    [InlineData(10286)]
    [InlineData(27700)]
    public void Codes_outside_the_legacy_range_are_left_unchanged(int epsgCode)
    {
        CrsReference reference = new CrsReference { EpsgCode = epsgCode, NameSnapshot = "stored name" };

        CrsReference migrated = JapanCrsPresets.MigrateLegacyReference(reference);

        Assert.Same(reference, migrated);
    }

    [Fact]
    public void Search_still_finds_zones_by_jgd2024()
    {
        Assert.Contains(new CrsRegistry().Search("JGD2024 zone 9"), definition => definition.EpsgCode == 6677);
    }
}
