# Vendored pstack skills

`.claude/skills/` and `.claude/agents/` hold a copy of the pstack Claude Code plugin (version 0.9.135, by Lauren Tan and Michael Denyer), so cloud sessions, which have no plugins installed, load `poteto-mode`, `babysit` and the skills they route to.

- pstack is MIT licensed. The license and notices travel with the copy in `skills/poteto-mode/references/licenses/`.
- Project skills load without the plugin prefix: `pstack:poteto-mode` here is `poteto-mode`, and `subagent_type: "pstack:poteto-agent"` is `poteto-agent`.
- `statechart` and `setup-pstack` are left out. Nothing poteto-mode routes to needs them, and `setup-pstack` configures a local install.
- To update, copy `skills/` and `agents/` from a newer pstack release over these folders, keeping the same two exclusions.
