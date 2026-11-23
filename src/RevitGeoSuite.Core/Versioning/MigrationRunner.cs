using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Core.Versioning;

public static class MigrationRunner
{
    public static GeoProjectInfo Migrate(GeoProjectInfo legacyInfo)
    {
        if (legacyInfo is null)
        {
            throw new ArgumentNullException(nameof(legacyInfo));
        }

        if (legacyInfo.SchemaVersion > SchemaVersion.Current)
        {
            throw new InvalidOperationException("Stored schema version is newer than this build supports.");
        }

        if (legacyInfo.SchemaVersion <= 0)
        {
            legacyInfo.SchemaVersion = 1;
        }

        MigrateCrs(legacyInfo);
        return legacyInfo;
    }

    public static GeoreferenceModuleState Migrate(GeoreferenceModuleState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        WorkingProjectBasePointReference? workingPoint = state.WorkingProjectBasePoint;
        if (workingPoint?.ProjectCrs is not null)
        {
            workingPoint.ProjectCrs = JapanCrsPresets.MigrateLegacyReference(workingPoint.ProjectCrs);
        }

        return state;
    }

    /// <summary>Migrates only the CRS, so loading an undo snapshot never rejects its schema version.</summary>
    public static GeoreferenceUndoSnapshot Migrate(GeoreferenceUndoSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.PreviousGeoInfo is not null)
        {
            MigrateCrs(snapshot.PreviousGeoInfo);
        }

        if (snapshot.PreviousModuleState is not null)
        {
            Migrate(snapshot.PreviousModuleState);
        }

        return snapshot;
    }

    private static void MigrateCrs(GeoProjectInfo info)
    {
        if (info.ProjectCrs is not null)
        {
            info.ProjectCrs = JapanCrsPresets.MigrateLegacyReference(info.ProjectCrs);
        }
    }
}
