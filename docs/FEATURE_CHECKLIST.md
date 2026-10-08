# tinyTouch feature parity checklist

Reviewed **2026-10-07**. Target: **ESP32-S3 + ZW111**, retaining Bluetooth and adding usable USB data/control across the companion system.

This is the canonical checklist. Check a box only after the feature is implemented in our port and its acceptance criterion passes. Imported source, upstream marketing claims and a passing helper unit test do not complete an end-to-end feature. Prototype evidence is recorded separately below. Keep this file updated with each implementation commit; do not automatically check boxes from upstream status.

**Status values:** `Yet to be done`, `In progress`, `Completed`. Every checklist table includes a Status column. Use `Yet to be done` for work that has not begun, including deferred work; Linux stays on hold in the Implementation order column and remains outside the active work plan. Use `In progress` for partial implementation or validation that is still pending, and record blockers in the evidence log. Set `Completed` and check the box together only after the acceptance criterion passes. Reopen both if a regression invalidates completion. Update statuses and evidence with each implementation push.

## Read this first: implementation order

| Order | Implement first / dependency |
| --- | --- |
| **P0 — foundation** | Confirm assembly/charging constraints, build imported ESP32 baseline, preserve recovery, establish monorepo builds |
| **P1 — device contract** | ZW111 mapping/enrollment, scheduling, capabilities, crypto/pairing/freshness and command dispatch |
| **P2 — first usable app** | Native Mac setup and management; USB/BLE selection and screen unlock on this machine |
| **P3 — multiple hosts** | Isolated host slots, switch finger, second-host registration/revocation and lock request |
| **P4 — privileged core** | Encrypted keystore, SSH/TOTP/API device operations, Mac PAM and trusted authentication routing |
| **P5 — complete Mac workflows** | Vault UI/imports, Quick Fill, password managers/custom targets, CLI/agent workflow and localization |
| **P6 — finish Mac release** | Mac OTA/recovery, signing/packaging, diagnostics, comprehensive Mac tests and standalone tinyTouch.app delivery |
| **P7 — Windows** | Windows companion, service, Credential Provider and Windows tests, after Mac acceptance |
| **P8 — hardware / PCB** | Sensor gating, ADC, measured power, tamper, PCB/enclosure and public project/website work, after Windows |
| **On hold — Linux** | Preserve all Linux requirements; do not implement/install/test Linux until the user resumes it |

**User priority (2026-10-07): finish the device functionality needed by Mac, a separate standalone Mac app, all Mac features and their tests before Windows. Linux is on hold. PCB/enclosure and additional hardware follow Windows.** Separate Mac app means its own `tinyTouch.app` build in `app-macos`, not another repository. Keep basic power/wiring safety checks in P0; future hardware-dependent features remain P8 and must be shown as unsupported in the initial Mac release.

Mac acceptance includes USB and BLE, pairing/enrollment, host management (two Macs where needed), screen/app unlock, SSH/PAM, vault/Quick Fill, agent approval, upgrades/recovery and feature-specific failure/security tests. Do not declare Mac complete from a successful build alone. Windows begins after this release gate passes; Linux rows remain on hold.

**First concrete tasks:** HW-01/HW-02 → FW-01 → FW-03/FW-07 → DOC-01/FW-09 → FW-08/FW-11–FW-15 → MAC-01–MAC-07/FW-10. Implement and validate one usable Mac path before expanding to the remaining Mac workflows. Complete the Mac release gate before starting Windows.

## General feature index (references, not duplicate tasks)

| Overall capability | Canonical detailed items |
| --- | --- |
| Fingerprint management | FW-03–FW-06; platform-specific UI: MAC-06, WIN-06, LIN-05/06 |
| BLE and USB connectivity | FW-08–FW-17; adapters: MAC-02/03, WIN-02/03, LIN-02/03 |
| Screen unlock / intentional lock | MAC-07/09, WIN-05, LIN-09; device event: FW-18 |
| Mutual authentication and gated actions | FW-11–FW-15; OS IPC: MAC-11, WIN-04, LIN-04 |
| Two-host pairing and switching | FW-19–FW-22; UI: MAC-08, WIN-06, LIN-13 |
| sudo / system auth / polkit | MAC-10–MAC-13, LIN-08 |
| SSH key generation, storage and signing | FW-23–FW-26; agents: MAC-16, WIN-07, LIN-11 |
| OTP/API vault and Quick Fill | FW-23/24/26–FW-28; interfaces: MAC-17–MAC-21, WIN-08, LIN-12 |
| Password-manager and custom app unlock | MAC-14/15, LIN-10 |
| Agent command approval and environment injection | MAC-21/22, LIN-14, AGENT-01–AGENT-04 |
| Updates, recovery and release delivery | OTA-01–OTA-07; app uploaders: MAC-26, WIN-10, LIN-15 |
| Battery operation, power saving, controls and tamper | FW-29–FW-34, HW-01–HW-08 |
| Setup, diagnostics, translations and installers | Platform settings/packaging rows; DOC-04 and ROOT-05/06 |
| Public project, documentation and website | ROOT/DOC, WEB and ORG rows |

## Evidence already available from the earlier prototype

These checks record narrow accomplishments, not completion of the combined native-app port.

| Done | Status | ID | Existing evidence | Port acceptance still needed |
| --- | --- | --- | --- | --- |
| [x] | Completed | BASE-01 | User measured approximately 3.29 V on USB-powered 3V3 rail and board stayed cool | HW-01/HW-02 final assembly verification |
| [x] | Completed | BASE-02 | Sensor fingerprint triggered working authentication in original tinyTouch setup | FW-03–FW-06 under new protocol/native UI |
| [x] | Completed | BASE-03 | User confirmed BLE Mac unlock worked | MAC-07 through native companion |
| [x] | Completed | BASE-04 | User confirmed battery-powered BLE Mac unlock with USB disconnected | MAC-07 and power/reconnect regression |
| [x] | Completed | BASE-05 | Prior battery checks: approximately 3.28 V USB rail, 3.27 V battery rail, 3.8 V loaded battery | Charging compatibility, measured runtime and calibrated ADC remain open |

## Per-folder implementation checklist

The source links at each section identify the README and implementation used for that section. Every row has one stable ID. A platform UI/transport integration is distinct from the shared device feature; the general index above adds no duplicate checkboxes.

### Repository / documentation

Sources: [Overview README](https://github.com/immurok/immurok/blob/main/README.md), [protocol](../docs/protocol.md), [app specification](../docs/app-spec.md).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [x] | Completed | ROOT-01 | `root` | Consolidate the eight runtime/design/agent components and preserve histories/licenses | P0 | Combined remote folders and imported history verified |
| [x] | Completed | ROOT-02 | `docs` | Record folder responsibilities and ESP32 compatibility gaps | P0 | Published folder analysis with source links |
| [x] | Completed | ROOT-03 | `docs` | Create deduplicated feature tracker and pinned upstream inventory | P0 | This checklist and source inventory validated |
| [ ] | Yet to be done | ROOT-04 | `root / CI` | Build and release each component from monorepo working directories | P0 | Root CI runs correct jobs; downloadable artifacts identify board/platform |
| [ ] | Yet to be done | DOC-01 | `docs/protocol.md` | Document versioned commands, capabilities, errors and BLE/USB framing | P1 | C, Swift, C# and Rust fixtures agree; unsupported commands fail explicitly |
| [ ] | Yet to be done | DOC-02 | `docs/security.md` | Document threat model, pairing, request freshness, local IPC and storage trust | P1 | Descriptions match implemented security and tested failure paths |
| [ ] | Yet to be done | DOC-03 | `docs/app-spec.md` | Document platform feature matrix and supported OS/desktop limits | P2 | No unsupported feature is advertised as working |
| [ ] | In progress | DOC-04 | `docs / root README` | Provide setup, permissions, transport selection, recovery and troubleshooting guides | P2 | A fresh user can build/pair/recover without relying on chat history |
| [ ] | Yet to be done | ROOT-05 | `root / packaging` | Apply tinyTouch branding with configuration and Keychain migration | P2 | Existing owner credentials remain accessible; upstream legal notices preserved |
| [ ] | Yet to be done | ROOT-06 | `root / releases` | Produce release notes, compatibility matrix and reproducible version metadata | P6 | App, firmware and package versions correspond to tested artifacts |

### firmware

Sources: [Firmware README](https://github.com/immurok/firmware/blob/main/README.md), [APP](../firmware/APP), [profiles](../firmware/Profile), [ESP32 baseline](../firmware/ports/esp32s3).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [x] | Completed | FW-01 | `ports/esp32s3` | Independently build the imported working ESP-IDF baseline | P0 | Clean build succeeds with recorded dependencies and image/partition sizes |
| [x] | Completed | FW-02 | `test` | Run existing ten host-side upstream test programs | P0 | All ten passed on this Mac on 2026-10-07 |
| [ ] | Yet to be done | FW-03 | `ports/esp32s3/main/fingerprint.*` | Adapt and verify ZW111 UART, touch detection and template discovery | P1 | Real sensor capacity/IDs confirmed; existing enrolled finger retained |
| [ ] | Yet to be done | FW-04 | `APP fingerprint / ESP32 driver` | Logical fingerprint enrollment with progress, completion and cancellation | P1 | ZW111 capture sequence exposed through capabilities; cancel leaves consistent slots |
| [ ] | Yet to be done | FW-05 | `APP fingerprint / ESP32 driver` | List, identify, match and delete authentication fingerprints | P1 | Valid/no-match paths tested; physical and logical IDs never confused |
| [ ] | Yet to be done | FW-06 | `APP/include/fp_policy.h` | Preserve last-authentication-finger and switch-finger safety rules | P1 | Translated policy tests pass; owner cannot become switch-only |
| [ ] | Yet to be done | FW-07 | `APP/main.c / ESP32 tasks` | Replace WCH startup, TMOS events and IRQ assumptions with ESP-IDF services | P1 | Heavy sensor/crypto/storage work does not block BLE; shared buffers serialized |
| [ ] | Yet to be done | FW-08 | `Profile / ESP32 BLE` | Implement encrypted command/response GATT and HID keyboard service | P1 | Bond required for protected operations; full command contract works |
| [ ] | Yet to be done | FW-09 | `Profile / ESP32 BLE` | Expose device information, status, version and actual feature capabilities | P1 | Mac reads status even when HID hides standard DIS; no version spoofing |
| [ ] | Yet to be done | FW-10 | `ports/esp32s3 USB` | Keep USB keyboard and control/data transport with explicit USB/BLE selection | P2 | Both modes work; one action produces one credential output |
| [ ] | Yet to be done | FW-11 | `APP/immurok_security.c` | ECDH P-256 pairing, HKDF-SHA256 derivation and reliable key erasure | P1 | Cross-language known-answer and malformed-key tests pass |
| [ ] | Yet to be done | FW-12 | `APP pairing policy` | Physical-presence pairing, timeout/cancel and owner-approved migration | P1 | Unapproved nearby host rejected; existing templates not silently wiped |
| [ ] | Yet to be done | FW-13 | `APP security / storage` | Persistent bonds and paired host keys with deliberate unpair/repair behavior | P1 | Power cycle retains owner; revoked peer cannot authenticate |
| [ ] | Yet to be done | FW-14 | `APP security / commands` | Reconnect challenge and authenticated events with session/request freshness | P1 | Stale, forged and duplicate events rejected on each transport |
| [ ] | Yet to be done | FW-15 | `APP/hidkbd.c command dispatcher` | AUTH_REQUEST, cancel, busy/timeout and fingerprint-gated operations | P1 | Wrong finger, cancellation, disconnect and timeout grant no authority |
| [ ] | Yet to be done | FW-16 | `APP/hidkbd.c match events` | Pending match delivery and acknowledgement after reconnect | P2 | Expiry and sequence binding prevent stale or double unlock |
| [ ] | Yet to be done | FW-17 | `APP HID` | Wake/pre-trigger keystrokes and reliable key/modifier release | P2 | No stuck keys; wake behavior verified on supported hosts |
| [ ] | Yet to be done | FW-18 | `APP lock notification` | Long-touch lock request independent of ordinary match | P3 | Intentional hold requests lock; auth/enrollment gates suppress accidental lock |
| [ ] | Yet to be done | FW-19 | `APP/immurok_slots.c / slot_meta.c` | Two isolated host identities, bonds and shared keys | P3 | Two real hosts registered; neither receives the other's credentials |
| [ ] | Yet to be done | FW-20 | `APP host switching` | Dedicated switch fingerprint changes active host without authorizing actions | P3 | First host disconnects, selected host reconnects; no unlock/sign from switch finger |
| [ ] | Yet to be done | FW-21 | `APP pairing policy` | Second-host registration using enrolled owner finger plus physical confirmation | P3 | Both presence checks required; claimed slots cannot be silently overwritten |
| [ ] | Yet to be done | FW-22 | `APP slot commands` | Host-slot status and authorized self/other-slot removal | P3 | Intended slot revoked; other slot and stored keys remain consistent |
| [ ] | Yet to be done | FW-23 | `APP/immurok_keystore.c` | Transactional encrypted storage for SSH, OTP and API records | P4 | Power-loss recovery, deletion, host access and confidentiality tests pass |
| [ ] | Yet to be done | FW-24 | `APP keystore protocol` | Count/read/write/commit/delete/chunked-result command family | P4 | Bounds, capacity, partial writes and fingerprint gates tested |
| [ ] | Yet to be done | FW-25 | `APP SSH crypto` | Device P-256 key generation, import, public-key lookup and signing | P4 | OpenSSH signatures verify; private bytes are never returned by normal reads |
| [ ] | Yet to be done | FW-26 | `APP vault capacity` | Support at least 32 SSH, 128 OTP and 50 API records with explicit limits | P4 | Capacity/field-length tests pass; negotiated limits match available storage |
| [ ] | Yet to be done | FW-27 | `APP/totp_core.c` | Fingerprint-gated six-digit SHA-1 TOTP using host-supplied time | P4 | RFC 6238 vectors, time bounds and binary-secret encoding verified |
| [ ] | Yet to be done | FW-28 | `APP API secrets` | Fingerprint-gated API secret reads and masked metadata responses | P4 | Unauthenticated reads expose no secret; gate binds intended record |
| [ ] | Yet to be done | FW-29 | `APP reset / tamper` | Authenticated software reset and deliberate physical long-button reset | P6 | Templates, keys and bonds cleared; interrupted reset resumes safely |
| [ ] | Yet to be done | FW-30 | `APP battery / Profile` | Measured raw battery voltage and Battery Service notifications | P8 | ADC calibrated; no placeholder percentage shown as measured charge |
| [ ] | Yet to be done | FW-31 | `APP power / GPIO` | Sensor rail gating, touch/button wake and UART sleep configuration | P8 | Hardware supports control; sensor wakes/matches reliably after sleep |
| [ ] | Yet to be done | FW-32 | `APP advertising / low power` | Low-battery hysteresis, advertising phases and reconnect after wake | P8 | Measured power and threshold tests; recovery works while charging |
| [ ] | Yet to be done | FW-33 | `APP indicators` | Pair/enroll/auth/error/low-battery/reset LED feedback | P8 | Board-specific indicators match documented states |
| [ ] | Yet to be done | FW-34 | `APP/tamper.c / factory_test.*` | Case-open response, persistent wipe marker and factory-test behavior | P8 | Requires added hardware; cleanup survives interrupted power and respects factory state |
| [ ] | Yet to be done | FW-35 | `tools/devtest / test` | Port device integration and regression suites to ESP32 | P6 | Read-only tests run routinely; reset/destructive tests isolated with recovery |
| [x] | Completed | FW-36 | `ports/esp32s3 / USB OTA` | Deploy and verify our baseline without erasing enrollment or host pairing | P0 | Enrolled-finger upload, activation, retained state, sensor and transport regression verified |

### app-macos

Sources: [Mac README](https://github.com/immurok/app-macos/blob/main/README.md), [Sources](../app-macos/Sources), [CLI](../app-macos/CLISources), [tests](../app-macos/Tests).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | In progress | MAC-01 | `Sources / Package.swift` | Build native menu-bar app and first-run setup wizard | P2 | Local signed test bundle launches; permissions and missing prerequisites explained |
| [ ] | In progress | MAC-02 | `Sources/BLEManager.swift` | Discover tinyTouch, pair, verify and reconnect over compatible BLE protocol | P2 | Native app handles cold start, disconnect, sleep/wake and wrong device |
| [ ] | In progress | MAC-03 | `Sources transport layer` | Add USB companion transport and transport/device selector | P2 | Same management commands work over USB and BLE without duplicate action |
| [ ] | Yet to be done | MAC-04 | `Sources/ImmurokSecurity.swift` | Implement negotiated crypto/freshness client and migrate Keychain identity | P2 | Firmware fixtures verified; existing saved password/key access preserved |
| [ ] | In progress | MAC-05 | `Sources device views` | Show connection, host slot, battery, firmware and readiness status | P2 | Capability-driven display; unknown battery distinguished from measured level |
| [ ] | In progress | MAC-06 | `Sources/FingerprintView.swift` | Enroll, cancel, delete and rename logical fingerprints | P2 | UI follows ZW111 progress/capacity and receives accurate final state |
| [ ] | Yet to be done | MAC-07 | `Sources/AppDelegate.swift` | Native app screen unlock and wake/reconnect retry flow | P2 | Lock-screen unlock over BLE/USB; pending match cannot duplicate typing |
| [ ] | In progress | MAC-08 | `Sources/DualHostView.swift` | First/second-host guidance, slot status and authorized unbinding | P3 | Two-host workflow tested with ESP32, including lost-host removal |
| [ ] | Yet to be done | MAC-09 | `Sources lock handling` | Confirm and perform device-requested screen lock | P3 | Cancel/confirm behavior correct; no unintended authentication grant |
| [ ] | Yet to be done | MAC-10 | `pam / PAMSocketServer.swift` | sudo and system-authorization PAM authentication with password fallback | P4 | Real sudo/authorization positive/negative tests; safe fallback and uninstall |
| [ ] | Yet to be done | MAC-11 | `CLISources/PamKeyCommand.swift / pam` | Strong local PAM channel using nonce/HMAC, root-owned verifier and peer checks | P4 | Spoofed/replayed IPC denied; install/remove/status/verify-peer paths tested |
| [ ] | Yet to be done | MAC-12 | `Sources/AuthContextDetector.swift / AuthInjector.swift` | Route pure PAM, credential injection and GUI-to-PAM bridge appropriately | P4 | Pending PAM wins; no recognized target means no injection or authorization |
| [ ] | Yet to be done | MAC-13 | `AuthInjectionKit / whitelist` | Check running target identity, focused secure field and automation context | P4 | Untrusted app cannot receive passwords by imitating a bundle/name |
| [ ] | Yet to be done | MAC-14 | `Sources password targets` | Unlock 1Password/Bitwarden and supported system dialogs with separate saved secrets | P5 | Each target explicitly configured/tested; disabled feature clears intended secret |
| [ ] | In progress | MAC-15 | `Sources/Automation* / TargetPicker.swift` | Create/edit/enable custom targets and import/export automation configuration | P5 | Rules and protected export round-trip; target selection remains explicit |
| [ ] | Yet to be done | MAC-16 | `Sources/SSHAgentServer.swift / SSHKeyCache.swift` | OpenSSH identity enumeration and biometric signing proxy | P4 | SSH authentication and git signing work with ESP32-held keys |
| [ ] | Yet to be done | MAC-17 | `Sources/KeystoreViewModel.swift / key views` | Browse/create/edit/delete SSH, OTP and API records with capacity/progress UI | P5 | Commands operate on intended records and honor all device gates |
| [ ] | Yet to be done | MAC-18 | `Sources/SSHKeyImporter.swift` | Import supported existing P-256 SSH keys and show public fingerprints | P5 | Accepted encodings round-trip; unsupported types produce clear errors |
| [ ] | Yet to be done | MAC-19 | `Sources OTP import/export` | OTP import/export workflow with explicit secret-export policy | P5 | Supported formats tested; no claim of non-exportability contradicts enabled export |
| [ ] | Yet to be done | MAC-20 | `Sources/QuickFillPanel.swift / GlobalHotKey.swift` | Customizable Quick Fill hotkey, search/category filters and OTP/API/public-key insertion | P5 | Correct focused target receives selected value; cancel/timeout and secret lifetime tested |
| [ ] | Yet to be done | MAC-21 | `CLISources main / EnvScanner.swift` | imk list/get/run/version/help and imk:// URI resolution | P5 | All categories, unknown references, env-file injection and exit codes tested |
| [ ] | Yet to be done | MAC-22 | `Sources/CLISocketServer.swift / AgentGateOverlaySession.swift` | Exact-command approval overlay, reject/timeout and authorized subprocess lifecycle | P5 | Unapproved command never starts; no fallback after rejection; approved context scoped |
| [ ] | In progress | MAC-23 | `Sources settings / setup` | Per-feature toggles, login item, Accessibility/Bluetooth and PAM status/repair UI | P2 | Enable/disable and restart behavior consistent; settings survive upgrades |
| [ ] | Yet to be done | MAC-24 | `Resources / LocalizationManager.swift` | System-language detection, bundled translations and custom translation overrides | P5 | All available resources load; missing strings fall back safely |
| [ ] | In progress | MAC-25 | `Sources logs / About` | Runtime logs, diagnostics, app version and update checks | P6 | Useful diagnosis without passwords/private keys; correct fork release endpoint |
| [ ] | Yet to be done | MAC-26 | `FirmwareUpdateKit / update service` | ESP32 package validation, download/cache, progress, cancel and completion UI | P6 | No CH592 image accepted; image-size limits negotiated; interrupted transfer handled |
| [ ] | In progress | MAC-27 | `packaging / Tests` | Bundle signing, CLI deployment, updates and native regression suite | P6 | Fresh install/upgrade/uninstall verified; tests pass against ESP32 integration |
| [ ] | In progress | MAC-28 | `packaging / app-macos` | Deliver a standalone tinyTouch.app for this Mac, with its companion CLI and own application identity | P6 | App runs independently of development terminals; login launch, permissions and Keychain migration tested |
| [ ] | Yet to be done | MAC-29 | `Tests / device acceptance` | Complete the Mac release gate across all supported features, transports and negative cases | P6 | Every supported Mac feature has recorded automated or manual acceptance evidence; remaining hardware limitations explicit |
| [x] | Completed | MAC-30 | `Package.swift / Tests` | Build imported Mac app/CLI and run existing host-side tests before ESP32 adaptation | P0 | Swift build and all existing test suites pass; does not imply device/native-app compatibility |

### app-win

Sources: [Windows README](https://github.com/immurok/app-win/blob/main/README.md), [implementation plan](../app-win/IMPLEMENTATION_PLAN.md), [projects](../app-win).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | WIN-01 | `ImmurokCommon` | Align command enums, framing, capabilities and authenticated events | P7 | Cross-language fixtures agree; retired/unsupported commands handled explicitly |
| [ ] | Yet to be done | WIN-02 | `ImmurokService/Ble / Security` | BLE discovery/pairing/reconnect and protected per-user host credentials | P7 | ESP32 works for intended Windows user; other user/device cannot borrow authority |
| [ ] | Yet to be done | WIN-03 | `ImmurokService transport` | Add USB control transport and explicit mode selection | P7 | USB/BLE feature behavior matches without double output |
| [ ] | Yet to be done | WIN-04 | `ImmurokService/Ipc` | Hardened named-pipe IPC and per-user client authorization | P7 | Pipe/process/session spoofing and cross-user requests rejected |
| [ ] | Yet to be done | WIN-05 | `ImmurokCredentialProvider / System` | Native logon/lock-screen credential delivery through trusted provider | P7 | Real Windows login, lock/unlock and second-account behavior verified |
| [ ] | Yet to be done | WIN-06 | `ImmurokClient` | Tray/WPF UI, first-run pairing, fingerprint management and status/settings | P7 | Capability-driven ZW111 management and host slot flows tested |
| [ ] | Yet to be done | WIN-07 | `ImmurokService/Ssh / ImmurokConsolePrompt` | SSH agent and visible interactive signing authorization | P7 | OpenSSH/git signing succeeds only for approved operation |
| [ ] | Yet to be done | WIN-08 | `ImmurokClient / service key handlers` | SSH/TOTP/API management and supported secret retrieval workflows | P7 | Platform feature matrix reflects tested commands, not preview claims |
| [ ] | Yet to be done | WIN-09 | `ImmurokCli` | CLI commands and meaningful service/permission errors | P7 | Every advertised command tested against ESP32 service |
| [ ] | Yet to be done | WIN-10 | `ImmurokService/Ota` | ESP32 firmware validation/transfer and progress reporting | P7 | Wrong board/signature/version refused; disconnect handled |
| [ ] | Yet to be done | WIN-11 | `packaging / build/install scripts` | Service/provider installation, auto-start, app updates and clean removal | P7 | Install/upgrade/uninstall and upgrade while LogonUI holds provider tested safely |

### app-linux-rs

Sources: [Linux README](https://github.com/immurok/app-linux-rs/blob/main/README.md), [crates](../app-linux-rs/crates), [packaging](../app-linux-rs/packaging).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | LIN-01 | `crates/immurok-common` | Protocol, crypto, paths and authenticated IPC contracts for ESP32 | On hold | Fixtures agree with firmware and other clients |
| [ ] | Yet to be done | LIN-02 | `crates/immurok-daemon / scripts` | BlueZ BLE discovery/bonding/reconnect and notify helper integration | On hold | Cold start, suspend/resume and bond recovery tested with ESP32 |
| [ ] | Yet to be done | LIN-03 | `crates daemon transport / device rules` | USB backend and user/service device access | On hold | Both transports work under intended service permissions |
| [ ] | Yet to be done | LIN-04 | `crates/immurok-daemon / packaging` | Privileged separation, dedicated service user and protected /run sockets | On hold | Wrong UID/socket spoofing denied; service hardening maintained |
| [ ] | Yet to be done | LIN-05 | `crates/immurok-client / immurok-cli` | Management CLI and TUI dashboard, keys, PAM, logs and firmware pages | On hold | Every advertised page/command handles supported device capabilities |
| [ ] | Yet to be done | LIN-06 | `crates/immurok-gui` | GTK4/libadwaita management UI and auth dialogs | On hold | Pair/enroll/status/settings and prompt cancellation tested |
| [ ] | Yet to be done | LIN-07 | `crates/immurok-session-agent` | Bridge authenticated system-service requests to correct desktop session | On hold | Active/user session selection tested; no cross-user credential injection |
| [ ] | Yet to be done | LIN-08 | `pam / helper / packaging polkit` | sudo and polkit fingerprint approval, hook install/remove/repair and fallback | On hold | Real positive/negative PAM tests; rejected prompt grants nothing |
| [ ] | Yet to be done | LIN-09 | `daemon screen / pam` | GNOME login/lock and KDE lock/session integration with documented limits | On hold | GNOME/GDM and KDE tested separately; SDDM manual requirements explicit |
| [ ] | Yet to be done | LIN-10 | `polkit / desktop integrations` | Password-manager system authentication for supported 1Password/Bitwarden/KeePassXC setups | On hold | App-specific setup and Flatpak policy limits documented/tested |
| [ ] | Yet to be done | LIN-11 | `daemon ssh_agent` | SSH identities and signing with device-held P-256 keys | On hold | OpenSSH/git signing and gate failures tested |
| [ ] | Yet to be done | LIN-12 | `cli keys / daemon keystore` | SSH/OTP/API creation, listing, deletion and authorized retrieval | On hold | Record/capacity boundaries and masked secret reads tested |
| [ ] | Yet to be done | LIN-13 | `cli slot / daemon` | Two-host status, owner registration and unbinding user flows | On hold | Real Mac/Linux host switch test passes |
| [ ] | Yet to be done | LIN-14 | `cli imk / daemon socket` | imk URI/env-file workflows and visible agent command approval | On hold | Approve/reject/timeout, subprocess exit and context binding tested |
| [ ] | Yet to be done | LIN-15 | `cli fw / daemon ota` | Update discovery/cache, progress and board-aware upgrade policy | On hold | ESP32 trust/size/battery rules used; no CH592 bridge image offered |
| [ ] | Yet to be done | LIN-16 | `packaging / Makefile` | Debian/Ubuntu, Fedora and Arch builds, service startup and uninstall | On hold | Packages and permission/config migration tested on target distros |
| [ ] | Yet to be done | LIN-17 | `docs / tests` | Wayland focus, GNOME/KDE/wlroots limitations and diagnostic regression coverage | On hold | Unsupported compositor behavior stated honestly; suite runs on Linux |

### ota

Sources: [OTA README](https://github.com/immurok/ota/blob/main/README.md), [package generator](../ota/ota-package.py), [v2 tests](../ota/test_imfw_v2.py).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | OTA-01 | `ota-package.py / generate_ota_keys.py` | ESP32 board-tagged v2 package generation and separate fork signing/encryption keys | P6 | Public source contains no private key; package fixtures verify metadata and signatures |
| [ ] | Yet to be done | OTA-02 | `firmware OTA service / OTA engines` | Authenticated update entry, chunk erase/write/readback, status and inactivity abort | P6 | Wrong peer, malformed chunks and interrupted sessions handled safely |
| [ ] | Yet to be done | OTA-03 | `package validation / device` | ECDSA signature, AES-CTR decrypt, plaintext SHA-256 and security-version floor | P6 | Tampered/wrong-board/wrong-key/downgrade images rejected |
| [ ] | Yet to be done | OTA-04 | `jumpapp / iap replacement` | ESP-IDF inactive-slot install, boot confirmation and rollback | P6 | No CH592 startup/layout reused; failed boot and power-loss recovery verified |
| [ ] | Yet to be done | OTA-05 | `ota-update.py / app sockets` | Update CLI delegation compatible with Mac/Windows/Linux transports | P6 | Correct app/device selected; progress/errors/exit status tested |
| [ ] | Yet to be done | OTA-06 | `build/deploy/migration scripts` | USB recovery flashing and migration from our development firmware | P6 | Recoverable image and board instructions tested; no upstream CH592 bridge reused |
| [ ] | Yet to be done | OTA-07 | `release scripts / web metadata` | Publish own firmware manifest/artifacts and self-build trust documentation | P6 | Apps fetch fork artifacts; official and fork signing identities never confused |

### hardware

Sources: [Hardware README](https://github.com/immurok/hardware/blob/main/README.md), [schematic/PCB references](../hardware).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | HW-01 | `ESP32 + ZW111 wiring` | Confirm pin map and document direct-solder assembly for current board | P0 | Photo/wiring review, continuity and voltage checks recorded before final assembly |
| [ ] | Yet to be done | HW-02 | `battery / charging` | Verify selected cell protection, polarity and charge-current compatibility | P0 | Board/cell specifications and measured power paths support this assembly |
| [ ] | Yet to be done | HW-03 | `sensor rail` | Add suitable sensor power switch while keeping touch wake available | P8 | Rail control and leakage measured; UART cannot back-power sensor |
| [ ] | Yet to be done | HW-04 | `battery measurement` | Add ADC divider/protection and calibrated battery reporting | P8 | Full-charge voltage safe at GPIO; firmware agrees with multimeter |
| [ ] | Yet to be done | HW-05 | `controls / indicators` | Map physical pair/reset button and LED states without boot-pin conflicts | P8 | Button timing and boot/recovery behavior tested on assembled board |
| [ ] | Yet to be done | HW-06 | `tamper / power bypass` | Case switch, always-available tamper power path and interrupted-wipe design | P8 | Opening powered/off device produces intended response; current hardware lacks this |
| [ ] | Yet to be done | HW-07 | `power / radio` | Measure sleep, active, charging and BLE reconnect performance | P8 | Publish measured ESP32 runtime; do not inherit CH592 month-long standby claim |
| [ ] | Yet to be done | HW-08 | `schematic / pcb / enclosure` | Provide ESP32-specific circuit, board/assembly files and mechanical design | P8 | Editable sources/BOM published; USB data, antenna and sensor access retained |

### imk-skill

Sources: [Agent README](https://github.com/immurok/imk-skill/blob/main/README.md), [guide](../imk-skill/skills/using-imk/SKILL.md).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | AGENT-01 | `skills/using-imk` | Adapt command/SSH/secret approval guide to working tinyTouch CLI | P5 | Examples reference tested commands and describe actual authorization lifetime |
| [ ] | Yet to be done | AGENT-02 | `.claude-plugin / convention files` | Claude plugin and Cursor/Windsurf/Continue/Cline/Codex/Aider/Gemini integration instructions | P5 | Local/monorepo paths install correctly; no deleted fork URL used |
| [ ] | Yet to be done | AGENT-03 | `skill approval rules` | Document rejection/timeout handling, no bypass and secret-safe command display | P5 | Integration smoke test requests explicit command approval and respects rejection |
| [ ] | Yet to be done | AGENT-04 | `README / troubleshooting` | Discovery, install/update, unsupported/headless workflows and troubleshooting | P5 | Fresh consuming project can find CLI and diagnose absent companion/device |

### website / organization profile / archived app-linux

Sources: [Website README](https://github.com/immurok/website/blob/main/README.md), [organization profile](https://github.com/immurok/.github/blob/main/profile/README.md), [archived Linux source](https://github.com/immurok/app-linux).

| Done | Status | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | Yet to be done | WEB-01 | `website (not imported)` | Create tinyTouch product/docs/download pages based on verified capabilities | P8 | Website source resides inside combined repo; no upstream sales/certification claims copied |
| [ ] | Yet to be done | WEB-02 | `website/blog-src / theme` | Hugo blog, technical articles, shared layout and local preview/build | P8 | Build succeeds; ESP32 hardware/security claims reflect actual implementation |
| [ ] | Yet to be done | WEB-03 | `website/tools / language pages` | Generated product/platform/agent/comparison pages and multilingual content | P8 | Generated output and translations consistent with tested feature matrix |
| [ ] | Yet to be done | WEB-04 | `website/functions / firmware manifest` | App download routing, release links and board-specific firmware metadata | P8 | Only our tested releases served; no upstream binaries presented as ESP32 compatible |
| [ ] | Yet to be done | WEB-05 | `website/3d / img` | Device renders/3D viewer and relevant visual assets | P8 | Use our board/enclosure and appropriately licensed assets |
| [ ] | Yet to be done | WEB-06 | `website tools / metadata` | Sitemap, crawler text, structured data, redirects and 404 behavior | P8 | Build checks pass; metadata contains accurate product facts |
| [ ] | Yet to be done | WEB-07 | `website deployment` | Own hosting configuration and optional analytics/feedback endpoints | P8 | Deployment reviewed for our account/domain; no upstream secrets or endpoints reused |
| [ ] | Yet to be done | ORG-01 | `.github/profile (not imported)` | Update project/profile repository links and feature overview | P8 | Profile points to combined repo and verified releases; upstream org identity not impersonated |
| [ ] | Yet to be done | LEGACY-01 | `archived app-linux (not imported)` | Compare Python BLE/security/daemon/CLI/PAM behavior against maintained Rust app | On hold | Any unique behavior captured under LIN items; no second competing Linux runtime needed |

## Upstream coverage and differences

All **11 public upstream repositories** were inventoried through GitHub, and each root README was fetched when available. The eight already imported components match the current default-branch commits at review time. `website`, `.github` and archived `app-linux` were additionally reviewed through their trees/content; they are tracked above and have not been imported or forked. The archived Python app has no root README; its source was used instead. The organization profile has `profile/README.md` rather than a root README.

The complete revision list is in [upstream-feature-sources.json](upstream-feature-sources.json). Companion READMEs have different feature coverage; platform tasks reflect their actual interfaces, not an assumption that every app already exposes every feature. Website commercial checkout, shipping, official certification and upstream branding are not functional ESP32 parity requirements. Website work uses our own identity/assets and does not publish unverified hardware claims.

Important specification differences to resolve while implementing:

- Slot PIN commands `0x3A`/`0x3B` are **retired in current firmware**, replaced by fingerprint plus button registration. Stale app symbols/comments do not create new parity requirements.
- Some overview prose describes legacy HMAC OTA; current OTA v2 uses ECDSA signatures. Implement the current secure package behavior for ESP32, not the CH592 boot layout.
- R559S enrollment counts/capacity, CH592 flash limits and advertised standby current do not describe ZW111/ESP32. Discover capabilities and measure our hardware.
- The encrypted-vault marketing claim does not substitute for verified encryption at rest. FW-23 requires an explicit implementation and tests.
- README/spec descriptions of pre-authorization lifetimes differ. Define the accepted policy from current code and verify single-use/request/service scope; do not silently choose the widest window.
- Linux password-manager support depends on the application's own system-auth/polkit configuration; KDE SDDM and wlroots workflows have separate limitations.

## Completion log and maintenance rule

For every future checked item, append or link evidence here: implementation commit/PR, validation command or manual scenario, platform/board, date and limitations. Reopen the checkbox and update Status if a regression invalidates its criterion. Update the related platform row separately when that integration passes. Failed/blocked work remains unchecked with its reason in the log; obsolete requirements need an explained replacement, not a fake completion.

| Item | Evidence | Date |
| --- | --- | --- |
| ROOT-01 | Combined repository remote verified; every removed component fork's branch heads were ancestors of combined history before deletion | 2026-10-07 |
| ROOT-02 | [Folder analysis](esp32-folder-analysis.md), commit `7beb65c` | 2026-10-07 |
| ROOT-03 | This document and pinned source inventory; IDs, duplicate names, order values and local links validated before publishing | 2026-10-07 |
| MAC-30 | [Mac build/test baseline](MAC_BUILD_VALIDATION.md): app and CLI built; 97 existing tests passed with zero failures | 2026-10-07 |
| FW-01 | [Build validation](../firmware/ports/esp32s3/BUILD_VALIDATION.md): clean baseline and fork-identity rebuild passed; 664,400-byte ESP32 image validated | 2026-10-07 |
| FW-36 | Upload/activation, retained one finger/host, USB PING, encrypted BLE/helper reconnect and live fingerprint OK AUTH passed; Mac lock-screen retest remains separate | 2026-10-07 |
| FW-02 | `make` in `firmware/test`: all ten host executables passed; fake storage tests do not validate ESP32 flash/peripherals | 2026-10-07 |
| BASE-01–BASE-05 | User-confirmed prototype measurements and operation from earlier setup; original helper/custom firmware, not the new native companion | Earlier setup; recorded 2026-10-07 |

No new flashing, PAM installation, app installation or hardware change was performed to create this checklist.

### Native ESP32 Mac compatibility work — 2026-10-07

Full Mac app feature parity is the release goal. The connection/status adapter is implemented and the native USB probe passed on the attached ESP32. BLE/USB management, authenticated native operations and transport acceptance criteria remain open; do not check connection items complete on status alone. Implementation dependencies and current evidence: [MAC_ESP32_COMPATIBILITY.md](MAC_ESP32_COMPATIBILITY.md). Existing MAC IDs remain authoritative; no duplicate feature entries were added.

Native USB fingerprint/settings management is now implemented with last-finger protection and post-operation readback. Live inventory/settings reads passed; 115 Mac tests pass. MAC-06 remains in progress until physical enrollment, cancellation and protected deletion are tested. Native Bluetooth bonded identity has also been confirmed; broader reconnect/transport management criteria remain open.

October 7 startup/interface update: the ESP32-specific startup wizard and original Keys/Features/Automation/About layouts are implemented. 118 Mac tests pass. MAC-01 remains In progress pending physical wizard acceptance and production packaging; inactive device-backed controls remain labeled and disabled. See MAC_ESP32_COMPATIBILITY.md for readiness gates and remaining host-pairing work.

October 7 host-management stage: MAC-08 now has a native registered-computer panel backed by the signed tinyTouch CLI, live readback, bounded host-ID parsing, fingerprint-approved removal, fresh-inventory last-host protection and post-removal verification. Live read confirmed one of eight hosts. 123 Mac tests pass. Second-host registration/removal remains untested, so MAC-08 stays In progress. MAC-15 is In progress because original local automation editing/import/export is available; target/runtime acceptance is pending. A later live inventory confirmed slot 1 has four views and original slot 10 still has one; cancellation/deletion acceptance for MAC-06 remains open.

October 7 native Mac settings/delivery stage: Features now provides explicit SMAppService launch-at-login controls, native permission status, wizard access and read-only USB/Bluetooth helper health. Existing privileged feature controls remain inactive. 128 tests pass; actual launchctl health checks confirm both helpers running. A per-user installer verifies the app and bundled signed CLI, retains the previous native app on updates, and installs separately from the password helper. MAC-23/27/28 and DOC-04 stay In progress pending real login/restart, update/recovery, production signing and full setup acceptance. See MAC_NATIVE_SETUP.md for current installation and troubleshooting.
