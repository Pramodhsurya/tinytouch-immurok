# Mac app compatibility with ESP32-S3 + ZW111

The release goal is **every feature in the imported Mac app working with our ESP32 firmware**, not simply a connected device. Mac work and its full acceptance gate come first; Windows follows, Linux stays on hold, then PCB/hardware work. The authoritative per-feature completion/status list remains [FEATURE_CHECKLIST.md](FEATURE_CHECKLIST.md), MAC-01 through MAC-30. This document adds implementation dependencies without duplicating those checklist entries.

## Connection adapter — 2026-10-07

The native device page now uses `TinyTouchConnection` and `TinyTouchKit`, rather than interpreting the ESP32 as a CH592F.

- USB discovery checks USB VID/PID (303a:4001) and serial format through IOKit. If a bonded device is configured, only that serial is selected. Multiple unidentified candidates are rejected rather than picking the first port.
- USB status uses the existing signed tinyTouch CLI, bundled with the preview. Its foreground lease pauses/resumes the USB helper. Requests have a deadline and bounded output; invalid protocol/status and disconnects fail closed. USB attach/detach is checked every five seconds; status reads otherwise happen on explicit refresh to avoid repeatedly interrupting authentication.
- Bluetooth retrieves the remembered CoreBluetooth UUID from the existing nonsecret `bluetooth.json`, reads the encrypted NUS identity characteristic, and checks the complete serial. Discovery/connect/read has a deadline, disconnects clear identity state, and retries back off. Name matching alone never establishes identity.
- Connection status does **not** mark the immurok challenge-response verifier authenticated. The current helper remains the sole password-output owner. The native adapter does not subscribe to EV/EV2 or write PW/PW2 and never reads saved passwords or pairing keys.
- Physical ZW111 template counts are displayed directly, not as the original six-slot bitmap. Unknown battery measurement is displayed as unavailable. USB metadata is cleared on disconnect; BLE-only connectivity does not imply fresh USB metadata.
- The ESP32 preview keeps unported feature controls and PAM/SSH/CLI service deployment inactive, and opens its connection page instead of the CH592 commissioning wizard. Native app update installation is disabled in this preview; the release endpoint points to our combined repository rather than replacing this app with upstream binaries.

This is a **connection/status adapter**, not completed management/authentication feature parity. The existing helper provides the already tested USB/BLE keyboard unlock path. Cold start, Bluetooth permissions, sleep/wake and physical detach/reconnect still need app/device acceptance evidence before MAC-02/03 can be checked off.

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

- `swift test`: 103 tests, zero failures, including wrong-device/ambiguous-device selection, malformed status, unsupported protocol, invalid counts oversized output and exact Bluetooth identity matching.
- Native `tinyTouchProbe` against the attached device: TT-90706911C494, protocol 6, firmware 0.1.35, build esp32-baseline-1, sensor ready, one physical template, one host.
- Bluetooth native app acceptance and full feature compatibility remain pending until separately recorded.
