using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.UnitFloors;

public sealed class UnitFloorLinkFileTests
{
    [Fact]
    public void SavesAndLoadsTheLinkBetweenRowsAndFloors()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rgs-unitfloors-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = UnitFloorLinkFile.PathFor(Path.Combine(folder, "station.gpkg"), "SHINJUKU_LUMINE_EST_B1_unit", "B1");
            Assert.EndsWith("station.SHINJUKU_LUMINE_EST_B1_unit.B1.unit-floors.json", path);

            UnitFloorLinkFile file = new() { CreatedUtc = "2026-11-10T05:00:00Z" };
            file.Source.Table = "SHINJUKU_LUMINE_EST_B1_unit";
            file.Floors.Add(new UnitFloorLinkFloor { Key = "12", FloorUniqueId = "abc-123", TypeName = "j 店舗_床", HostUniqueId = "host-1" });
            file.Skipped.Add(new UnitFloorLinkSkipped { Key = "13", Reason = "NotPolygon" });
            file.Save(path);
            file.Floors[0].FloorUniqueId = "abc-456";
            file.Save(path);

            UnitFloorLinkFile loaded = UnitFloorLinkFile.Load(path);
            Assert.Equal(UnitFloorLinkFile.Format, loaded.FileFormat);
            Assert.Equal("abc-456", Assert.Single(loaded.Floors).FloorUniqueId);
            Assert.Equal("j 店舗_床", loaded.Floors[0].TypeName);
            Assert.Equal("13", Assert.Single(loaded.Skipped).Key);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ANewerVersionIsRefused()
    {
        string path = Path.Combine(Path.GetTempPath(), "rgs-unitfloors-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"format\":\"revitgeosuite.unit-floors\",\"version\":2}");
            Assert.Throws<InvalidDataException>(() => UnitFloorLinkFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
