using System;
using RevitGeoSuite.Core.ProjectMetadata;

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

        return legacyInfo;
    }
}
