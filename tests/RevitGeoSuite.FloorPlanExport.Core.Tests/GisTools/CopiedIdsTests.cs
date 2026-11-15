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
        Assert.Equal(
            "Element 6481239 keeps id 6FFEEBFA as the oldest; elements copied from it got a new id: 6535289, 6600000. If an earlier export took only a copy, the GDB row with id 6FFEEBFA now belongs to element 6481239.",
            CopiedIds.ReissuedLine(copied, copied.Copies));
    }

    [Fact]
    public void OneElementReadTwiceIsNoCopy()
    {
        Assert.Empty(CopiedIds.Find(new[] { new AuditIdValue(5, "a"), new AuditIdValue(5, "a") }));
        CopiedId copied = CopiedIds.Find(new[] { new AuditIdValue(2, "a"), new AuditIdValue(1, "a") }).Single();
        Assert.Equal(1, copied.Original);
        Assert.Equal(new long[] { 2 }, copied.Copies);
    }

    [Fact]
    public void AGroupedCopyWhoseIdHoldsOneValuePerGroupTypeKeepsTheSharedId()
    {
        CopiedId copied = new("a", 10, new long[] { 20, 30, 40 });
        (var reissue, var kept) = CopiedIds.Split(copied, copy => copy == 30);
        Assert.Equal(new long[] { 20, 40 }, reissue);
        Assert.Equal(new long[] { 30 }, kept);
        Assert.Equal(
            "Element 30 still shares id a with element 10: Sato has borrowed it. The GDB gets one row per element with that id.",
            CopiedIds.StillSharedLine(copied, kept, "Sato has borrowed it"));
        Assert.StartsWith("Elements 20, 40 still share id a", CopiedIds.StillSharedLine(copied, reissue, "x"));
    }
}
