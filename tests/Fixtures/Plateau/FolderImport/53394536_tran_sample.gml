<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:tran="http://www.opengis.net/citygml/transportation/2.0"
                gml:id="folder-tran-sample"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <tran:Road gml:id="road-folder-001">
      <gml:name>Folder Road A</gml:name>
      <tran:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="road-poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">120 120 0 180 120 0 180 135 0 120 135 0 120 120 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod1MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>
