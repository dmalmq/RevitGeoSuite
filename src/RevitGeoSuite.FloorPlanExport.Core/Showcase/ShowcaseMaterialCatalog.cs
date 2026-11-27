using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Explicit geometric provenance recorded on Forma Companion showcase elements.</summary>
public enum ShowcaseGeometryProvenance
{
    /// <summary>Geometry and elevations come directly from surveyed / authoritative coordinates or level TP.</summary>
    Surveyed,

    /// <summary>Geometry or elevation was synthesized schematically (e.g. straight connector path or estimated/default floor elevation).</summary>
    Schematic,
}

/// <summary>Revit-API-free RGB + transparency specification for Forma Companion showcase materials.</summary>
public readonly struct ShowcaseColorRgb : IEquatable<ShowcaseColorRgb>
{
    public ShowcaseColorRgb(byte r, byte g, byte b, byte transparencyPercent = 0)
    {
        R = r;
        G = g;
        B = b;
        TransparencyPercent = transparencyPercent > 100 ? (byte)100 : transparencyPercent;
    }

    public byte R { get; }

    public byte G { get; }

    public byte B { get; }

    public byte TransparencyPercent { get; }

    public bool Equals(ShowcaseColorRgb other) =>
        R == other.R && G == other.G && B == other.B && TransparencyPercent == other.TransparencyPercent;

    public override bool Equals(object? obj) => obj is ShowcaseColorRgb other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = R;
            hash = (hash * 397) ^ G;
            hash = (hash * 397) ^ B;
            hash = (hash * 397) ^ TransparencyPercent;
            return hash;
        }
    }
}

/// <summary>Named showcase material definition created in a Forma Companion document.</summary>
public sealed class ShowcaseMaterialSpec
{
    public ShowcaseMaterialSpec(string materialName, ShowcaseColorRgb color, string description)
    {
        MaterialName = materialName ?? throw new ArgumentNullException(nameof(materialName));
        Color = color;
        Description = description ?? string.Empty;
    }

    public string MaterialName { get; }

    public ShowcaseColorRgb Color { get; }

    public string Description { get; }
}

/// <summary>
/// Canonical <c>M_KS_*</c> showcase materials aligned to <c>NetworkOverlay</c> (<c>NetworkInk</c>) colors,
/// distinct POI category beacons, and red <c>net_junction</c> QA finding alerts.
/// </summary>
public static class ShowcaseMaterialCatalog
{
    // NetworkOverlay-aligned network materials (exact RGB parity with NetworkOverlay.ColorOf)
    public const string NetWalk = "M_KS_Net_Walk";
    public const string NetWalkNotStepFree = "M_KS_Net_WalkNotStepFree";
    public const string NetElevator = "M_KS_Net_Elevator";
    public const string NetEscalator = "M_KS_Net_Escalator";
    public const string NetStairs = "M_KS_Net_Stairs";
    public const string NetRamp = "M_KS_Net_Ramp";
    public const string NetMovingWalkway = "M_KS_Net_MovingWalkway";
    public const string NetTicketGate = "M_KS_Net_TicketGate";
    public const string NetSchematic = "M_KS_Net_Schematic";
    public const string NetNode = "M_KS_Net_Node";
    public const string NetChevron = "M_KS_Net_Chevron";

    // POI & point_facility_network materials
    public const string PoiAccessibility = "M_KS_Poi_Accessibility";
    public const string PoiTransit = "M_KS_Poi_Transit";
    public const string PoiSafety = "M_KS_Poi_Safety";
    public const string PoiCommercial = "M_KS_Poi_Commercial";
    public const string PoiGeneral = "M_KS_Poi_General";
    public const string PoiAttachment = "M_KS_Poi_Attachment";
    public const string PoiAttachmentUnmatched = "M_KS_Poi_AttachmentUnmatched";

    // QA finding / net_junction materials (exact RGB parity with NetworkInk.Warning & NetworkInk.Info)
    public const string FindingAlert = "M_KS_Finding_Alert";
    public const string FindingInfo = "M_KS_Finding_Info";

    private static readonly IReadOnlyDictionary<string, ShowcaseMaterialSpec> SpecsByName =
        BuildCatalog().ToDictionary(spec => spec.MaterialName, StringComparer.OrdinalIgnoreCase);

    /// <summary>All built-in F4 showcase materials in deterministic order.</summary>
    public static IReadOnlyList<ShowcaseMaterialSpec> AllMaterials { get; } = BuildCatalog();

    public static bool TryGet(string? materialName, out ShowcaseMaterialSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(materialName) && SpecsByName.TryGetValue(materialName!.Trim(), out ShowcaseMaterialSpec? found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    public static ShowcaseMaterialSpec GetRequired(string materialName)
    {
        if (TryGet(materialName, out ShowcaseMaterialSpec spec))
        {
            return spec;
        }

        throw new KeyNotFoundException($"Unknown showcase material '{materialName}'.");
    }

    /// <summary>Formats <see cref="ShowcaseGeometryProvenance"/> as canonical lowercase <c>"surveyed"</c> or <c>"schematic"</c>.</summary>
    public static string ToProvenanceString(ShowcaseGeometryProvenance provenance) =>
        provenance == ShowcaseGeometryProvenance.Schematic ? "schematic" : "surveyed";

    /// <summary>Resolves the <c>M_KS_Poi_*</c> material name for a <c>point_facility</c> feature.</summary>
    public static string ResolvePoiMaterialName(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes)
    {
        string token = ReadFirstText(attributes, "category", "facility_type", "poi_type", "type", "sub_category", "class", "name_en", "name_ja");
        if (string.IsNullOrEmpty(token))
        {
            return PoiGeneral;
        }

        string lower = token.ToLowerInvariant();
        if (ContainsAny(lower, "toilet", "restroom", "wc", "wheelchair", "accessible", "barrier", "nursing", "baby", "ostomy", "elevator", "lift", "多機能", "トイレ", "エレベータ", "授乳"))
        {
            return PoiAccessibility;
        }

        if (ContainsAny(lower, "aed", "emergency", "medical", "first_aid", "defib", "fire", "rescue", "info", "information", "police", "案内", "救護", "非常"))
        {
            return PoiSafety;
        }

        if (ContainsAny(lower, "ticket", "gate", "fare", "turnstile", "locker", "platform", "exit", "entrance", "escalator", "stair", "boarding", "改札", "券売", "出口", "ロッカー", "ホーム"))
        {
            return PoiTransit;
        }

        if (ContainsAny(lower, "shop", "store", "retail", "cafe", "restaurant", "atm", "bank", "vending", "kiosk", "convenience", "売店", "店舗", "カフェ"))
        {
            return PoiCommercial;
        }

        return PoiGeneral;
    }

    /// <summary>Resolves the <c>M_KS_Net_*</c> material name for a horizontal <c>network_link</c> feature.</summary>
    public static string ResolveNetworkLinkMaterialName(
        IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes,
        ShowcaseGeometryProvenance provenance)
    {
        string typeToken = ReadFirstText(attributes, "link_type", "route_type", "connector_kind", "kind", "type", "category", "structure");
        if (TryMatchCirculationMaterial(typeToken, out string circulationMaterial))
        {
            return circulationMaterial;
        }

        if (IsExplicitlyNotStepFree(attributes))
        {
            return NetWalkNotStepFree;
        }

        if (!string.IsNullOrEmpty(typeToken) &&
            ContainsAny(typeToken.ToLowerInvariant(), "schematic", "inferred", "estimated", "virtual"))
        {
            return NetSchematic;
        }

        // Preserve NetworkOverlay green walkway default unless explicitly schematic-only without a known walkway type.
        if (provenance == ShowcaseGeometryProvenance.Schematic && string.IsNullOrEmpty(typeToken))
        {
            return NetSchematic;
        }

        return NetWalk;
    }

    /// <summary>Resolves the <c>M_KS_Net_*</c> material name for a vertical transition tube (<c>network_connector</c> or vertical <c>network_link</c>).</summary>
    public static string ResolveVerticalTubeMaterialName(
        IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes,
        ShowcaseGeometryProvenance provenance)
    {
        string typeToken = ReadFirstText(attributes, "connector_kind", "kind", "link_type", "route_type", "type", "category", "structure");
        if (TryMatchCirculationMaterial(typeToken, out string circulationMaterial))
        {
            return circulationMaterial;
        }

        if (provenance == ShowcaseGeometryProvenance.Schematic)
        {
            return NetSchematic;
        }

        return IsExplicitlyNotStepFree(attributes) ? NetWalkNotStepFree : NetElevator;
    }

    /// <summary>Resolves the <c>M_KS_Finding_*</c> material name for a <c>net_junction</c> finding puck.</summary>
    public static string ResolveFindingMaterialName(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes)
    {
        string severity = ReadFirstText(attributes, "severity", "level", "finding_severity", "status").ToLowerInvariant();
        if (severity == "info" || severity == "information" || severity == "note")
        {
            return FindingInfo;
        }

        return FindingAlert;
    }

    private static bool TryMatchCirculationMaterial(string typeToken, out string materialName)
    {
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            materialName = string.Empty;
            return false;
        }

        string lower = typeToken.Trim().ToLowerInvariant();
        if (ContainsAny(lower, "elevator", "lift", "ev", "エレベータ"))
        {
            materialName = NetElevator;
            return true;
        }

        if (ContainsAny(lower, "escalator", "esc", "エスカレータ"))
        {
            materialName = NetEscalator;
            return true;
        }

        if (ContainsAny(lower, "moving_walkway", "movingwalkway", "travelator", "autowalk", "動く歩道"))
        {
            materialName = NetMovingWalkway;
            return true;
        }

        if (ContainsAny(lower, "stair", "step", "階段"))
        {
            materialName = NetStairs;
            return true;
        }

        if (ContainsAny(lower, "ramp", "slope", "スロープ"))
        {
            materialName = NetRamp;
            return true;
        }

        if (ContainsAny(lower, "ticket_gate", "ticketgate", "turnstile", "fare_gate", "gate", "改札"))
        {
            materialName = NetTicketGate;
            return true;
        }

        if (ContainsAny(lower, "walkway", "walk", "corridor", "concourse", "passage", "footway", "通路"))
        {
            materialName = NetWalk;
            return true;
        }

        materialName = string.Empty;
        return false;
    }

    private static bool IsExplicitlyNotStepFree(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes)
    {
        foreach (string key in new[] { "step_free", "stepfree", "wheelchair", "barrier_free", "accessible" })
        {
            if (!attributes.TryGetValue(key, out ShowcaseAttributeValue? val) || val is null || val.IsNull)
            {
                continue;
            }

            if (val.Kind == ShowcaseAttributeKind.Boolean && val.BoolValue == false)
            {
                return true;
            }

            if (val.Kind == ShowcaseAttributeKind.Integer && val.Int64Value == 0)
            {
                return true;
            }

            if (val.Kind == ShowcaseAttributeKind.Text)
            {
                string t = (val.TextValue ?? string.Empty).Trim().ToLowerInvariant();
                if (t == "false" || t == "no" || t == "0" || t == "limited" || t == "unavailable")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ReadFirstText(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (attributes.TryGetValue(key, out ShowcaseAttributeValue? val) && val != null && !val.IsNull)
            {
                string? text = val.ToExactString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text!.Trim();
                }
            }
        }

        return string.Empty;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (haystack.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ShowcaseMaterialSpec> BuildCatalog() => new ShowcaseMaterialSpec[]
    {
        // Exact NetworkOverlay.cs RGB parity
        new(NetWalk, new ShowcaseColorRgb(0, 150, 80), "Step-free walking network link (NetworkOverlay Walk green)."),
        new(NetWalkNotStepFree, new ShowcaseColorRgb(230, 120, 0), "Non-step-free walking link (NetworkOverlay WalkNotStepFree orange)."),
        new(NetElevator, new ShowcaseColorRgb(0, 110, 220), "Elevator vertical connector tube (NetworkOverlay Elevator blue)."),
        new(NetEscalator, new ShowcaseColorRgb(150, 60, 200), "Escalator vertical connector tube (NetworkOverlay Escalator purple)."),
        new(NetStairs, new ShowcaseColorRgb(140, 90, 40), "Stairs vertical connector tube (NetworkOverlay Stairs brown)."),
        new(NetRamp, new ShowcaseColorRgb(200, 160, 0), "Ramp sloped connector tube (NetworkOverlay Ramp amber)."),
        new(NetMovingWalkway, new ShowcaseColorRgb(0, 160, 170), "Moving walkway connector/ribbon (NetworkOverlay MovingWalkway teal)."),
        new(NetTicketGate, new ShowcaseColorRgb(215, 75, 130), "Ticket gate / fare-control passage link."),
        new(NetSchematic, new ShowcaseColorRgb(90, 140, 230, transparencyPercent: 25), "Schematic / level-synthesized network geometry (NetworkOverlay Info blue)."),
        new(NetNode, new ShowcaseColorRgb(110, 110, 110), "Standard walking network node disc."),
        new(NetChevron, new ShowcaseColorRgb(245, 248, 252), "High-contrast directional chevron accent on one-way network ribbons."),

        // POI & attachment markers
        new(PoiAccessibility, new ShowcaseColorRgb(0, 120, 215), "Accessibility / restroom / barrier-free POI marker."),
        new(PoiTransit, new ShowcaseColorRgb(0, 155, 115), "Transit / ticketing / platform / exit POI marker."),
        new(PoiSafety, new ShowcaseColorRgb(225, 135, 0), "Safety / emergency / information POI marker."),
        new(PoiCommercial, new ShowcaseColorRgb(135, 95, 185), "Commercial / retail / ATM POI marker."),
        new(PoiGeneral, new ShowcaseColorRgb(40, 135, 195), "General station point_facility POI marker."),
        new(PoiAttachment, new ShowcaseColorRgb(30, 175, 185), "Matched point_facility_network attachment marker and connector stem."),
        new(PoiAttachmentUnmatched, new ShowcaseColorRgb(230, 130, 20), "Unmatched point_facility_network attachment marker."),

        // Findings
        new(FindingAlert, new ShowcaseColorRgb(220, 30, 30), "QA / topology alert net_junction finding puck (NetworkOverlay Warning red)."),
        new(FindingInfo, new ShowcaseColorRgb(90, 140, 230), "Informational net_junction finding puck (NetworkOverlay Info blue)."),
    };
}
