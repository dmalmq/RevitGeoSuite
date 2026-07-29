<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0"
                xmlns:gml="http://www.opengis.net/gml"
                xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                gml:id="sample-detailed-building"
                srsName="urn:ogc:def:crs:EPSG::6677">
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-detailed-001">
      <gml:name>Detailed Building</gml:name>
      <bldg:lod0FootPrint>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="lod0-poly-001">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension="3">100 150 0 140 150 0 140 190 0 100 190 0 100 150 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0FootPrint>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id="ground-poly-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 150 42 140 150 42 140 190 42 100 190 42 100 150 42</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
      <bldg:boundedBy>
        <bldg:WallSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id="wall-south-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 150 42 140 150 42 140 150 54 100 150 54 100 150 42</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id="wall-north-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 190 42 140 190 42 140 190 54 100 190 54 100 190 42</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id="wall-west-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 150 42 100 190 42 100 190 54 100 150 54 100 150 42</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id="wall-east-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">140 150 42 140 190 42 140 190 54 140 150 54 140 150 42</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:WallSurface>
      </bldg:boundedBy>
      <bldg:boundedBy>
        <bldg:RoofSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id="roof-south-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 150 54 140 150 54 140 170 60 100 170 60 100 150 54</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id="roof-north-001">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension="3">100 170 60 140 170 60 140 190 54 100 190 54 100 170 60</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:RoofSurface>
      </bldg:boundedBy>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
