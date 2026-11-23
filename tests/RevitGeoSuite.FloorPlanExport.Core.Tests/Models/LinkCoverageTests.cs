using System;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class LinkCoverageTests
{
    [Fact]
    public void ThrowIfIncomplete_AllowsFullCoverage()
    {
        LinkCoverage.ThrowIfIncomplete(Array.Empty<LinkCoverageProblem>());
        Assert.Null(LinkCoverage.DescribeProblems(Array.Empty<LinkCoverageProblem>()));
    }

    [Theory]
    [InlineData(LinkCoverageProblemKind.NotLoaded, "Not loaded: Structure.rvt")]
    [InlineData(LinkCoverageProblemKind.NoModelId, "No model id: Structure.rvt")]
    [InlineData(LinkCoverageProblemKind.ReadFailed, "Could not be read: Structure.rvt")]
    public void ThrowIfIncomplete_NamesTheLinkAndProblem(LinkCoverageProblemKind kind, string expected)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LinkCoverage.ThrowIfIncomplete(new[] { new LinkCoverageProblem("Structure.rvt", kind) }));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void DescribeProblems_ListsEveryLinkOnceWithItsDetail()
    {
        string? message = LinkCoverage.DescribeProblems(new[]
        {
            new LinkCoverageProblem("B.rvt", LinkCoverageProblemKind.NotLoaded),
            new LinkCoverageProblem("A.rvt", LinkCoverageProblemKind.NotLoaded),
            new LinkCoverageProblem("A.rvt", LinkCoverageProblemKind.NotLoaded),
            new LinkCoverageProblem("C.rvt", LinkCoverageProblemKind.ReadFailed, "view 'L1': boom"),
        });

        Assert.Contains("Not loaded: A.rvt, B.rvt.", message);
        Assert.Contains("C.rvt (view 'L1': boom)", message);
    }
}
