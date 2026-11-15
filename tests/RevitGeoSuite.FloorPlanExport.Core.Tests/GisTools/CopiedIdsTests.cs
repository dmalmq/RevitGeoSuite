using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class CopiedIdsTests
{
    [Fact]
    public void TheOldestElementKeepsTheIdAndEachLaterCopyIsListed()
    {
        CopiedId copied = Assert.Single(CopiedIds.Find(new[]
        {
            new AuditIdValue(6535289, "6ffeebfa"),
            new AuditIdValue(6481239, "6FFEEBFA "),
            new AuditIdValue(6600000, "6ffeebfa"),
            new AuditIdValue(7, "unique"),
            new AuditIdValue(8, ""),
            new AuditIdValue(9, ""),
        }));

        Assert.Equal(6481239, copied.Original);
        Assert.Equal("6FFEEBFA", copied.Id);
        Assert.Equal(new long[] { 6535289, 6600000 }, copied.Copies);
        Assert.Equal("Element 6481239 keeps id 6FFEEBFA; elements copied from it got a new id: 6535289, 6600000.", CopiedIds.ReissuedLine(copied));
    }

    [Fact]
    public void OneElementReadTwiceIsNoCopy()
    {
        Assert.Empty(CopiedIds.Find(new[] { new AuditIdValue(5, "a"), new AuditIdValue(5, "a") }));
        Assert.Equal("Element 1 keeps id a; element copied from it got a new id: 2.", CopiedIds.ReissuedLine(CopiedIds.Find(new[] { new AuditIdValue(2, "a"), new AuditIdValue(1, "a") }).Single()));
    }
}
