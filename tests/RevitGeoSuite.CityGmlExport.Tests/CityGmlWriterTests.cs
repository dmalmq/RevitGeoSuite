using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Schema;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class CityGmlWriterTests
{
    [Fact]
    public void Build_xml_creates_citymodel_with_expected_members()
    {
        CityGmlWriter writer = new CityGmlWriter();

        string xml = writer.BuildXml(CreatePackage());
        XDocument document = XDocument.Parse(xml);

        Assert.Equal(PlateauConstants.CoreNamespace + "CityModel", document.Root!.Name);
        Assert.Equal(2, document.Root.Elements(PlateauConstants.CoreNamespace + "cityObjectMember").Count());
        Assert.NotNull(document.Descendants(PlateauConstants.BuildingNamespace + "Building").FirstOrDefault());
        Assert.NotNull(document.Descendants(PlateauConstants.TransportationNamespace + "Road").FirstOrDefault());
    }

    internal static CityGmlExportPackage CreatePackage()
    {
        return new CityGmlExportPackage
        {
            ReferenceContext = new CityGmlExportReferenceContext
            {
                Title = "Canonical Origin",
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
                AnchorLatitude = 36d,
                AnchorLongitude = 139.833333333333d,
                AnchorElevationMeters = 0d
            },
            Features = new List<CityGmlFeature>
            {
                new CityGmlFeature
                {
                    Id = "building-1",
                    Name = "Building One",
                    CategoryName = "Walls",
                    SemanticType = CityGmlSemanticType.Building,
                    Attributes = new AttributeMapper().BuildBasicAttributes("1", "Walls", "Building One").ToArray(),
                    CodeAssignment = new CityGmlCodeAssignment { Code = "402", Name = "Office Building", CodeSpace = "urn:test:building" },
                    Surfaces = new []
                    {
                        new CityGmlSurface
                        {
                            ExteriorRing = new []
                            {
                                new CityGmlCoordinate(0d, 0d, 0d),
                                new CityGmlCoordinate(1d, 0d, 0d),
                                new CityGmlCoordinate(0d, 1d, 0d),
                                new CityGmlCoordinate(0d, 0d, 0d)
                            }
                        }
                    }
                },
                new CityGmlFeature
                {
                    Id = "road-1",
                    Name = "Road One",
                    CategoryName = "Roads",
                    SemanticType = CityGmlSemanticType.Road,
                    Attributes = new AttributeMapper().BuildBasicAttributes("2", "Roads", "Road One").ToArray(),
                    Surfaces = new []
                    {
                        new CityGmlSurface
                        {
                            ExteriorRing = new []
                            {
                                new CityGmlCoordinate(10d, 0d, 0d),
                                new CityGmlCoordinate(11d, 0d, 0d),
                                new CityGmlCoordinate(10d, 1d, 0d),
                                new CityGmlCoordinate(10d, 0d, 0d)
                            }
                        }
                    }
                }
            },
            SemanticCounts = new Dictionary<CityGmlSemanticType, int>
            {
                [CityGmlSemanticType.Building] = 1,
                [CityGmlSemanticType.Road] = 1
            }
        };
    }
}
