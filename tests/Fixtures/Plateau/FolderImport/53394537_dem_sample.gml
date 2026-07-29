<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:dem="http://www.opengis.net/citygml/relief/2.0"
                gml:id="folder-dem-sample"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <dem:ReliefFeature gml:id="relief-folder-001">
      <gml:name>Folder Relief Patch</gml:name>
      <dem:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="relief-poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">260 180 4 300 180 4 300 220 6 260 220 6 260 180 4</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </dem:lod1MultiSurface>
    </dem:ReliefFeature>
  </core:cityObjectMember>
</core:CityModel>
