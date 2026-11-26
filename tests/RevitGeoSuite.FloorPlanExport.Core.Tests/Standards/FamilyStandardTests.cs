using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class FamilyStandardTests
{
    [Fact]
    public void TheShippedListHasEveryKindOnce()
    {
        Assert.Equal(
            new[] { "escalator", "moving_walkway", "elevator", "ticket_gate" },
            FamilyStandard.Embedded.Kinds.Select(kind => kind.Kind));
    }

    [Fact]
    public void EveryRequiredParameterIsAStandardTypeParameter()
    {
        foreach (StandardFamilyKind kind in FamilyStandard.Embedded.Kinds)
        {
            Assert.Contains(GisSharedParameters.GisKind, kind.RequiredTypeParameters);
            foreach (string name in kind.RequiredTypeParameters)
            {
                Assert.True(GisSharedParameters.TryGet(name, out GisParameterDefinition definition), name);
                Assert.Equal(GisParameterBinding.Type, definition.Binding);
            }
        }
    }

    [Fact]
    public void TheExportCatalogTakesTheListedFamiliesAsBefore()
    {
        // The four legacy names plus the four Starter Kit circulation/gate families registered in standards/families.json.
        IReadOnlyDictionary<string, ZoneInfo> families = ZoneCatalog.CreateDefault().FamilyLookup;

        Assert.Equal(
            new[]
            {
                ("KNZ_CIRC_Elevator", "elevator", "E0E0E0"),
                ("KNZ_CIRC_Escalator", "escalator", "D0D0D0"),
                ("KNZ_CIRC_MovingWalkway", "movingwalkway", "E8F6FF"),
                ("KNZ_FIX_TicketGateBank", "fixture", "D4C5A9"),
                ("j EV", "elevator", "E0E0E0"),
                ("j エスカレータ-lightweight", "escalator", "D0D0D0"),
                ("j エスカレーター-lightweight", "escalator", "D0D0D0"),
                ("j 自動改札", "fixture", "D4C5A9"),
            },
            families.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value.Category, entry.Value.FillColor)));
        Assert.All(families.Values, info => Assert.Null(info.Restriction));
    }

    [Fact]
    public void KindOfFindsAListedFamilyByItsExactName()
    {
        Assert.Equal("elevator", FamilyStandard.Embedded.KindOf(" j EV ")?.Kind);
        Assert.Null(FamilyStandard.Embedded.KindOf("j ev"));
        Assert.Null(FamilyStandard.Embedded.KindOf("Lift"));
        Assert.Equal("moving_walkway", FamilyStandard.Embedded.Kind("movingwalkway")?.Kind);
    }

    [Fact]
    public void AFamilyListedTwiceOrAnUnknownKindIsRefused()
    {
        Assert.Throws<FormatException>(() => FamilyStandard.Parse(@"{ ""kinds"": [ { ""kind"": ""lift"", ""families"": [] } ] }"));
        Assert.Throws<FormatException>(() => FamilyStandard.Parse(@"{ ""kinds"": [
            { ""kind"": ""elevator"", ""families"": [ ""j EV"" ] },
            { ""kind"": ""escalator"", ""families"": [ ""j EV"" ] } ] }"));
    }

    [Fact]
    public void GisKindIsBoundToMechanicalEquipmentWhereLumineDrawsItsElevators()
    {
        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisKind, out GisParameterDefinition definition));
        Assert.Equal(new[] { "OST_GenericModel", "OST_SpecialityEquipment", "OST_MechanicalEquipment" }, definition.Categories);
    }
}
