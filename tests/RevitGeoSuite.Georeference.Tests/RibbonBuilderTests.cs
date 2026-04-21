using System;
using RevitGeoSuite.Shell;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class RibbonBuilderTests
{
    [Fact]
    public void IsDuplicateRibbonTabException_returns_true_for_duplicate_argument_exception_message()
    {
        ArgumentException exception = new ArgumentException("A ribbon tab with the name 'Revit Geo Suite' already exists.");

        Assert.True(RibbonBuilder.IsDuplicateRibbonTabException(exception));
    }

    [Fact]
    public void IsDuplicateRibbonTabException_returns_false_for_non_duplicate_errors()
    {
        InvalidOperationException exception = new InvalidOperationException("Ribbon startup failed for another reason.");

        Assert.False(RibbonBuilder.IsDuplicateRibbonTabException(exception));
    }
}
