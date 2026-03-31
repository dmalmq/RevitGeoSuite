using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.PlateauImport;

public sealed class CityGmlParser
{
    public PlateauCityModel ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("CityGML file path cannot be empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CityGML file could not be found.", filePath);
        }

        XDocument document = XDocument.Load(filePath, LoadOptions.SetLineInfo);
        XNamespace gml = PlateauConstants.GmlNamespace;
        XNamespace bldg = PlateauConstants.BuildingNamespace;

        string srsName = document.Root?.Attribute("srsName")?.Value
            ?? document.Descendants(gml + "Envelope").Attributes("srsName").Select(attribute => attribute.Value).FirstOrDefault()
            ?? string.Empty;

        int? epsgCode = PlateauSchemaHelper.TryExtractEpsgCode(srsName, out int parsedEpsg)
            ? parsedEpsg
            : null;

        List<PlateauBuildingFeature> features = new List<PlateauBuildingFeature>();
        foreach (XElement building in document.Descendants(bldg + "Building"))
        {
            PlateauBuildingFeature? feature = TryParseBuilding(building, gml);
            if (feature is not null)
            {
                features.Add(feature);
            }
        }

        return new PlateauCityModel
        {
            SourcePath = filePath,
            SrsName = PlateauSchemaHelper.NormalizeSrsName(srsName),
            EpsgCode = epsgCode,
            FileTileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath),
            Buildings = features
        };
    }

    private static PlateauBuildingFeature? TryParseBuilding(XElement building, XNamespace gml)
    {
        XElement? polygon = building
            .Descendants()
            .FirstOrDefault(element => element.Name == gml + "Polygon");
        if (polygon is null)
        {
            return null;
        }

        XElement? ring = polygon
            .Descendants(gml + "LinearRing")
            .FirstOrDefault();
        if (ring is null)
        {
            return null;
        }

        XElement? posList = ring.Element(gml + "posList");
        if (posList is null)
        {
            return null;
        }

        PlateauCoordinate3D[] coordinates = ParseCoordinates(posList);
        if (coordinates.Length < 4)
        {
            return null;
        }

        return new PlateauBuildingFeature
        {
            Id = (string?)building.Attribute(gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            Name = building.Elements(gml + "name").Select(element => element.Value).FirstOrDefault()
                ?? building.Name.LocalName,
            ExteriorRing = coordinates
        };
    }

    private static PlateauCoordinate3D[] ParseCoordinates(XElement posListElement)
    {
        string[] rawValues = (posListElement.Value ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (rawValues.Length < 6)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        int dimension = 0;
        if (int.TryParse((string?)posListElement.Attribute("srsDimension"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDimension)
            && parsedDimension >= 2)
        {
            dimension = parsedDimension;
        }
        else if (rawValues.Length % 3 == 0)
        {
            dimension = 3;
        }
        else
        {
            dimension = 2;
        }

        List<PlateauCoordinate3D> coordinates = new List<PlateauCoordinate3D>(rawValues.Length / dimension);
        for (int index = 0; index <= rawValues.Length - dimension; index += dimension)
        {
            double x = double.Parse(rawValues[index], CultureInfo.InvariantCulture);
            double y = double.Parse(rawValues[index + 1], CultureInfo.InvariantCulture);
            double z = dimension >= 3
                ? double.Parse(rawValues[index + 2], CultureInfo.InvariantCulture)
                : 0d;
            coordinates.Add(new PlateauCoordinate3D(x, y, z));
        }

        return coordinates.ToArray();
    }
}
