# Kanizu network file fixture

`3f2a9c1e-0000-4000-8000-000000000001.gis-network.json` is the sample the network thread and the RevitGeoSuite port thread agreed for `kanizu.gis-network` version 1 (build list C8, 2026-11-13). The format is in docs/kanizu-link-contract.md. It has one link of each kind (step-free, one-way, not step-free), one connector of each kind (a moving walkway left untied), a finding drawn on a floor and one that goes to the audit only.

Kanizu's writer, `revit::network_file::write`, is not built yet. When it is, regenerate this file with it so the fixture shows exactly what Kanizu writes, the same way `gis-drift` is handled. Read by `GisNetworkTests`.
