# tinyTouch ESP32-S3 + ZW111 fork

Target: retain all immurok app/feature capabilities on Seeed XIAO ESP32-S3 and ZW111, with USB data plus Bluetooth. This is the port target, not a claim of completed feature parity.

See the [folder-by-folder source analysis](docs/esp32-folder-analysis.md) for component responsibilities, reuse decisions, protocol gaps and the first implementation stages.

The [feature parity checklist](docs/FEATURE_CHECKLIST.md) is the canonical completion tracker. Follow its implementation-order column and record acceptance evidence before checking an item off.

## Repositories

All components are included in this single repository, preserving their upstream histories and license files. `components.json` records their source commits and folder paths. The ESP32-S3 port branch is `codex/esp32s3-zw111`.

- [Firmware](./firmware): ESP-IDF baseline in `ports/esp32s3` alongside original CH592F code.
- [macOS app](./app-macos)
- [Windows apps/service/Credential Provider](./app-win)
- [Linux daemon/GUI/CLI](./app-linux-rs)
- [OTA tools](./ota)
- [Hardware](./hardware)
- [Agent integration](./imk-skill)

## Status and acceptance matrix

| Capability | Current tinyTouch baseline | Required for parity |
|---|---|---|
| Battery-powered BLE screen unlock, Mac | User verified on prototype | Repeat from fork build and native app |
| USB keyboard and serial control | Implemented; original USB setup validated | Retest explicit USB mode; add app USB transport |
| Native Mac menu/settings/CLI | Upstream app forked | Device capability model, protocol integration, branding and Keychain migration |
| Fingerprint enrollment/deletion | Existing sensor driver/configuration | Translate R559S assumptions to ZW111 capacities and commands; preserve existing enrollment |
| ECDH pairing, signed events | Existing tinyTouch HMAC/session protocol differs | Implement immurok wire protocol and owner approval; prove freshness/replay rejection |
| Dual host and switch fingerprint | Not integrated | Peer slots, separate host keys, authorized switching, reconnect tests |
| sudo/PAM/polkit | Not integrated | Authenticated request binding, explicit user consent, fallback-password checks |
| SSH generation/signing | USB PIV code exists; immurok SSH commands absent | ESP32 cryptography/key storage and compatible app SSH agent |
| TOTP and API secret vault | Not implemented | Encrypted persistent storage, fingerprint gates, time/replay validation |
| Password-manager/app unlock | Not integrated | Mac target detection/injection and focused-field tests |
| Agent approval and command wrapping | Upstream integration forked | Exact-command approval, authenticated IPC, rejection/timeout verification |
| Windows login | Upstream app forked only | Device compatibility; build/test service and Credential Provider on Windows |
| Linux login/desktop integration | Upstream app forked only | Device compatibility; Linux BLE/PAM/polkit testing |
| Wireless OTA | Current prototype uses USB OTA | ESP32 partition/signature/rollback design and compatible app uploader |
| Battery level, sleep, sensor power gating | Battery operation verified; telemetry not implemented | ADC wiring, load switch if needed, measured runtime and wake/reconnect tests |
| Physical buttons/tamper functions | Additional hardware not installed | Board-specific configuration and matching firmware behavior |

## Implementation order

**User priority, 2026-10-07:** complete the standalone macOS app and all ESP32 functionality needed by it, including Mac tests and OTA/recovery, first. Windows follows Mac acceptance. Linux is explicitly on hold. PCB/enclosure and additional hardware follow Windows. All sources and platform app folders remain in this combined repository.

1. Independently build the imported ESP-IDF baseline; retain recoverable original firmware and private local backups. No existing user enrollment or keys in public Git.
2. Define shared capabilities and the ESP32 protocol implementation. Retain USB operation; do not mistake UUID changes for protocol compatibility. Establish command/session freshness and authorization tests before exposing key/secret operations.
3. Integrate native Mac app with ESP32 device and verify pairing, enrollment, BLE/USB unlock, disconnect/reconnect and wrong-host rejection.
4. Port host switching, authenticated sudo/PAM and SSH signing, then vault/TOTP, app unlock and agent approval. Each item requires its own functional/security checks.
5. Implement signed ESP32 OTA/recovery, finish standalone Mac packaging and run the Mac acceptance suite. Hardware-dependent features remain unavailable until suitable hardware exists; do not block the Mac release on a future PCB.
6. Validate Windows on its target operating system after the Mac release gate passes. Keep Linux implementation and runtime validation on hold until the user resumes it.
7. Design the PCB/enclosure and add battery sensing, sensor power control and tamper hardware; then measure low-power behavior. CH592F linker layout/bootloader cannot be flashed onto ESP32-S3.

## Licensing and release boundaries

Preserve each upstream license and attribution. Mac/Windows/Linux apps are Apache-2.0 (with additional notices for third-party code). Firmware, hardware, OTA, docs and agent integration retain their upstream terms; BSL components restrict commercial use before their change date. Imported tinyTouch baseline has a separate MIT notice. Forking and renaming do not remove these obligations or grant upstream trademarks.

This initial fork contains source and planning only. It has not installed immurok apps, changed PAM/login configuration, or flashed the connected prototype. Published upstream feature claims are not validation of our ESP32 port.
