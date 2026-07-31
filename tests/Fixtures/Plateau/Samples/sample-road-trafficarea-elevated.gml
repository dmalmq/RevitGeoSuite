<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:tran="http://www.opengis.net/citygml/transportation/2.0"
                gml:id="sample-road-traffic-area"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <tran:Road gml:id="road-parent-001">
      <gml:name>Road Container</gml:name>
      <tran:trafficArea>
        <tran:TrafficArea gml:id="traffic-area-low-001">
          <gml:name>Low Traffic Area</gml:name>
          <tran:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id="traffic-area-poly-low-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 200 0 130 200 0 130 210 0 100 210 0 100 200 0</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod2MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
      <tran:trafficArea>
        <tran:TrafficArea gml:id="traffic-area-high-001">
          <gml:name>Elevated Traffic Area</gml:name>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id="traffic-area-poly-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">132 202 40.82 144 202 40.82 144 210 40.82 132 210 40.82 132 202 40.82</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id="traffic-area-poly-002">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">146 202 41.15 158 202 41.15 158 210 41.15 146 210 41.15 146 202 41.15</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>
