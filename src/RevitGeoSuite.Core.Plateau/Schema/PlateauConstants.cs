using System;
using System.Xml.Linq;

namespace RevitGeoSuite.Core.Plateau.Schema;

public static class PlateauConstants
{
    public static readonly XNamespace CoreNamespace = "http://www.opengis.net/citygml/2.0";
    public static readonly XNamespace BuildingNamespace = "http://www.opengis.net/citygml/building/2.0";
    public static readonly XNamespace BridgeNamespace = "http://www.opengis.net/citygml/bridge/2.0";
    public static readonly XNamespace TransportationNamespace = "http://www.opengis.net/citygml/transportation/2.0";
    public static readonly XNamespace VegetationNamespace = "http://www.opengis.net/citygml/vegetation/2.0";
    public static readonly XNamespace ReliefNamespace = "http://www.opengis.net/citygml/relief/2.0";
    public static readonly XNamespace GmlNamespace = "http://www.opengis.net/gml";

    public const string DefaultImportCategoryName = "PLATEAU Context";

    public const string SampleFileExtension = ".gml";
}
