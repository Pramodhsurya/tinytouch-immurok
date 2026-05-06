---
name: using-imk
description: Use whenever the agent is about to run a command that needs sudo, ssh signing (push / pull / remote shell), or accesses secrets via `imk://...` URIs in a project that uses immurok — the macOS BLE fingerprint companion. Wrapping with `imk run --agent` surfaces the command to the user via an on-screen overlay, lets one fingerprint touch authorize the entire subprocess (incl. sudo + SSH), and rejection cleanly kills the command. Triggers — recognize from any of: `imk` in `$PATH`, a `~/.immurok/` directory, `imk://` URIs in code or `.env` files, or project docs mentioning immurok / fingerprint device.
---

# Using imk

immurok is a macOS BLE fingerprint device. The `imk` CLI lets the agent ask
the user "you OK with me running this?" via an on-screen overlay backed by a
real fingerprint touch. One touch covers the whole wrapped subprocess —
sudo, SSH-key signing, secret reads — so the user doesn't get spammed with
prompts and the agent doesn't get stuck inside scripts that hit auth gates.

## The core rule

If the agent is about to run a command that:

- requires `sudo`, OR
- triggers ssh-agent signing (`git push`, `git pull` over SSH, `ssh host '...'`,
  `scp`, `rsync` over SSH), OR
- reads a secret value (`imk get imk://...`, `--env-file` resolution)

then **wrap with `imk run --agent`**:

```bash
imk run --agent -- <command>
```

The user sees a HUD overlay at the top of their screen showing the command,
touches their device, and the wrapped subprocess runs. Closing the overlay
(or 30 s timeout) kills the subprocess via SIGTERM — the agent gets a non-
zero exit code (77 / `EX_NOPERM`) and should treat it as user denial.

## Three patterns

### A. Privileged / SSH-signed command

```bash
imk run --agent -- sudo apt install foo
imk run --agent -- git push origin main
imk run --agent -- ssh prod 'systemctl restart api'
imk run --agent -- rsync -av ~/work/ user@server:/srv/
```

After fingerprint, sudo's PAM module finds an active 5 min pre-auth and
skips its own prompt. SSH signing lights up via the same fingerprint
because the device's AUTH cooldown also satisfies KEY_SIGN / OTP_GET / API
secret reads (per FW 1.2.27).

### B. Inject secrets into env

`.env`:
```
OPENAI_KEY=imk://api/openai
GITHUB_TOKEN=imk://api/github
```

Run:
```bash
imk run --agent --env-file .env -- python script.py
```

`script.py` sees real values in `os.environ`. Secrets never land on disk
and never appear in the command string shown in the overlay.

### C. Read one secret directly

```bash
# Whole command is the secret read — overlay UI shifts to "secret access"
# theme (key icon, amber accent) so the user recognises it's a key read,
# not a generic command run.
imk run --agent -- imk get imk://otp/aws-mfa

# Inline use
imk run --agent -- bash -c '
  curl -H "Authorization: Bearer $(imk get imk://api/anthropic)" \
       https://api.anthropic.com/v1/messages
'
```

Note: `imk get imk://CAT/NAME` resolves the URI to the secret string on
stdout. CAT is one of `ssh` / `otp` / `api`. NAME matches what `imk list
CAT` shows — exact byte equality required (whitespace and zero-width
characters absorbed automatically by the App's match logic since 332).

## When NOT to wrap

- **User-initiated commands**: if the user typed something themselves and
  the agent is just helping them, don't wrap. Wrap only commands the agent
  is about to execute on their behalf.
- **Non-interactive contexts**: CI runners, cron jobs, headless scripts,
  ssh sessions without an attached macOS App — there's no human to
  fingerprint. The wrap will time out at 30 s and exit 77.
- **Read-only commands**: `ls`, `cat README`, `git log`, `git status`,
  `npm view ...` etc. don't need wrapping.
- **Inside an active wrap**: nesting (`imk run --agent -- ... imk run
  --agent -- ...`) returns `BUSY` because only one auth runs at a time.
  Inside the outer wrap, just call the inner command directly — the
  outer's pre-auth covers it.

## Discovery checklist

Before deciding to wrap, verify imk is actually in use:

```bash
which imk                     # binary exists
ls ~/.immurok/                # config / sockets present
ls /Applications/immurok.app  # App installed
test -S ~/.immurok/pam.sock   # App is running
```

If none of these are true, imk isn't set up — fall back to the standard
behaviour (let sudo prompt for password, etc.).

## Troubleshooting

| Symptom | Cause | What the agent should do |
|---|---|---|
| `imk: agent approval failed: ... immurok app not running` | App quit / not launched | Tell the user to launch `/Applications/immurok.app`; do not retry until it's up. |
| `imk: agent command rejected by user` (exit 77) | User clicked the close button or 30 s timeout | Treat as a hard "no" — abort the workflow, don't retry. |
| `ERROR:NOT_FOUND:foo \| available: ['foo ', 'bar', ...]` | Name typed differs from stored | Match against the dumped available list (whitespace-trimmed match is automatic; case isn't). |
| `Error: BUSY` | Another auth already in flight | Wait ~5 s and retry once. If persists, an earlier `imk` is still alive — find and kill it. |
| sudo prompts password after wrap | Pre-auth window (5 min) expired or `sudo -k` was called inside the wrapped script | Re-wrap; or restructure to avoid mid-script `sudo -k`. |
| Overlay shows up but typing into terminal echoes the password / does nothing | Terminal lost focus when overlay appeared | Make sure App version >= 332 (focus-restore fix); else click terminal back. |

## Security gotchas

The wrapped command string appears verbatim in the overlay HUD (truncated
to ~80 chars). **Don't put literal secrets in the command** — use
`--env-file` or `$(imk get ...)` inside a `bash -c` body.

```
✗ imk run --agent -- curl -H "Authorization: Bearer sk-real-secret-here" ...
✓ imk run --agent -- bash -c 'curl -H "Authorization: Bearer $(imk get imk://api/foo)" ...'
```

The overlay highlights any `imk://` URI it sees in the command (bold orange)
so users can spot a secret access regardless of the wrapper level.

## Authoring agent-friendly project setups

Project owners can make life easier for agents by:

- adding `imk://` references to `.env.example` so the agent can copy them
  literally;
- documenting in `AGENTS.md` / `CLAUDE.md` that "this project uses imk;
  wrap subprocesses that need sudo or SSH";
- providing a `Makefile` target like `make agent-deploy` that internally
  uses `imk run --agent -- ...`, so the agent just calls `make agent-deploy`
  without needing to know about the wrap.
