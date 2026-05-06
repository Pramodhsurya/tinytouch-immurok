# AGENTS.md (this repo)

This repository **is** an agent skill. There's no code to build — just
markdown explaining how to authorize commands via the `imk` CLI in
projects that use immurok.

If you're an AI agent reading this file, you're probably looking for the
guide on **how to use imk in another project**. That lives in:

→ [`skills/using-imk/SKILL.md`](skills/using-imk/SKILL.md)

Read it as if it were attached to the consuming project, then apply its
rules whenever you're about to run `sudo`, SSH push/pull, or any command
that reads `imk://...` URIs.

## Editing this skill

The skill is a single markdown file with YAML frontmatter. To improve it:

1. Edit `skills/using-imk/SKILL.md`.
2. Run a quick sanity test in any agent that consumes the skill — ask it
   to perform a wrap-needed task in an immurok-using project.
3. Commit + open a PR.

Do not split the skill into multiple files unless it grows past ~400 lines.
Keeping it in one file keeps the discoverability story simple ("read this
one file and you know how to use imk").
