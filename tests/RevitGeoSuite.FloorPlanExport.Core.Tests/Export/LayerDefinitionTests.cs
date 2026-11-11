using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Export;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Export;

public sealed class LayerDefinitionTests
{
    [Fact]
    public void EveryLayer_HasARevitKeyColumn()
    {
        ExportLayer[] layers =
        {
            LayerDefinition.CreateUnitLayer(),
            LayerDefinition.CreateDetailLayer(),
            LayerDefinition.CreateOpeningLayer(),
            LayerDefinition.CreateFixtureLayer(),
            LayerDefinition.CreateLevelLayer(),
        };

        foreach (ExportLayer layer in layers)
        {
            AttributeDefinition key = Assert.Single(layer.Attributes, attribute => attribute.Name == RevitKey.AttributeName);
            Assert.Equal(ExportAttributeType.Text, key.Type);
        }
    }

    [Fact]
    public void DetailLayer_WritesCategoryAndParentKey()
    {
        ExportLayer detail = LayerDefinition.CreateDetailLayer();
        detail.AddFeature(
            new ExportLineString(
                new LineString2D(new[] { new Point2D(0, 0), new Point2D(2, 0) }),
                new Dictionary<string, object?>
                {
                    ["id"] = "step-1",
                    [RevitKey.AttributeName] = "model|run-uid|0",
                    [DetailCategory.AttributeName] = DetailCategory.Steps,
                    [RevitKey.ParentAttributeName] = "model|stair-uid|0",
                    ["level_id"] = "level-1",
                    ["element_id"] = 42L,
                    ["source_label"] = "stair-step",
                }));

        string path = Path.Combine(Path.GetTempPath(), $"detail-{Guid.NewGuid():N}.gpkg");
        try
        {
            new GpkgWriter().Write(path, srsId: 6677, layers: new[] { detail });

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT revit_key, category, parent_key FROM detail;";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("model|run-uid|0", reader.GetString(0));
            Assert.Equal("steps", reader.GetString(1));
            Assert.Equal("model|stair-uid|0", reader.GetString(2));
            Assert.False(reader.Read());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
