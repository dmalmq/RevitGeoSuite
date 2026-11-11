using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisStandardNoticeStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gis-notice-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ASkippedVersionStaysSkippedUntilANewerOne()
    {
        GisStandardNoticeStore store = new(Path.Combine(_folder, "notices.json"));
        Assert.False(store.IsSkipped("Model-A", 1));

        store.Skip("Model-A", 1);

        Assert.True(new GisStandardNoticeStore(Path.Combine(_folder, "notices.json")).IsSkipped("model-a", 1));
        Assert.False(store.IsSkipped("model-a", 2));
        Assert.False(store.IsSkipped("model-b", 1));
    }

    [Fact]
    public void ADamagedFileShowsTheNoticeAgain()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "notices.json");
        File.WriteAllText(path, "not json");

        Assert.False(new GisStandardNoticeStore(path).IsSkipped("model-a", 1));
    }
}
