---
name: using-imk
description: Use whenever the agent is about to run a command that needs sudo, ssh signing (push / pull / remote shell), or accesses secrets via `imk://...` URIs in a project that uses immurok, the macOS BLE fingerprint companion. Wrapping with `imk run --agent` shows the command to the user in an on-screen overlay, lets one fingerprint touch authorize the wrapped subprocess (sudo, SSH signing, secret reads), and a rejection means the command never starts. Triggers, recognize from any of: `imk` in `$PATH`, a `~/.immurok/` directory, `imk://` URIs in code or `.env` files, or project docs mentioning immurok / fingerprint device.
---

# Using imk

immurok is a macOS BLE fingerprint device. Every privileged step on this
machine (sudo, SSH key signing, reading a stored secret) is gated by a touch
on that device. The device itself can only blink an LED; it cannot tell the
user *what* is asking. The `imk` CLI closes that gap for AI agents:

```bash
imk run --agent -- <command>
```

shows an on-screen overlay with the command text, waits for a touch, then
launches the command. One touch covers the wrapped subprocess for the next
10 seconds (details below). If the user closes the overlay or 30 s pass, the
command is never launched and `imk` exits 77 (`EX_NOPERM`).

## The core rule

Wrap any command you are about to execute on the user's behalf that:

- needs `sudo`, OR
- triggers ssh-agent signing (`git push`, `git pull` / `fetch` / `clone`
  over SSH, `ssh host ...`, `scp`, `rsync` over SSH), OR
- reads a secret (`imk get imk://...`, `--env-file` with `imk://` values).

**An unwrapped command still hits the device.** sudo, ssh and `imk get`
all go through the immurok app whether or not you wrapped them. The only
thing the wrap adds is the overlay that tells the user what is going on.
Run one of these bare and the user sees the device blink with no window
explaining why. That is the single most common mistake with this tool.
Never "fall back" to the bare command after a wrap fails or is rejected.

## What one touch actually covers

After the overlay is approved:

| Inside the wrapped command | Covered by the approval touch? |
|---|---|
| First `sudo` | Yes, if it reaches PAM within 10 s (app-side pre-auth, one-shot). |
| Second `sudo` in the same script | No. It shows another overlay and needs another touch. Put everything under one `sudo bash -c '...'`. |
| SSH signing (`git push`, `ssh`, ...) | Yes, if the signature request reaches the device within 10 s of the last touch (firmware AUTH cooldown, rolling). |
| `imk get imk://otp/...` | Same 10 s cooldown. |
| `imk get imk://api/...` and `--env-file` values | Same 10 s cooldown with app build 487 or newer. Older builds always ask for a second touch, without an overlay. |
| Anything that does not touch the device | Runs, no touch needed. |

The 10 s window starts at the touch, not at command launch. So put the
gated step at the *front* of the wrapped command and do slow work outside
the wrap:

```bash
# Good: build first, touch, install immediately
make build && imk run --agent -- sudo make install

# Bad: the touch happens, then 40 s of compiling, then sudo asks again
imk run --agent -- bash -c 'make build && sudo make install'
```

If a gate is hit after the window has expired, the device blinks again.
With app build 487+ an overlay comes up for that second gate too (sudo,
ssh signing, secret reads). On older builds only sudo gets an overlay; SSH
and `imk get` just blink.

## Three patterns

### A. Privileged / SSH-signed command

```bash
imk run --agent -- sudo apt install foo
imk run --agent -- git push origin main
imk run --agent -- ssh prod 'systemctl restart api'
imk run --agent -- rsync -av ~/work/ user@server:/srv/
```

Before asking for a touch, `imk` checks that the device actually has an
SSH key when the command is `ssh` / `scp` / `sftp` / `rsync` or a `git`
network command against an SSH remote. No key: exit 78 (`EX_CONFIG`) with
a message, no touch burned. HTTPS git remotes are not checked.

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
# Whole command is the secret read. The overlay switches to its
# "secret access" look (key icon, amber accent) so the user can tell a key
# read from a generic command run.
imk run --agent -- imk get imk://otp/aws-mfa

# Inline use
imk run --agent -- bash -c '
  curl -H "Authorization: Bearer $(imk get imk://api/anthropic)" \
       https://api.anthropic.com/v1/messages
'
```

`imk get imk://CAT/NAME` prints the secret on stdout. CAT is one of `ssh`
/ `otp` / `api` (`ssh` returns the public key and needs no touch). NAME
must match what `imk list CAT` shows; leading / trailing whitespace and
zero-width characters are ignored, case is not.

## When NOT to wrap

- **User-initiated commands**: if the user typed something themselves and
  you are only helping, do not wrap. Wrap only commands you execute on
  their behalf.
- **Non-interactive contexts**: CI runners, cron jobs, headless scripts,
  ssh sessions without an attached macOS app. Nobody can touch the device;
  the wrap times out after 30 s and exits 77.
- **Read-only commands**: `ls`, `cat README`, `git log`, `git status`,
  `npm view ...` do not touch the device and need no wrap.
- **Inside an active wrap**: do not nest `imk run --agent` inside a
  wrapped command. Nesting is not refused, it simply shows a second
  overlay and costs a second touch. Call the inner command directly.

## Discovery checklist

Before deciding to wrap, verify imk is actually in use:

```bash
which imk                     # binary exists
ls ~/.immurok/                # config / sockets present
ls /Applications/immurok.app  # app installed
test -S ~/.immurok/pam.sock   # app is running
```

If none of these hold, imk is not set up. Fall back to standard behaviour
(let sudo prompt for a password, and so on).

## Troubleshooting

| Symptom | Cause | What to do |
|---|---|---|
| **Device LED blinks but no overlay appeared** | You ran sudo / ssh / `imk get` without the wrap, or the gated step ran more than 10 s after the approval touch, or the app is older than build 487 (ssh signing and `imk get` had no overlay at all). | Tell the user which command is waiting so they can touch or Ctrl+C. Next time wrap it, and move the gated step to the front of the wrap. |
| `imk: agent command rejected by user` (exit 77) | User closed the overlay, or 30 s passed | A hard "no". Stop the workflow. Do not retry, do not run the bare command. |
| `imk: agent approval failed: immurok app not running (socket connect failed)` | App quit / not launched | Ask the user to launch `/Applications/immurok.app`. Do not retry until it is up. |
| `imk: agent approval failed: unexpected response: ERROR` | Device not connected or not verified | Ask the user to check the device connection in the app's menu. |
| `imk: agent approval failed: another auth or key operation is in flight` | Another gate is open (a previous `imk`, a keystore import / export, a QuickFill) | Wait ~5 s and retry once. If it persists, an earlier `imk` is still alive; find and kill it. |
| `imk: <ssh/git/...> needs an SSH key but none is configured` (exit 78) | Device has no SSH key | The user configures one in the app (Settings → SSH keys), or the command should not use the device. |
| `ERROR:NOT_FOUND:foo \| available: ['foo ', 'bar', ...]` | Name typed differs from stored | Pick the matching entry from the dumped list. Whitespace is forgiven, case is not. |
| sudo asks for a password right after the overlay | The wrapped command had two `sudo` calls, or the first `sudo` came more than 10 s after the touch, or the script ran `sudo -k` | Use one `sudo bash -c '...'`, put sudo first, avoid mid-script `sudo -k`. |
| Overlay is up but typing into the terminal does nothing | Terminal lost focus when the overlay appeared | App build 332 or newer restores focus on dismiss. Otherwise click the terminal. |

If you need to prove which path a touch came from, the app log
`~/Library/Logs/immurok/immurok.log` (build 487+) records every decision:
`AGENT_APPROVE: overlay shown`, `PAM AUTH sudo: overlay suppressed
(caller=manual ...)`, `gate overlay: ssh-agent shown (caller=agent)`,
`imk get api: within AUTH/KEYSTORE cooldown, no touch`.

## Security gotchas

The wrapped command string appears verbatim in the overlay (truncated to
about 80 characters). **Do not put literal secrets in the command.** Use
`--env-file` or `$(imk get ...)` inside a `bash -c` body.

```
✗ imk run --agent -- curl -H "Authorization: Bearer sk-real-secret-here" ...
✓ imk run --agent -- bash -c 'curl -H "Authorization: Bearer $(imk get imk://api/foo)" ...'
```

The overlay highlights any `imk://` URI it sees in the command (bold
orange) so the user can spot a secret access regardless of how deeply it
is wrapped.

## Authoring agent-friendly project setups

Project owners can make life easier for agents by:

- adding `imk://` references to `.env.example` so the agent can copy them
  literally;
- documenting in `AGENTS.md` / `CLAUDE.md` that "this project uses imk;
  wrap subprocesses that need sudo or SSH";
- providing a `Makefile` target like `make agent-deploy` that internally
  uses `imk run --agent -- ...`, so the agent just calls `make agent-deploy`
  without needing to know about the wrap. Keep the build outside that
  target and the `sudo` step first inside it, for the 10 s window.
