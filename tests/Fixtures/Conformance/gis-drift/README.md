# Kanizu drift file fixture

`8d7f3c1e-0000.gis-drift.json` was written by Kanizu's own writer, `kanizu_core::revit::gis::DriftFile::save`, at Kanizu a55c7b6f (branch claude/project-thread-29jvoc, 2026-11-12), the commit that adds `gdbCategory` and `gdbRestriction`. It is not hand-made, so it shows exactly what RevitGeoSuite's Review GDB edits has to read: integer `version`, `null` for a missing `revit`/`gdb`/`base`, and `notPlaced`. Regenerated at Kanizu C2 (2026-11-12) to add `epsg` and `shapes`: one of each shape edit (reshape, a split's two pieces, a merge with the unit it took in, a unit drawn by hand, a deletion and a move), with outlines in EPSG:6677.

To regenerate after the format changes, copy `write_drift_fixture.rs.txt` to `crates/kanizu-core/examples/write_drift_fixture.rs` in a Kanizu checkout and run:

    cargo run -p kanizu-core --example write_drift_fixture -- <empty folder>

It prints the path of the file it wrote (`<folder>/gis-drift/8d7f3c1e-0000.gis-drift.json`); copy that file here. Read by `GisDriftTests.ReadsAFileWrittenByKanizu`, `GisDriftTests.ReadsTheShapeEditsKanizuWrote` and pyRevit's `test_drift.py`.
