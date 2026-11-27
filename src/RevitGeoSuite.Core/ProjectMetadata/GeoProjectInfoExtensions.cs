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

    /// <summary>
    /// Keeps the model's identity when <paramref name="info"/> was built fresh, as re-georeferencing
    /// does: the ModelId, the station folder and the federation role survive from
    /// <paramref name="previous"/> unless <paramref name="info"/> sets its own.
    /// </summary>
    public static void CarryForwardIdentity(this GeoProjectInfo info, GeoProjectInfo? previous)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        if (previous is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(info.ModelId))
        {
            info.ModelId = previous.ModelId;
        }

        if (string.IsNullOrWhiteSpace(info.GisStationFolder))
        {
            info.GisStationFolder = previous.GisStationFolder;
        }

        if (string.IsNullOrWhiteSpace(info.FederationMode))
        {
            info.FederationMode = previous.FederationMode;
            info.FederationHubModelId = previous.FederationHubModelId;
        }
    }

    public static bool NeedsIdentityCarriedForward(this GeoProjectInfo info) =>
        string.IsNullOrWhiteSpace(info.ModelId)
        || string.IsNullOrWhiteSpace(info.GisStationFolder)
        || string.IsNullOrWhiteSpace(info.FederationMode);

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
