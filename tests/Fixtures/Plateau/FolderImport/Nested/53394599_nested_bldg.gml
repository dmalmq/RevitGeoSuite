<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                gml:id="nested-bldg-sample"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <bldg:Building gml:id="nested-bldg-001">
      <gml:name>Nested Building</gml:name>
      <bldg:lod0FootPrint>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="nested-poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">10 10 0 20 10 0 20 20 0 10 20 0 10 10 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0FootPrint>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
