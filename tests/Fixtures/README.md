These fixtures establish the shared Core sample set required by Milestones A and B.

They are intentionally small, human-readable, and safe to use from unit tests without Revit.
Values that represent external geodesy truth are pinned to authoritative references so later phases
can extend the same samples instead of re-baselining them.

Contents:
- `Crs/japan-presets.json` - all JGD2011 Japan Plane Rectangular CS presets (EPSG:6669-6687)
- `Transforms/latlon-to-projected.json` - known coordinate transform samples from GSI SurveyCalc
- `Mesh/japan-mesh-samples.json` - known tertiary mesh samples using interior points, not boundaries
- `Storage/*.json` - serialized metadata and migration samples
- `Revit/manual-test-files.md` - reserved location for manual Revit assets

Tolerance expectations:
- coordinate transforms: 0.001 m
- mesh code calculations: exact match

Axis normalization used by Core tests:
- geographic input is always `latitude`, `longitude`
- projected output is always `expectedEasting`, `expectedNorthing`
- official Japanese X/Y samples from GSI are normalized into that Easting/Northing order in fixtures
