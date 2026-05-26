using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class KibanGmlParserTests
{
    [Fact]
    public void ParseFile_reads_sidewalk_rdcompt_and_all_railcl_features()
    {
        string directory = CreateTempDirectory();
        try
        {
            string filePath = Path.Combine(directory, "FG-GML-533900-RdCompt-20260401-0001.xml");
            File.WriteAllText(filePath, BuildFixtureXml(), Encoding.UTF8);

            KibanGmlParser parser = new KibanGmlParser();

            KibanParseResult parseResult = parser.ParseFile(filePath);
            KibanParsedFeature[] features = parseResult.Lines.ToArray();

            Assert.Empty(parseResult.Polygons);
            Assert.Equal(3, features.Length);
            KibanParsedFeature sidewalk = Assert.Single(features, feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiSidewalksLayer);
            Assert.Equal("533900", sidewalk.MeshCode);
            Assert.Equal("sidewalk-id", sidewalk.SourceId);
            Assert.Equal("sidewalk-fid", sidewalk.Fid);
            Assert.Equal("歩道", sidewalk.FeatureType);
            Assert.Equal("表示", sidewalk.Visibility);
            Assert.Equal(2, sidewalk.Vertices.Count);

            KibanParsedFeature[] railways = features.Where(feature => feature.Layer == PlateauContextOutlinesDxfWriter.GsiRailwaysLayer).ToArray();
            Assert.Equal(2, railways.Length);
            Assert.Contains(railways, railway => railway.Visibility == "非表示" && railway.FeatureType == "トンネル内の鉄道");
            Assert.Contains(railways, railway => railway.Visibility == "表示" && railway.FeatureType == "普通鉄道");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_reads_green_land_use_area_from_polygon_feature_type()
    {
        string directory = CreateTempDirectory();
        try
        {
            string filePath = Path.Combine(directory, "FG-GML-533900-GreenArea-20260401-0001.xml");
            File.WriteAllText(filePath, BuildGreenLandUseFixtureXml(), Encoding.UTF8);

            KibanParseResult parseResult = new KibanGmlParser().ParseFile(filePath);

            KibanParsedPolygonFeature landUse = Assert.Single(
                parseResult.Polygons,
                feature => feature.Layer == KibanGmlParser.LandUseLayer);
            Assert.Equal("533900", landUse.MeshCode);
            Assert.Equal("green-id", landUse.SourceId);
            Assert.Equal("green-fid", landUse.Fid);
            Assert.Equal("緑地", landUse.FeatureType);
            Assert.Single(landUse.ExteriorRings);
            Assert.Equal(5, landUse.ExteriorRings[0].Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseFile_does_not_treat_green_administrative_names_as_land_use()
    {
        string directory = CreateTempDirectory();
        try
        {
            string filePath = Path.Combine(directory, "FG-GML-533900-AdmArea-20260401-0001.xml");
            File.WriteAllText(filePath, BuildGreenNameAdministrativeFixtureXml(), Encoding.UTF8);

            KibanParseResult parseResult = new KibanGmlParser().ParseFile(filePath);

            Assert.DoesNotContain(parseResult.Polygons, feature => feature.Layer == KibanGmlParser.LandUseLayer);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string BuildFixtureXml()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Dataset xmlns:gml=""http://www.opengis.net/gml/3.2"" xmlns=""http://fgd.gsi.go.jp/spec/2008/FGD_GMLSchema"" gml:id=""Dataset1"">
  <RdCompt gml:id=""sidewalk-id"">
    <fid>sidewalk-fid</fid>
    <vis>表示</vis>
    <loc><gml:Curve gml:id=""sidewalk-g""><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.001000000
35.336100000 139.002000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>歩道</type>
  </RdCompt>
  <RdCompt gml:id=""road-id"">
    <fid>road-fid</fid>
    <vis>表示</vis>
    <loc><gml:Curve gml:id=""road-g""><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.001000000
35.336100000 139.002000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>車道</type>
  </RdCompt>
  <RailCL gml:id=""rail-visible-id"">
    <fid>rail-visible-fid</fid>
    <vis>表示</vis>
    <loc><gml:Curve gml:id=""rail-visible-g""><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.003000000
35.336100000 139.004000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>普通鉄道</type>
  </RailCL>
  <RailCL gml:id=""rail-hidden-id"">
    <fid>rail-hidden-fid</fid>
    <vis>非表示</vis>
    <loc><gml:Curve gml:id=""rail-hidden-g""><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.005000000
35.336100000 139.006000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></loc>
    <type>トンネル内の鉄道</type>
  </RailCL>
</Dataset>";
    }

    private static string BuildGreenLandUseFixtureXml()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Dataset xmlns:gml=""http://www.opengis.net/gml/3.2"" xmlns=""http://fgd.gsi.go.jp/spec/2008/FGD_GMLSchema"" gml:id=""Dataset1"">
  <GreenArea gml:id=""green-id"">
    <fid>green-fid</fid>
    <area><gml:Surface gml:id=""green-g""><gml:patches><gml:PolygonPatch><gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.001000000
35.336000000 139.002000000
35.337000000 139.002000000
35.337000000 139.001000000
35.336000000 139.001000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></area>
    <type>緑地</type>
  </GreenArea>
</Dataset>";
    }

    private static string BuildGreenNameAdministrativeFixtureXml()
    {
        return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Dataset xmlns:gml=""http://www.opengis.net/gml/3.2"" xmlns=""http://fgd.gsi.go.jp/spec/2008/FGD_GMLSchema"" gml:id=""Dataset1"">
  <AdmArea gml:id=""admin-id"">
    <fid>admin-fid</fid>
    <area><gml:Surface gml:id=""admin-g""><gml:patches><gml:PolygonPatch><gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>
35.336000000 139.001000000
35.336000000 139.002000000
35.337000000 139.002000000
35.337000000 139.001000000
35.336000000 139.001000000
    </gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></area>
    <type>町村・指定都市の区</type>
    <name>横浜市緑区</name>
  </AdmArea>
</Dataset>";
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteKibanParserTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
