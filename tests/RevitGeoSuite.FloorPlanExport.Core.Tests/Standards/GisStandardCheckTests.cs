using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisStandardCheckTests
{
    private static List<GisBoundParameterState> AllStandard(params string[] skip)
    {
        return GisSharedParameters.All
            .Where(definition => !skip.Contains(definition.Name))
            .Select(definition => new GisBoundParameterState(definition.Name, definition.Guid, definition.Binding))
            .ToList();
    }

    private static GisStandardModelState SetUpModel(string version = "1") => new()
    {
        StandardVersionText = version,
        BoundParameters = AllStandard(),
        GisViewCount = 3,
    };

    [Fact]
    public void ASetUpModelOnTheCurrentVersionIsCurrentWithNoIssues()
    {
        GisStandardStatus status = GisStandardCheck.Check(SetUpModel());

        Assert.Equal(GisStandardState.Current, status.State);
        Assert.Empty(status.Issues);
        Assert.False(status.BlocksExport);
        Assert.True(status.AppliesToModel);
        Assert.Equal("Current", status.ShortLabel);
    }

    [Fact]
    public void AModelWithoutStampOrParametersIsNotAGisModel()
    {
        GisStandardStatus status = GisStandardCheck.Check(new GisStandardModelState());

        Assert.Equal(GisStandardState.NotSetUp, status.State);
        Assert.False(status.AppliesToModel);
        Assert.False(status.BlocksExport);
        Assert.Equal("Not set up", status.ShortLabel);
    }

    [Fact]
    public void AnOldExportModelWithTheSixParametersUnderOwnGuidsIsAGisModelAndStillExports()
    {
        List<GisBoundParameterState> legacy = GisStandardCheck.LegacyParameterNames
            .Select(name => new GisBoundParameterState(name, Guid.NewGuid(), GisParameterBinding.Instance))
            .ToList();

        GisStandardStatus status = GisStandardCheck.Check(new GisStandardModelState { BoundParameters = legacy });

        Assert.True(status.AppliesToModel);
        Assert.False(status.BlocksExport);
        Assert.Contains(status.Issues, issue => issue.Message.Contains("keep the model's own GUIDs"));
    }

    [Fact]
    public void AGisParameterUnderAnotherGuidBlocksTheExport()
    {
        List<GisBoundParameterState> parameters = AllStandard(GisSharedParameters.GisCategory);
        parameters.Add(new GisBoundParameterState(GisSharedParameters.GisCategory, Guid.NewGuid(), GisParameterBinding.Type));
        GisStandardModelState model = SetUpModel();
        model.BoundParameters = parameters;

        GisStandardStatus status = GisStandardCheck.Check(model);

        Assert.True(status.BlocksExport);
        Assert.Equal("Needs fixing", status.ShortLabel);
        Assert.Contains(status.Issues, issue => issue.BlocksExport && issue.Message.Contains("gis_category"));
    }

    [Fact]
    public void AProjectParameterNamedLikeAGisParameterBlocksTheExport()
    {
        List<GisBoundParameterState> parameters = AllStandard(GisSharedParameters.GisKind);
        parameters.Add(new GisBoundParameterState(GisSharedParameters.GisKind, null, GisParameterBinding.Type));
        GisStandardModelState model = SetUpModel();
        model.BoundParameters = parameters;

        Assert.True(GisStandardCheck.Check(model).BlocksExport);
    }

    [Fact]
    public void AWrongBindingKindWarnsWithoutBlocking()
    {
        List<GisBoundParameterState> parameters = AllStandard(GisSharedParameters.GisRestriction);
        GisParameterDefinition restriction = GisSharedParameters.All.Single(definition => definition.Name == GisSharedParameters.GisRestriction);
        parameters.Add(new GisBoundParameterState(restriction.Name, restriction.Guid, GisParameterBinding.Instance));
        GisStandardModelState model = SetUpModel();
        model.BoundParameters = parameters;

        GisStandardStatus status = GisStandardCheck.Check(model);

        Assert.False(status.BlocksExport);
        Assert.Contains(status.Issues, issue => issue.Message.Contains("bound as an instance parameter"));
    }

    [Fact]
    public void AGisParameterUnderAnotherGuidBlocksEvenWhenBoundAsTheWrongKind()
    {
        List<GisBoundParameterState> parameters = AllStandard(GisSharedParameters.GisRestriction);
        parameters.Add(new GisBoundParameterState(GisSharedParameters.GisRestriction, Guid.NewGuid(), GisParameterBinding.Instance));
        GisStandardModelState model = SetUpModel();
        model.BoundParameters = parameters;

        GisStandardStatus status = GisStandardCheck.Check(model);

        Assert.True(status.BlocksExport);
        Assert.Contains(status.Issues, issue => issue.Message.Contains("bound as an instance parameter"));
    }

    [Fact]
    public void ABlockedModelThatIsNotSetUpSaysItNeedsFixing()
    {
        GisStandardModelState model = new()
        {
            BoundParameters = new List<GisBoundParameterState>
            {
                new(GisSharedParameters.GisKind, null, GisParameterBinding.Type),
            },
        };

        GisStandardStatus status = GisStandardCheck.Check(model);

        Assert.Equal(GisStandardState.NotSetUp, status.State);
        Assert.True(status.BlocksExport);
        Assert.Equal("Needs fixing", status.ShortLabel);
    }

    [Theory]
    [InlineData("0", GisStandardState.Unsupported, true)]
    [InlineData("2", GisStandardState.NewerThanTools, false)]
    [InlineData("v1", GisStandardState.Unreadable, false)]
    [InlineData(" 1 ", GisStandardState.Current, false)]
    public void TheVersionStampDecidesTheState(string version, GisStandardState expected, bool blocks)
    {
        GisStandardStatus status = GisStandardCheck.Check(SetUpModel(version));

        Assert.Equal(expected, status.State);
        Assert.Equal(blocks, status.BlocksExport);
        Assert.True(status.AppliesToModel);
    }

    [Fact]
    public void MissingGisViewsAndUntypedFloorTypesWarn()
    {
        GisStandardModelState model = SetUpModel();
        model.GisViewCount = 0;
        model.FloorTypesWithoutCategory = Enumerable.Range(1, 7).Select(index => $"type {index}").ToList();

        GisStandardStatus status = GisStandardCheck.Check(model);

        Assert.False(status.BlocksExport);
        Assert.Contains(status.Issues, issue => issue.Message.Contains("no GIS_ plan view"));
        Assert.Contains(status.Issues, issue => issue.Message.Contains("7 floor types") && issue.Message.Contains("and 2 more"));
    }
}
