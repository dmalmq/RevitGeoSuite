"""Regenerates placement-tokyo-station.json with PROJ (pyproj), an oracle independent of
RevitGeoSuite's ProjNet and Kanizu's own code.

    python -m venv venv && venv/bin/pip install pyproj
    venv/bin/python generate_placement.py > placement-tokyo-station.json
"""
import json
import math

import pyproj

CRS = 6677  # JGD2011 / Japan Plane Rectangular CS IX
ANCHOR_LAT, ANCHOR_LON = 35.6812, 139.7671  # Tokyo Station
ORTHOMETRIC = 3.3
GEOID_OFFSET = 36.7  # a typed offset, so the fixture does not depend on a geoid model
OFFSETS = [
    (0.0, 0.0, 0.0),
    (300.0, 0.0, 0.0),
    (0.0, 300.0, 0.0),
    (-300.0, -300.0, 0.0),
    (-250.0, 180.0, 12.5),
    (150.0, -220.0, -15.0),
    (300.0, 300.0, 40.0),
    (12.345, -6.789, 3.21),
]

to_grid = pyproj.Transformer.from_crs(4326, CRS, always_xy=True)
to_geo = pyproj.Transformer.from_crs(CRS, 4326, always_xy=True)
to_ecef = pyproj.Transformer.from_crs(4979, 4978, always_xy=True)

e0, n0 = to_grid.transform(ANCHOR_LON, ANCHOR_LAT)
e0, n0 = round(e0, 3), round(n0, 3)
lon0, lat0 = to_geo.transform(e0, n0)
h0 = ORTHOMETRIC + GEOID_OFFSET
x0, y0, z0 = to_ecef.transform(lon0, lat0, h0)
phi, lam = math.radians(lat0), math.radians(lon0)


def enu(x, y, z):
    dx, dy, dz = x - x0, y - y0, z - z0
    east = -math.sin(lam) * dx + math.cos(lam) * dy
    north = (-math.sin(phi) * math.cos(lam) * dx - math.sin(phi) * math.sin(lam) * dy
             + math.cos(phi) * dz)
    up = (math.cos(phi) * math.cos(lam) * dx + math.cos(phi) * math.sin(lam) * dy
          + math.sin(phi) * dz)
    return east, north, up


vertices = []
for dx, dy, dz in OFFSETS:
    lon, lat = to_geo.transform(e0 + dx, n0 + dy)
    h = h0 + dz
    x, y, z = to_ecef.transform(lon, lat, h)
    east, north, up = enu(x, y, z)
    vertices.append({
        "gridOffset": [dx, dy, dz],
        "latitude": round(lat, 10),
        "longitude": round(lon, 10),
        "ellipsoidalHeight": round(h, 4),
        "ecef": [round(x, 4), round(y, 4), round(z, 4)],
        "enu": [round(east, 4), round(north, 4), round(up, 4)],
    })

print(json.dumps({
    "description": "Exact placement of plane-grid vertices into the anchor's east-north-up frame. "
                   "Shared by RevitGeoSuite and Kanizu; both must match within 1 cm.",
    "oracle": f"pyproj {pyproj.__version__}, PROJ {pyproj.proj_version_str}",
    "projectEpsg": CRS,
    "anchor": {
        "easting": e0,
        "northing": n0,
        "latitude": round(lat0, 10),
        "longitude": round(lon0, 10),
        "orthometricHeight": ORTHOMETRIC,
        "geoidOffset": GEOID_OFFSET,
        "ellipsoidalHeight": h0,
    },
    "vertices": vertices,
}, indent=2))
