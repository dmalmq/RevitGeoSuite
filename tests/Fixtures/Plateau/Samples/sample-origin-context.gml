<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                gml:id="sample-origin-context"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <gml:boundedBy>
    <gml:Envelope srsName="urn:ogc:def:crs:EPSG::6677">
      <gml:lowerCorner>80 120 0</gml:lowerCorner>
      <gml:upperCorner>220 260 0</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-001">
      <gml:name>Sample Building A</gml:name>
      <bldg:lod0FootPrint>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">100 150 0 140 150 0 140 190 0 100 190 0 100 150 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0FootPrint>
    </bldg:Building>
  </core:cityObjectMember>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-002">
      <gml:name>Sample Building B</gml:name>
      <bldg:lod0FootPrint>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-002">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">170 210 0 210 210 0 210 250 0 170 250 0 170 210 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0FootPrint>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
