using System;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class RevitWriteTextTests
{
    [Fact]
    public void EveryInstanceParameterAGroupMemberCanHoldVariesByGroupInstance()
    {
        string[] vary = GisSharedParameters.VaryByGroupInstance(GisSharedParameters.All).ToArray();
        Assert.Contains(GisSharedParameters.Id, vary);
        Assert.Contains(GisSharedParameters.LevelId, vary);
        Assert.Contains(GisSharedParameters.Ordinal, vary);
        Assert.Contains(GisSharedParameters.Category, vary);
        Assert.Contains(GisSharedParameters.GisKey, vary);

        // Type parameters cannot vary by group instance, and Project Information is never in a group.
        Assert.DoesNotContain(vary, name => GisSharedParameters.All.Single(definition => definition.Name == name).Binding == GisParameterBinding.Type);
        Assert.DoesNotContain(GisSharedParameters.GisStandardVersion, vary);
        Assert.DoesNotContain(GisSharedParameters.GisModelRole, vary);
        Assert.DoesNotContain(GisSharedParameters.GisKitVersion, vary);
    }

    [Fact]
    public void AFieldListParameterVariesLikeTheStandardOnes()
    {
        GisParameterDefinition extra = new("shop_code", Guid.NewGuid(), GisParameterDataType.Text, GisParameterBinding.Instance, new[] { "OST_Floors" }, "A station field.");
        Assert.Equal(new[] { "shop_code" }, GisSharedParameters.VaryByGroupInstance(new[] { extra }));
    }

    [Fact]
    public void NamesAParameterThatStillHoldsOneValuePerGroupType()
    {
        Assert.Equal(
            "'id' still holds one value per group type (Sato has borrowed it; ask them to synchronize and relinquish), so writing it on an element in a group edits the whole group.",
            GisSharedParameters.AlignedPerGroupLine("id", "Sato has borrowed it; ask them to synchronize and relinquish"));
    }

    [Fact]
    public void ListsEachRevitWarningOnce()
    {
        Assert.Equal(new[] { "Revit warned: Highlighted floors overlap.", "Revit warned: Room is not enclosed." },
            RevitWriteText.WarningLines(new[] { "Highlighted floors overlap.", "Room is not enclosed.", "Highlighted floors overlap." }));
    }

    [Fact]
    public void SaysWhyAWriteChangedNothing()
    {
        Assert.Equal(
            "Revit did not commit 'IMDF Export - Assign IDs', so nothing was changed: Changes to groups are allowed only in group edit mode. 'id' still holds one value per group type (x), so writing it on an element in a group edits the whole group.",
            RevitWriteText.NotCommitted(
                "IMDF Export - Assign IDs",
                new[] { "Changes to groups are allowed only in group edit mode.", "Changes to groups are allowed only in group edit mode." },
                new[] { GisSharedParameters.AlignedPerGroupLine("id", "x") }));
        Assert.Equal("Revit did not commit 'Kanizu GIS: Set up model', so nothing was changed.", RevitWriteText.NotCommitted("Kanizu GIS: Set up model", Array.Empty<string>(), Array.Empty<string>()));
    }
}
