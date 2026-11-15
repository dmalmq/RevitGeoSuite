# Kanizu network file fixture

`3f2a9c1e-0000-4000-8000-000000000001.gis-network.json` is a byte copy of Kanizu's own sample, `crates/kanizu-core/tests/fixtures/c8-sample.gis-network.json` at Kanizu dev ebb42b77 (#459, the commit that adds the writer `revit::network_file`). It is `kanizu.gis-network` version 1 with the additive `notChecked`. The format is in docs/kanizu-link-contract.md. It has one link of each kind (step-free, one-way, not step-free), one connector of each kind (a moving walkway left untied), a finding drawn on a floor and one that goes to the audit only.

When Kanizu's sample changes, copy it here again unchanged. Read by `GisNetworkTests`.
