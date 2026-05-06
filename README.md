# imk-skill

Drop-in **skill / agent guide** for AI coding agents working in projects
that use [immurok](https://github.com/immurok) — the macOS BLE fingerprint
device — for fingerprint-gated secret access and command authorization.

The skill teaches the agent the canonical wrap pattern, when to apply it,
and what to do when things go wrong.

## Install

The right install path depends on which agent the user is running. The
skill content is the same single markdown file
([`skills/using-imk/SKILL.md`](skills/using-imk/SKILL.md)) — only the
delivery mechanism differs.

### Claude Code (cleanest — `/plugin install`)

In a Claude Code session, run two slash commands:

```
/plugin marketplace add immurok/imk-skill
/plugin install imk-tools@imk
```

That's it. The skill registers as `imk-tools:using-imk` and lazy-loads
when its triggers fire (`imk` on `$PATH`, `~/.immurok/` exists, etc.).

To update later: `/plugin marketplace update imk` then re-install.

To pin to a specific version / branch:

```
/plugin marketplace add immurok/imk-skill#v0.1.0
```

Plugin works at user scope (default — applies in every Claude Code
session) or project scope (add the marketplace inside one project's
session and install there). Choose based on whether all your projects
use immurok.

### Cursor / Windsurf / Continue / Cline (rules file)

These agents read per-project rules files
(`.cursorrules`, `.windsurfrules`, `.continue/rules.md`, `.clinerules`).
Two options:

**A. By reference** (lighter, always up-to-date):

Append to your project's rules file:

```markdown
## imk authorization

This project uses `imk` for fingerprint-gated command authorization.
Wrap subprocesses needing sudo / SSH / secret access with
`imk run --agent -- <command>`. Full rules:
https://github.com/immurok/imk-skill/blob/main/skills/using-imk/SKILL.md
```

**B. By vendoring** (offline-friendly):

```bash
curl -sL https://raw.githubusercontent.com/immurok/imk-skill/main/skills/using-imk/SKILL.md \
  >> .cursorrules     # or .windsurfrules / .clinerules / etc.
```

Or `git submodule add https://github.com/immurok/imk-skill .imk-skill`
and reference `.imk-skill/skills/using-imk/SKILL.md` from your rules file.

### Codex / Aider / Gemini CLI (convention file)

Append to `AGENTS.md` / `GEMINI.md` at project root:

```markdown
## imk authorization

This project uses `imk` for fingerprint-gated command authorization.
Wrap subprocesses needing sudo / SSH / secret access with
`imk run --agent -- <command>`. Full rules:
https://github.com/immurok/imk-skill/blob/main/skills/using-imk/SKILL.md
```

## Verify install

In a fresh agent session inside an immurok-using project, ask:

> "在这个项目跑 `git push origin main` 应该怎么操作？"

A correctly-installed agent answers with `imk run --agent -- git push
origin main` (or equivalent), citing imk authorization. If it answers
with bare `git push`, the skill isn't loading — verify the install path.

## What's in the skill

[`skills/using-imk/SKILL.md`](skills/using-imk/SKILL.md) — single file,
YAML frontmatter + markdown:

- Trigger conditions (when the skill applies)
- The core rule (`imk run --agent -- ...`)
- Three usage patterns (privileged command / env-file injection / direct
  secret read)
- When NOT to wrap (manual user commands, CI, read-only ops, nesting)
- Discovery checklist
- Troubleshooting matrix
- Security gotchas (don't put literal secrets in the wrapped command)
- Project-side hints for repo maintainers

## Updating

Edit `skills/using-imk/SKILL.md`, commit, push. Vendored copies need
re-vendoring; reference-mode installs pick up changes automatically.

## License

BSL 1.1 — converts to Apache 2.0 on 2030-03-05.
