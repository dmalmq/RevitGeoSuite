using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitGeoSuite.FloorPlanExport.Core.Federation;

/// <summary>
/// Evaluates shared-coordinate rigid pose consistency between a host model and a linked model using
/// 3 non-collinear control points over a long baseline (typically <c>(0,0,0)</c>, <c>(100m,0,0)</c>,
/// <c>(0,100m,0)</c> in the link's internal frame).
/// </summary>
/// <remarks>
/// Because both chains map the same 3 physical points into real-world shared coordinates
/// (Easting, Northing, Elevation in meters), intentional differences in Survey Point or Project Base
/// Point placement cancel out cleanly, while translation drift, True-North rotation errors, scale skew,
/// reflection, and vertical Z drift are measured accurately in mm, arcseconds/degrees, and ppm.
/// </remarks>
public static class PoseConsistencyEvaluator
{
    private const double MinBaselineLengthMeters = 1e-3d;
    private const double MinNonCollinearSinAngle = 1e-3d;
    private const double RadiansToDegrees = 180.0d / Math.PI;
    private const double ToleranceEpsilon = 1e-6d;

    public static PoseConsistencyResult Evaluate(
        IReadOnlyList<ControlPointPair>? controlPoints,
        FederationCheckOptions? options = null)
    {
        FederationCheckOptions effective = options ?? FederationCheckOptions.Default;

        if (controlPoints == null || controlPoints.Count == 0)
        {
            return new PoseConsistencyResult(
                isEvaluated: false,
                isAligned: true,
                originTranslationMm: 0d,
                maxHorizontalTranslationMm: 0d,
                maxVerticalDriftMm: 0d,
                signedOriginVerticalOffsetMm: 0d,
                rotationErrorDegrees: 0d,
                rotationErrorArcSeconds: 0d,
                shearDegrees: 0d,
                maxScaleRatioErrorPpm: 0d,
                isReflected: false,
                isDegenerateBaseline: false,
                failureReasonsEn: Array.Empty<string>(),
                failureReasonsJa: Array.Empty<string>());
        }

        if (controlPoints.Count < 3)
        {
            return DegenerateResult(
                $"Pose check requires at least 3 non-collinear control points (received {controlPoints.Count}).",
                $"姿勢整合チェックには一直線上にない 3 点以上の制御点が必要です（取得点数: {controlPoints.Count}）。");
        }

        ControlPointPair p0 = controlPoints[0];
        ControlPointPair p1 = controlPoints[1];
        ControlPointPair p2 = controlPoints[2];

        for (int i = 0; i < controlPoints.Count; i++)
        {
            ControlPointPair pair = controlPoints[i];
            if (pair == null || !pair.HostSharedMeters.IsFinite || !pair.LinkOwnSharedMeters.IsFinite)
            {
                return DegenerateResult(
                    $"Control point #{i + 1} contains non-finite shared coordinates (NaN or Infinity).",
                    $"制御点 #{i + 1} の共有座標に不正な数値（NaN または無限大）が含まれています。");
            }
        }

        double originTranslationMm = HorizontalDistanceMm(p0.HostSharedMeters, p0.LinkOwnSharedMeters);
        double signedOriginZMm = (p0.HostSharedMeters.Z - p0.LinkOwnSharedMeters.Z) * 1000.0d;
        double maxHorizontalMm = 0d;
        double maxVerticalMm = 0d;

        for (int i = 0; i < controlPoints.Count; i++)
        {
            ControlPointPair pair = controlPoints[i];
            double hMm = HorizontalDistanceMm(pair.HostSharedMeters, pair.LinkOwnSharedMeters);
            double zMm = Math.Abs(pair.HostSharedMeters.Z - pair.LinkOwnSharedMeters.Z) * 1000.0d;
            if (hMm > maxHorizontalMm)
            {
                maxHorizontalMm = hMm;
            }

            if (zMm > maxVerticalMm)
            {
                maxVerticalMm = zMm;
            }
        }

        double uxH = p1.HostSharedMeters.X - p0.HostSharedMeters.X;
        double uyH = p1.HostSharedMeters.Y - p0.HostSharedMeters.Y;
        double vxH = p2.HostSharedMeters.X - p0.HostSharedMeters.X;
        double vyH = p2.HostSharedMeters.Y - p0.HostSharedMeters.Y;

        double uxK = p1.LinkOwnSharedMeters.X - p0.LinkOwnSharedMeters.X;
        double uyK = p1.LinkOwnSharedMeters.Y - p0.LinkOwnSharedMeters.Y;
        double vxK = p2.LinkOwnSharedMeters.X - p0.LinkOwnSharedMeters.X;
        double vyK = p2.LinkOwnSharedMeters.Y - p0.LinkOwnSharedMeters.Y;

        double lenUH = Math.Sqrt((uxH * uxH) + (uyH * uyH));
        double lenVH = Math.Sqrt((vxH * vxH) + (vyH * vyH));
        double lenUK = Math.Sqrt((uxK * uxK) + (uyK * uyK));
        double lenVK = Math.Sqrt((vxK * vxK) + (vyK * vyK));

        if (lenUH < MinBaselineLengthMeters || lenVH < MinBaselineLengthMeters
            || lenUK < MinBaselineLengthMeters || lenVK < MinBaselineLengthMeters)
        {
            return DegenerateResult(
                "Control point baselines are too short (< 1 mm) to determine rotation and scale.",
                "制御点間の基線長が短すぎるため（1 mm 未満）、回転・スケール誤差を算出できません。");
        }

        double crossH = (uxH * vyH) - (uyH * vxH);
        double crossK = (uxK * vyK) - (uyK * vxK);
        double sinAngleH = Math.Abs(crossH) / (lenUH * lenVH);
        double sinAngleK = Math.Abs(crossK) / (lenUK * lenVK);

        if (sinAngleH < MinNonCollinearSinAngle || sinAngleK < MinNonCollinearSinAngle)
        {
            return DegenerateResult(
                "Control points are collinear; 3 non-collinear control points are required for pose verification.",
                "制御点がほぼ一直線上に並んでいます。姿勢検証には一直線上にない 3 点が必要です。");
        }

        bool isReflected = (crossH * crossK) < 0d;

        double angleURad = SignedAngleBetween(uxK, uyK, uxH, uyH);
        double angleVRad = SignedAngleBetween(vxK, vyK, vxH, vyH);
        double rotationRad = Math.Max(Math.Abs(angleURad), Math.Abs(angleVRad));
        double rotationDeg = rotationRad * RadiansToDegrees;
        double rotationArcSec = rotationDeg * 3600.0d;
        double shearRad = Math.Abs(NormalizeAngleRadians(angleURad - angleVRad));
        double shearDeg = shearRad * RadiansToDegrees;

        double scaleU = lenUH / lenUK;
        double scaleV = lenVH / lenVK;
        double maxScaleErrorPpm = Math.Max(Math.Abs(scaleU - 1.0d), Math.Abs(scaleV - 1.0d)) * 1_000_000.0d;

        List<string> failuresEn = new();
        List<string> failuresJa = new();

        if (isReflected)
        {
            failuresEn.Add("mirrored/reflected coordinate axes detected between host link transform and link shared coordinates");
            failuresJa.Add("ホスト側リンク変換とリンク自身の共有座標の間で軸の反転（ミラー）が検出されました");
        }

        if (maxHorizontalMm > effective.PoseHorizontalToleranceMm + ToleranceEpsilon)
        {
            failuresEn.Add(string.Format(
                CultureInfo.InvariantCulture,
                "horizontal translation drift {0:0.##} mm exceeds {1:0.##} mm tolerance",
                maxHorizontalMm,
                effective.PoseHorizontalToleranceMm));
            failuresJa.Add(string.Format(
                CultureInfo.InvariantCulture,
                "水平平行移動誤差 {0:0.##} mm が許容値 {1:0.##} mm を超えています",
                maxHorizontalMm,
                effective.PoseHorizontalToleranceMm));
        }

        if (maxVerticalMm > effective.PoseVerticalToleranceMm + ToleranceEpsilon)
        {
            failuresEn.Add(string.Format(
                CultureInfo.InvariantCulture,
                "vertical Z drift {0:0.##} mm exceeds {1:0.##} mm tolerance",
                maxVerticalMm,
                effective.PoseVerticalToleranceMm));
            failuresJa.Add(string.Format(
                CultureInfo.InvariantCulture,
                "鉛直 Z 誤差 {0:0.##} mm が許容値 {1:0.##} mm を超えています",
                maxVerticalMm,
                effective.PoseVerticalToleranceMm));
        }

        if (rotationArcSec > effective.PoseRotationToleranceArcSeconds + ToleranceEpsilon)
        {
            failuresEn.Add(string.Format(
                CultureInfo.InvariantCulture,
                "True-North / rotation error {0:0.##} arcsec ({1:0.####} deg) exceeds {2:0.##} arcsec tolerance",
                rotationArcSec,
                rotationDeg,
                effective.PoseRotationToleranceArcSeconds));
            failuresJa.Add(string.Format(
                CultureInfo.InvariantCulture,
                "真北／回転誤差 {0:0.##} 秒 ({1:0.####}°) が許容値 {2:0.##} 秒を超えています",
                rotationArcSec,
                rotationDeg,
                effective.PoseRotationToleranceArcSeconds));
        }

        if (maxScaleErrorPpm > effective.PoseScaleTolerancePpm + ToleranceEpsilon)
        {
            failuresEn.Add(string.Format(
                CultureInfo.InvariantCulture,
                "scale skew {0:0.#} ppm exceeds {1:0.#} ppm tolerance",
                maxScaleErrorPpm,
                effective.PoseScaleTolerancePpm));
            failuresJa.Add(string.Format(
                CultureInfo.InvariantCulture,
                "スケール誤差 {0:0.#} ppm が許容値 {1:0.#} ppm を超えています",
                maxScaleErrorPpm,
                effective.PoseScaleTolerancePpm));
        }

        return new PoseConsistencyResult(
            isEvaluated: true,
            isAligned: failuresEn.Count == 0,
            originTranslationMm: originTranslationMm,
            maxHorizontalTranslationMm: maxHorizontalMm,
            maxVerticalDriftMm: maxVerticalMm,
            signedOriginVerticalOffsetMm: signedOriginZMm,
            rotationErrorDegrees: rotationDeg,
            rotationErrorArcSeconds: rotationArcSec,
            shearDegrees: shearDeg,
            maxScaleRatioErrorPpm: maxScaleErrorPpm,
            isReflected: isReflected,
            isDegenerateBaseline: false,
            failureReasonsEn: failuresEn,
            failureReasonsJa: failuresJa);
    }

    private static PoseConsistencyResult DegenerateResult(string reasonEn, string reasonJa) =>
        new(
            isEvaluated: true,
            isAligned: false,
            originTranslationMm: 0d,
            maxHorizontalTranslationMm: 0d,
            maxVerticalDriftMm: 0d,
            signedOriginVerticalOffsetMm: 0d,
            rotationErrorDegrees: 0d,
            rotationErrorArcSeconds: 0d,
            shearDegrees: 0d,
            maxScaleRatioErrorPpm: 0d,
            isReflected: false,
            isDegenerateBaseline: true,
            failureReasonsEn: new[] { reasonEn },
            failureReasonsJa: new[] { reasonJa });

    private static double HorizontalDistanceMm(FederationPoint3D a, FederationPoint3D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) * 1000.0d;
    }

    private static double SignedAngleBetween(double ax, double ay, double bx, double by)
    {
        double cross = (ax * by) - (ay * bx);
        double dot = (ax * bx) + (ay * by);
        return Math.Atan2(cross, dot);
    }

    private static double NormalizeAngleRadians(double radians)
    {
        while (radians > Math.PI)
        {
            radians -= 2.0d * Math.PI;
        }

        while (radians < -Math.PI)
        {
            radians += 2.0d * Math.PI;
        }

        return radians;
    }
}
