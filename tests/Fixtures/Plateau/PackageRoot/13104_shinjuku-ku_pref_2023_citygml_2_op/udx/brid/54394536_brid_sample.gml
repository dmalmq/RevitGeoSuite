<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:brid="http://www.opengis.net/citygml/bridge/2.0"
                gml:id="folder-brid-sample"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <brid:Bridge gml:id="bridge-folder-001">
      <gml:name>Folder Bridge A</gml:name>
      <brid:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="bridge-poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">220 150 12 290 150 12 290 168 12 220 168 12 220 150 12</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </brid:lod1MultiSurface>
    </brid:Bridge>
  </core:cityObjectMember>
</core:CityModel>
