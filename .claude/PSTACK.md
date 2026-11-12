# Vendored pstack skills

`.claude/skills/` and `.claude/agents/` hold a copy of the pstack Claude Code plugin (version 0.9.135, by Lauren Tan and Michael Denyer), so cloud sessions, which have no plugins installed, load `poteto-mode`, `babysit` and the skills they route to.

- pstack is MIT licensed. The license and notices travel with the copy in `skills/poteto-mode/references/licenses/`.
- Project skills and agents load without the plugin prefix, so the copy names them bare: `poteto-mode`, `subagent_type: "poteto-agent"`. The other-runtime mapping files in `skills/poteto-mode/references/*-tools.md` and `grok.md` keep upstream's `pstack:` names, since they describe the plugin on those runtimes.
- Local edits to upstream: the `pstack:` prefix is stripped in the skill and agent files above, poteto-mode's note on agent namespaces says the agents are bare here, and `poteto-agent` looks for `.claude/skills/poteto-mode/SKILL.md` before the plugin cache, and `setup-pstack` skips the session hook question (no hook is vendored) and commits only `.claude/pstack-models.md`, since `CLAUDE.md` stays local-only here.
- `statechart/scripts/test/` (a 3.3 MB test corpus) is left out.
- To update, copy `skills/` and `agents/` from a newer pstack release over these folders, then redo the edits above and set the executable bit on scripts with a shebang (`git update-index --chmod=+x`).
