# Mac app compatibility with ESP32-S3 + ZW111

The release goal is **every feature in the imported Mac app working with our ESP32 firmware**, not simply a connected device. Mac work and its full acceptance gate come first; Windows follows, Linux stays on hold, then PCB/hardware work. The authoritative per-feature completion/status list remains [FEATURE_CHECKLIST.md](FEATURE_CHECKLIST.md), MAC-01 through MAC-30. This document adds implementation dependencies without duplicating those checklist entries.

## Connection adapter — 2026-10-07

The native device page now uses `TinyTouchConnection` and `TinyTouchKit`, rather than interpreting the ESP32 as a CH592F.

- USB discovery checks USB VID/PID (303a:4001) and serial format through IOKit. If a bonded device is configured, only that serial is selected. Multiple unidentified candidates are rejected rather than picking the first port.
- USB status uses the existing signed tinyTouch CLI, bundled with the preview. Its foreground lease pauses/resumes the USB helper. Requests have a deadline and bounded output; invalid protocol/status and disconnects fail closed. USB attach/detach is checked every five seconds; status reads otherwise happen on explicit refresh to avoid repeatedly interrupting authentication.
- Bluetooth retrieves the remembered CoreBluetooth UUID from the existing nonsecret `bluetooth.json`, reads the encrypted NUS identity characteristic, and checks the complete serial. Discovery/connect/read has a deadline, disconnects clear identity state, and retries back off. Name matching alone never establishes identity.
- Connection status does **not** mark the immurok challenge-response verifier authenticated. The current helper remains the sole password-output owner. The connection adapter does not subscribe to EV/EV2 or write PW/PW2 and never reads saved passwords or pairing keys. The separate October 8 explicit AUTH2 test reads only the existing pairing key after user action, as documented below.
- Physical ZW111 template counts are displayed directly, not as the original six-slot bitmap. Unknown battery measurement is displayed as unavailable. USB metadata is cleared on disconnect; BLE-only connectivity does not imply fresh USB metadata.
- The ESP32 preview keeps unported feature controls and PAM/SSH/CLI service deployment inactive, and opens an ESP32-specific startup wizard instead of the CH592 commissioning wizard. Native app update installation is disabled in this preview; the release endpoint points to our combined repository rather than replacing this app with upstream binaries.

This is a **connection/status adapter**, not completed management/authentication feature parity. The existing helper provides the already tested USB/BLE keyboard unlock path. The native app has confirmed the bonded Bluetooth identity with macOS Bluetooth authorization granted. Sleep/wake and physical detach/reconnect still need app/device acceptance evidence before MAC-02/03 can be checked off.

## Implementation sequence for full feature parity

| Order | Existing checklist IDs | Compatibility work | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | MAC-01–05 | Native connection, explicit transport/device selection, negotiated capabilities, authenticated sessions and protected Keychain migration | Both transports; wrong peer/protocol; unplug, reconnect, sleep/wake; no secret disclosure or false battery reading |
| 2 | MAC-06–09 | ZW111 logical enrollment/progress/cancel/delete/rename; one output owner; lock/unlock; authorized host-slot management | Real enrollment/cancel and protected delete, lock/unlock over USB and battery/BLE, second host and rejected removal |
| 3 | MAC-10–13 | Fresh, request-scoped biometric authorization, local PAM verifier and secure target routing | Real sudo/system auth; negative fingerprint, replay/IPC spoof rejection; focused-target checks; password fallback/uninstall |
| 4 | MAC-16–19 | ESP32 key storage/crypto commands and SSH/OTP/API management/import/export | Device-held signing, OpenSSH/git, invalid keys/imports, secret-export rules and storage capacity |
| 5 | MAC-14–15, MAC-20–24 | Password-manager targets, custom automation, Quick Fill, CLI/URI, settings and localization | Each supported target tested; cancel/timeouts; command approval before execution; restart and language fallback |
| 6 | MAC-25–29 | Fork diagnostics/releases; ESP32 signed OTA; standalone app packaging/signing, CLI deployment and complete Mac release gate | Wrong-board/signature/rollback rejection; interrupted OTA; clean install/upgrade/uninstall; all Mac checklist items verified |
| Baseline | MAC-30 | Imported source build/regression suite | Already completed; baseline tests alone do not establish device feature compatibility |

The current ESP32 NUS service exposes encrypted identity and password delivery, **not** the immurok binary management command service. A firmware/API extension is therefore needed for BLE management, vault/PAM/signing and native OTA. USB protocol-6 already has management commands; each needs a native capability-aware adapter and its own acceptance checks. Preserve the current templates, host bonds and helper while adding those features. Keep tinyTouch attribution and licenses in source and packaged artifacts.

## Reproduce

From `app-macos`:

```sh
swift test
swift run tinyTouchProbe /path/to/signed/tinytouch-directory/tinytouch
python3 packaging/build-esp32-preview.py /path/to/signed/tinytouch-directory
```

The package script takes the complete signed CLI directory, checks its signature, copies its runtime without changing its signed bytes, includes upstream credits/licenses, and creates `.build/tinyTouch Native Preview.app`. It does not overwrite `~/Applications/tinyTouch.app` or deploy PAM components. The preview is ad hoc signed for local development, not a notarized distribution.

## Recorded evidence

- `swift test`: 115 tests, zero failures, including wrong-device selection, malformed/oversized status, exact Bluetooth identity matching, inventory validation, command bounds, timeout/cancellation, last-finger protection and device-scoped names.
- Native `tinyTouchProbe` against the attached device: TT-90706911C494, protocol 6, firmware 0.1.35, build esp32-baseline-1, sensor ready, one physical template, one host.
- Native app runtime: Bluetooth authorized, powered on, bonded encrypted identity read confirmed. USB status also confirmed in the running native app. Sleep/wake and physical reconnect acceptance remain pending.
- Native management probe: slot 10 has one existing view; nine empty blocks; 14 settings decoded. No fingerprint was changed during these checks.
- Enrollment/delete/settings positive acceptance and full feature compatibility remain pending until separately recorded.

## Native USB management — 2026-10-07

The device page now supports ten-slot ZW111 inventory, four-view enrollment with streamed prompts, cancellation, confirmed replacement/deletion, per-device local fingerprint names and validated HID/LED preferences. The underlying signed CLI retains its firmware fingerprint authorization and lease handling. Successful mutations are followed by fresh inventory/settings/status reads before the app reports verification. Native deletion/replacement reads the current inventory first and refuses to remove the last usable finger; cleanup-pending slots do not count as backup authentication fingers. The last-finger guard here is a native client policy, not a claim that the firmware-wide FW-06 port is complete.

Management and status operations are serialized within the native app. Leaving the device page cancels an active management operation. Timeout/cancellation bounds apply to the child process and its output. Cancellation closes CDC through the CLI; the existing firmware aborts enrollment on CDC DTR loss. Other stored slots are preserved. Partial captures or cleanup-pending slots remain visible instead of being labeled complete.

The existing slot 10 is preserved. The first physical enrollment acceptance should use an empty slot (for example 1), authorize with the existing enrolled finger, then capture all four views of the new finger. Check final inventory, cancellation, native rename persistence, and protected deletion of the new slot while keeping slot 10. Settings acceptance should change a benign value, verify the readback, then restore it. Positive/negative authorization and physical disconnect tests require the user's sensor interaction.

BLE management, first/second-host management, request-bound native authentication, PAM, vault/SSH/OTP/API, Quick Fill, automation, signed OTA, localization of new controls and production packaging remain open. The Mac release gate cannot be checked off until these adapters and their real acceptance tests are complete.

The native About view now includes the actual bundle version, connection-only diagnostic copy, and links to this fork, its checklist and credits. App update installation stays disabled until production signing/packaging is ready. No valid macOS code-signing identity is currently installed; the local preview remains ad hoc signed, so macOS may request Bluetooth permission again after a rebuild.

### Startup wizard and original interface (October 7, 2026)

The ESP32 bundle now presents a four-step startup wizard on first launch: USB sensor/identity checks, live fingerprint inventory with optional empty-slot enrollment, a user-confirmed keyboard-output test, then a readiness summary. Completion is recorded for the USB-verified serial only. A newly attached serial starts commissioning again, and the menu can reopen the wizard. Existing pairing and fingerprints are retained; cancellation uses the existing managed CLI operation. USB sensor readiness, HID mode, a configured host and a usable finger are required. A partially enrolled existing finger may be used; cleanup-pending slots do not qualify. Connection alone does not prove authentication. First-time helper host pairing is a stated prerequisite; native host pairing remains pending.

The original Keys, Features, Automation and About layouts are restored. Keys and privileged Features controls are disabled while their ESP32 adapters are missing. Local Automation editing/import/export remains available, with execution explicitly pending. About preserves the original layout/language/log controls, uses ESP32 diagnostics and the combined repository link, and disables upstream update installation/uninstall. The Device page retains the functioning ten-slot ESP32 management controls.

Validation: 118 Swift tests pass, including new readiness tests for missing sensor/host/finger, PIV mode, wrong serial, unconfirmed output and usable partial enrollment. Physical enrollment and the wizard's user-confirmed output step still require a person at the sensor. MAC-01 stays In progress until end-to-end wizard acceptance and production packaging/permissions are checked.

### Registered computer management (October 7, 2026)

The Device page now shows the firmware's eight-slot HID host inventory rather than assuming the original CH592 dual-host layout. The signed CLI returns public host IDs only; the native app does not retrieve pairing keys or passwords. Host IDs are strictly bounded lowercase 16-character hex values; duplicates, inconsistent counts and unexpected trailing output are rejected. Removal requires a confirmation describing possible current-Mac unbinding, a fresh host inventory with another host remaining, the CLI's enrolled-finger approval, and verified absence afterward. Count is cross-checked against fresh USB status. The guard applies to native-app operations; it does not change the firmware's independent last-host policy. The existing CLI may delete this Mac's pairing key when its own host is removed. Names/current-Mac identification and native host registration remain pending; setup on a second Mac uses its own tinyTouch HID setup.

Live read-only probe passed: sensor ready, five physical templates; logical slot 1 has four views, slot 10 retains one; eight free finger blocks; 14 settings; one registered host out of eight. The local wizard completion record is present for this serial. No host removal was performed. 123 tests pass, including host inventory and last-host/identifier guards. MAC-08 remains In progress pending real second-host registration/removal and local-unbinding recovery acceptance.

### Native Mac settings and standalone installation (October 7, 2026)

Features now includes usable native startup/status controls above a collapsed copy of the inactive original privileged features. Launch-at-login uses SMAppService for the native bundle; status/approval failures are shown, registration is an explicit user toggle, and controls are disabled when running from the build directory. Permission checks do not request Accessibility or modify TCC. Bluetooth permission settings can be opened for the user. Read-only launchctl checks report running, loaded-but-stopped, or unavailable helper states and do not claim credential validity or output-test success. The native app does not start/stop/replace password helpers.

The new installer copies the verified local bundle to `~/Applications/tinyTouch Native.app`, verifies the signed bundled CLI after copying, rejects an unrelated/symlink target, and retains any previous native bundle during an update. It refuses updates while the installed native executable is running. Existing tinyTouch helper and upstream comparison app remain separate. The native app keeps its current preview bundle identity to preserve existing local preferences and uses ad hoc signing; this is not a notarized release.

Validation: 128 Swift tests pass, including actual launchctl-style formatting, loaded/stopped states, unrelated labels and malformed/oversized output. Read-only live probes report both existing USB and Bluetooth password helpers Running. Login registration and post-login launch require separate acceptance; they are not marked completed. Current setup instructions are in [MAC_NATIVE_SETUP.md](MAC_NATIVE_SETUP.md).

Installed-app evidence: the process runs from `~/Applications/tinyTouch Native.app/Contents/MacOS/tinyTouch-native`, not `.build`. Native and bundled CLI signatures verified, and the installed CLI executable bytes match the original signed backend. Runtime confirmed USB protocol 6, two occupied logical finger slots, 14 settings, one host, and bonded Bluetooth identity. Initial window visibility was observed. Automated UI interaction could not complete because the native computer-use pipe closed; Features click-through, login/restart and update/restore remain manual acceptance work.

### Pause point for the next implementation session (October 7, 2026)

This is a historical pause point; implementation resumed October 8 with the AUTH2 stage below.

The current branch is clean and pushed at commit `d88af34`. The standalone app is installed at `~/Applications/tinyTouch Native.app`; the original `tinyTouch.app` helper remains separate and untouched. `swift test` passes 128 tests. The live device remains `TT-90706911C494`; no fingerprints or registered hosts were removed during this stage. The checklist has 12 items In progress, 113 Yet to be done, and 12 Completed.

In progress: DOC-04 setup/troubleshooting docs; MAC-01 wizard; MAC-02 Bluetooth reconnect; MAC-03 USB/BLE transport parity; MAC-05 status/readiness; MAC-06 physical enrollment/cancel/delete acceptance; MAC-08 second-host registration/removal; MAC-15 automation runtime; MAC-23 native settings/login/restart acceptance; MAC-25 diagnostics/update checks; MAC-27 packaging/update/recovery acceptance; MAC-28 standalone delivery acceptance. The next engineering dependency is FW-11–FW-15 authenticated command sessions and freshness, followed by native privileged adapters. MAC-04, MAC-07 and MAC-09–MAC-14/MAC-16–MAC-22/MAC-24/MAC-26/MAC-29 remain Yet to be done until that firmware contract exists. Windows, Linux, hardware/PCB, agent and website work remains in its ordered later phases; Linux remains on hold.

### Request-bound native USB proof (October 8, 2026)

The new normal firmware 0.1.36 advertises `auth_proof=1`. AUTH2 uses the existing registered host key to authenticate a device challenge, verify the host before prompting, and return a fingerprint result bound to both random nonces, serial, host ID and requested context. Requests expire and are consumed once. No legacy configuration window or cached native verification flag is granted. Capability negotiation treats older firmware as unsupported. Recovery/bypass builds cannot produce a proof. Contract and trust limits: [AUTH_PROOF_PROTOCOL.md](AUTH_PROOF_PROTOCOL.md).

Features now has **Fingerprint authentication test**, with explicit start/cancel and user-handled Keychain access to the existing pairing key. Saved passwords are not read. It reserves USB using the existing helper lease and closes CDC before resuming the helper; management/status requests remain serialized. Late progress messages cannot overwrite a completed test result. PAM, SSH, vault and automation execution remain inactive until their own request verifiers/adapters exist.

Validation: 140 Swift tests pass; portable C vectors and negative tests pass normally and under UndefinedBehaviorSanitizer. The RF entropy guard follows ESP-IDF's documented Bluetooth-enabled requirement. The image checksum/hash validates and fits the existing OTA slot. Live helper acknowledgement/release passed, leaving both helpers Running and no lease records. Fresh pre-update inventory confirms five templates (slot 1: four views; slot 10: one), one host and 14 settings. The new preview was installed with the previous native app retained. Firmware activation and native Keychain/sensor proof require physical user interaction and have not yet passed. FW-14/FW-15/MAC-04 and DOC-01/DOC-02 remain In progress; ECDH, BLE proofs and full Mac acceptance remain pending.

Firmware upload subsequently passed the live enrolled-finger gate, completed transfer/verification and returned update-ready. Immediate pre-restart inventory readback retained all templates and the registered host on the old running image. Native computer-use inspection still fails with a closed pipe, so the user must operate the native test controls and any Keychain prompt; no UI acceptance is claimed from the app merely running.

After user RESET, fresh USB runtime confirms 0.1.36 / esp32-auth-proof-1 with `auth_proof=1`, OTA idle, sensor ready, five templates and one host. The first native post-reset probe passed status but a follow-up management read failed; complete inventory/settings verification will be retried after the user-operated native proof test. Activation has passed; that proof test remains pending.

**Live proof acceptance withdrawn:** the app's green result was cryptographically valid, but the user subsequently confirmed it also occurred without touching the sensor, including the original apparent positive test. The foreground UART matcher lacked a new physical-touch requirement. No fresh biometric authority or feature completion is credited for these results. Cancellation did return no success. Earlier serial failures also remain distinct from this presence bug; stage-specific I/O errors, bounded empty-read retries and explicit DTR/exclusive cleanup are now implemented.

Replacement firmware 0.1.37 requires sensor-confirmed GPIO absence followed by a new debounced touch and fresh image/match while UART ownership is held. Strict packet shapes reject incompatible stale responses, and cancellation is checked between sensor operations. The native app requires both `auth_proof` and `auth_fresh` and the fresh HMAC domain; 0.1.36 is disabled. Each test click clears an earlier success before startup guards. The replacement app is built, signature-verified and installed. 142 Mac tests and portable C presence/packet regressions pass. The 0.1.37 image builds, validates and is now activated after OTA and RESET, retaining five templates and one host. Physical positive/no-touch/wrong-finger/cancel acceptance remains pending. See AUTH_PROOF_PROTOCOL.md for the updated contract.

The inventory issue persists independently: the signed CLI `fingers` command receives the firmware's `ERR FINGER inventory_unavailable`. This is not merely an initial reconnect transient. Sensor status/count remain ready/five, and no enrollment or deletion was attempted. Full logical inventory and protected management acceptance must be repaired and rechecked. Native positive AUTH2 acceptance does not close MAC-06 or the broader Mac release gate.
