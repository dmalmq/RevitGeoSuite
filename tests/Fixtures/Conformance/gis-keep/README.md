# Keep file fixture

`8d7f3c1e-0000.gis-keep.json` is byte for byte what `GisKeep.Merge` writes for its two entries (see `docs/kanizu-link-contract.md`, "gis-keep"). It goes with `../gis-drift/8d7f3c1e-0000.gis-drift.json`:

- The `u-2` `category` entry answers that file's one conflict (Revit `retail`, GDB `walkway`). A Kanizu apply lists it and writes the export's value, because the GDB still holds `walkway`.
- The `lvl` `ordinal` entry was kept when the GDB held `3`. The drift file's row now has `2`, so Kanizu shows the entry as stale and drops it after an apply.

Kanizu keeps a copy at `crates/kanizu-core/tests/fixtures/gis-keep/8d7f3c1e-0000.gis-keep.json`, and both repositories' tests pin its SHA-256 (`b99fca80…`). Change both files together. The folder is `-text` in `.gitattributes`, so a Windows checkout keeps the bytes.

Read by `GisKeepTests.TheConformanceFileIsWhatMergeWritesAndAnswersTheDriftFixturesConflict`.
