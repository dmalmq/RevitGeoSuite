using System;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Core.ProjectMetadata;

public static class GeoProjectInfoExtensions
{
    /// <summary>
    /// Gives <paramref name="info"/> a <see cref="GeoProjectInfo.ModelId"/> when it has none.
    /// Returns true when it set one, so the caller knows to save.
    /// </summary>
    public static bool EnsureModelId(this GeoProjectInfo info, Func<Guid>? newGuid = null)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        if (!string.IsNullOrWhiteSpace(info.ModelId))
        {
            return false;
        }

        info.ModelId = (newGuid ?? Guid.NewGuid)().ToString("D");
        return true;
    }

    public static void ApplyCanonicalLocation(
        this GeoProjectInfo info,
        CrsReference? projectCrs,
        ProjectOrigin? origin,
        double trueNorthAngle)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        bool canonicalLocationChanged = !CrsReferenceEquals(info.ProjectCrs, projectCrs)
            || !ProjectOriginEquals(info.Origin, origin)
            || info.TrueNorthAngle != trueNorthAngle;

        info.ProjectCrs = projectCrs;
        info.Origin = origin;
        info.TrueNorthAngle = trueNorthAngle;

        if (canonicalLocationChanged)
        {
            info.PrimaryMeshCode = null;
        }
    }

    private static bool CrsReferenceEquals(CrsReference? left, CrsReference? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.EpsgCode == right.EpsgCode
            && string.Equals(left.NameSnapshot, right.NameSnapshot, StringComparison.Ordinal);
    }

    private static bool ProjectOriginEquals(ProjectOrigin? left, ProjectOrigin? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Latitude == right.Latitude
            && left.Longitude == right.Longitude
            && left.ElevationMeters == right.ElevationMeters;
    }
}
