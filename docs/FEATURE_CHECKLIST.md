# tinyTouch feature parity checklist

Reviewed **2026-10-07**. Target: **ESP32-S3 + ZW111**, retaining Bluetooth and adding usable USB data/control across the companion system.

This is the canonical checklist. Check a box only after the feature is implemented in our port and its acceptance criterion passes. Imported source, upstream marketing claims and a passing helper unit test do not complete an end-to-end feature. Prototype evidence is recorded separately below. Keep this file updated with each implementation commit; do not automatically check boxes from upstream status.

## Read this first: implementation order

| Order | Implement first / dependency |
| --- | --- |
| **P0 — foundation** | Confirm assembly/charging constraints, build imported ESP32 baseline, preserve recovery, establish monorepo builds |
| **P1 — device contract** | ZW111 mapping/enrollment, scheduling, capabilities, crypto/pairing/freshness and command dispatch |
| **P2 — first usable app** | Native Mac setup and management; USB/BLE selection and screen unlock on this machine |
| **P3 — multiple hosts** | Isolated host slots, switch finger, second-host registration/revocation and lock request |
| **P4 — privileged core** | Encrypted keystore, SSH/TOTP/API device operations, Mac PAM and trusted authentication routing |
| **P5 — complete Mac workflows** | Vault UI/imports, Quick Fill, password managers/custom targets, CLI/agent workflow and localization |
| **P6 — remaining parity/release** | Windows/Linux validation, signed OTA/recovery, measured power, added tamper hardware, packaging and website |

P6 work can proceed in independent tracks after its dependencies exist: Windows/Linux need P1–P4; OTA needs P0/P1 plus a defined signing/storage policy; sensor gating/tamper need new hardware. The order is a dependency plan, not an instruction to ignore a hardware prerequisite until later.

**First concrete tasks:** HW-01/HW-02 → FW-01 → FW-03/FW-07 → DOC-01/FW-09 → FW-08/FW-11–FW-15 → MAC-01–MAC-07/FW-10. Implement and validate one usable Mac path before enabling every privileged feature.

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

| Done | ID | Existing evidence | Port acceptance still needed |
| --- | --- | --- | --- |
| [x] | BASE-01 | User measured approximately 3.29 V on USB-powered 3V3 rail and board stayed cool | HW-01/HW-02 final assembly verification |
| [x] | BASE-02 | Sensor fingerprint triggered working authentication in original tinyTouch setup | FW-03–FW-06 under new protocol/native UI |
| [x] | BASE-03 | User confirmed BLE Mac unlock worked | MAC-07 through native companion |
| [x] | BASE-04 | User confirmed battery-powered BLE Mac unlock with USB disconnected | MAC-07 and power/reconnect regression |
| [x] | BASE-05 | Prior battery checks: approximately 3.28 V USB rail, 3.27 V battery rail, 3.8 V loaded battery | Charging compatibility, measured runtime and calibrated ADC remain open |

## Per-folder implementation checklist

The source links at each section identify the README and implementation used for that section. Every row has one stable ID. A platform UI/transport integration is distinct from the shared device feature; the general index above adds no duplicate checkboxes.

### Repository / documentation

Sources: [Overview README](https://github.com/immurok/immurok/blob/main/README.md), [protocol](../docs/protocol.md), [app specification](../docs/app-spec.md).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [x] | ROOT-01 | `root` | Consolidate the eight runtime/design/agent components and preserve histories/licenses | P0 | Combined remote folders and imported history verified |
| [x] | ROOT-02 | `docs` | Record folder responsibilities and ESP32 compatibility gaps | P0 | Published folder analysis with source links |
| [x] | ROOT-03 | `docs` | Create deduplicated feature tracker and pinned upstream inventory | P0 | This checklist and source inventory validated |
| [ ] | ROOT-04 | `root / CI` | Build and release each component from monorepo working directories | P0 | Root CI runs correct jobs; downloadable artifacts identify board/platform |
| [ ] | DOC-01 | `docs/protocol.md` | Document versioned commands, capabilities, errors and BLE/USB framing | P1 | C, Swift, C# and Rust fixtures agree; unsupported commands fail explicitly |
| [ ] | DOC-02 | `docs/security.md` | Document threat model, pairing, request freshness, local IPC and storage trust | P1 | Descriptions match implemented security and tested failure paths |
| [ ] | DOC-03 | `docs/app-spec.md` | Document platform feature matrix and supported OS/desktop limits | P2 | No unsupported feature is advertised as working |
| [ ] | DOC-04 | `docs / root README` | Provide setup, permissions, transport selection, recovery and troubleshooting guides | P2 | A fresh user can build/pair/recover without relying on chat history |
| [ ] | ROOT-05 | `root / packaging` | Apply tinyTouch branding with configuration and Keychain migration | P2 | Existing owner credentials remain accessible; upstream legal notices preserved |
| [ ] | ROOT-06 | `root / releases` | Produce release notes, compatibility matrix and reproducible version metadata | P6 | App, firmware and package versions correspond to tested artifacts |

### firmware

Sources: [Firmware README](https://github.com/immurok/firmware/blob/main/README.md), [APP](../firmware/APP), [profiles](../firmware/Profile), [ESP32 baseline](../firmware/ports/esp32s3).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | FW-01 | `ports/esp32s3` | Independently build the imported working ESP-IDF baseline | P0 | Clean build succeeds with recorded dependencies and image/partition sizes |
| [x] | FW-02 | `test` | Run existing ten host-side upstream test programs | P0 | All ten passed on this Mac on 2026-10-07 |
| [ ] | FW-03 | `ports/esp32s3/main/fingerprint.*` | Adapt and verify ZW111 UART, touch detection and template discovery | P1 | Real sensor capacity/IDs confirmed; existing enrolled finger retained |
| [ ] | FW-04 | `APP fingerprint / ESP32 driver` | Logical fingerprint enrollment with progress, completion and cancellation | P1 | ZW111 capture sequence exposed through capabilities; cancel leaves consistent slots |
| [ ] | FW-05 | `APP fingerprint / ESP32 driver` | List, identify, match and delete authentication fingerprints | P1 | Valid/no-match paths tested; physical and logical IDs never confused |
| [ ] | FW-06 | `APP/include/fp_policy.h` | Preserve last-authentication-finger and switch-finger safety rules | P1 | Translated policy tests pass; owner cannot become switch-only |
| [ ] | FW-07 | `APP/main.c / ESP32 tasks` | Replace WCH startup, TMOS events and IRQ assumptions with ESP-IDF services | P1 | Heavy sensor/crypto/storage work does not block BLE; shared buffers serialized |
| [ ] | FW-08 | `Profile / ESP32 BLE` | Implement encrypted command/response GATT and HID keyboard service | P1 | Bond required for protected operations; full command contract works |
| [ ] | FW-09 | `Profile / ESP32 BLE` | Expose device information, status, version and actual feature capabilities | P1 | Mac reads status even when HID hides standard DIS; no version spoofing |
| [ ] | FW-10 | `ports/esp32s3 USB` | Keep USB keyboard and control/data transport with explicit USB/BLE selection | P2 | Both modes work; one action produces one credential output |
| [ ] | FW-11 | `APP/immurok_security.c` | ECDH P-256 pairing, HKDF-SHA256 derivation and reliable key erasure | P1 | Cross-language known-answer and malformed-key tests pass |
| [ ] | FW-12 | `APP pairing policy` | Physical-presence pairing, timeout/cancel and owner-approved migration | P1 | Unapproved nearby host rejected; existing templates not silently wiped |
| [ ] | FW-13 | `APP security / storage` | Persistent bonds and paired host keys with deliberate unpair/repair behavior | P1 | Power cycle retains owner; revoked peer cannot authenticate |
| [ ] | FW-14 | `APP security / commands` | Reconnect challenge and authenticated events with session/request freshness | P1 | Stale, forged and duplicate events rejected on each transport |
| [ ] | FW-15 | `APP/hidkbd.c command dispatcher` | AUTH_REQUEST, cancel, busy/timeout and fingerprint-gated operations | P1 | Wrong finger, cancellation, disconnect and timeout grant no authority |
| [ ] | FW-16 | `APP/hidkbd.c match events` | Pending match delivery and acknowledgement after reconnect | P2 | Expiry and sequence binding prevent stale or double unlock |
| [ ] | FW-17 | `APP HID` | Wake/pre-trigger keystrokes and reliable key/modifier release | P2 | No stuck keys; wake behavior verified on supported hosts |
| [ ] | FW-18 | `APP lock notification` | Long-touch lock request independent of ordinary match | P3 | Intentional hold requests lock; auth/enrollment gates suppress accidental lock |
| [ ] | FW-19 | `APP/immurok_slots.c / slot_meta.c` | Two isolated host identities, bonds and shared keys | P3 | Two real hosts registered; neither receives the other's credentials |
| [ ] | FW-20 | `APP host switching` | Dedicated switch fingerprint changes active host without authorizing actions | P3 | First host disconnects, selected host reconnects; no unlock/sign from switch finger |
| [ ] | FW-21 | `APP pairing policy` | Second-host registration using enrolled owner finger plus physical confirmation | P3 | Both presence checks required; claimed slots cannot be silently overwritten |
| [ ] | FW-22 | `APP slot commands` | Host-slot status and authorized self/other-slot removal | P3 | Intended slot revoked; other slot and stored keys remain consistent |
| [ ] | FW-23 | `APP/immurok_keystore.c` | Transactional encrypted storage for SSH, OTP and API records | P4 | Power-loss recovery, deletion, host access and confidentiality tests pass |
| [ ] | FW-24 | `APP keystore protocol` | Count/read/write/commit/delete/chunked-result command family | P4 | Bounds, capacity, partial writes and fingerprint gates tested |
| [ ] | FW-25 | `APP SSH crypto` | Device P-256 key generation, import, public-key lookup and signing | P4 | OpenSSH signatures verify; private bytes are never returned by normal reads |
| [ ] | FW-26 | `APP vault capacity` | Support at least 32 SSH, 128 OTP and 50 API records with explicit limits | P4 | Capacity/field-length tests pass; negotiated limits match available storage |
| [ ] | FW-27 | `APP/totp_core.c` | Fingerprint-gated six-digit SHA-1 TOTP using host-supplied time | P4 | RFC 6238 vectors, time bounds and binary-secret encoding verified |
| [ ] | FW-28 | `APP API secrets` | Fingerprint-gated API secret reads and masked metadata responses | P4 | Unauthenticated reads expose no secret; gate binds intended record |
| [ ] | FW-29 | `APP reset / tamper` | Authenticated software reset and deliberate physical long-button reset | P6 | Templates, keys and bonds cleared; interrupted reset resumes safely |
| [ ] | FW-30 | `APP battery / Profile` | Measured raw battery voltage and Battery Service notifications | P6 | ADC calibrated; no placeholder percentage shown as measured charge |
| [ ] | FW-31 | `APP power / GPIO` | Sensor rail gating, touch/button wake and UART sleep configuration | P6 | Hardware supports control; sensor wakes/matches reliably after sleep |
| [ ] | FW-32 | `APP advertising / low power` | Low-battery hysteresis, advertising phases and reconnect after wake | P6 | Measured power and threshold tests; recovery works while charging |
| [ ] | FW-33 | `APP indicators` | Pair/enroll/auth/error/low-battery/reset LED feedback | P6 | Board-specific indicators match documented states |
| [ ] | FW-34 | `APP/tamper.c / factory_test.*` | Case-open response, persistent wipe marker and factory-test behavior | P6 | Requires added hardware; cleanup survives interrupted power and respects factory state |
| [ ] | FW-35 | `tools/devtest / test` | Port device integration and regression suites to ESP32 | P6 | Read-only tests run routinely; reset/destructive tests isolated with recovery |

### app-macos

Sources: [Mac README](https://github.com/immurok/app-macos/blob/main/README.md), [Sources](../app-macos/Sources), [CLI](../app-macos/CLISources), [tests](../app-macos/Tests).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | MAC-01 | `Sources / Package.swift` | Build native menu-bar app and first-run setup wizard | P2 | Local signed test bundle launches; permissions and missing prerequisites explained |
| [ ] | MAC-02 | `Sources/BLEManager.swift` | Discover tinyTouch, pair, verify and reconnect over compatible BLE protocol | P2 | Native app handles cold start, disconnect, sleep/wake and wrong device |
| [ ] | MAC-03 | `Sources transport layer` | Add USB companion transport and transport/device selector | P2 | Same management commands work over USB and BLE without duplicate action |
| [ ] | MAC-04 | `Sources/ImmurokSecurity.swift` | Implement negotiated crypto/freshness client and migrate Keychain identity | P2 | Firmware fixtures verified; existing saved password/key access preserved |
| [ ] | MAC-05 | `Sources device views` | Show connection, host slot, battery, firmware and readiness status | P2 | Capability-driven display; unknown battery distinguished from measured level |
| [ ] | MAC-06 | `Sources/FingerprintView.swift` | Enroll, cancel, delete and rename logical fingerprints | P2 | UI follows ZW111 progress/capacity and receives accurate final state |
| [ ] | MAC-07 | `Sources/AppDelegate.swift` | Native app screen unlock and wake/reconnect retry flow | P2 | Lock-screen unlock over BLE/USB; pending match cannot duplicate typing |
| [ ] | MAC-08 | `Sources/DualHostView.swift` | First/second-host guidance, slot status and authorized unbinding | P3 | Two-host workflow tested with ESP32, including lost-host removal |
| [ ] | MAC-09 | `Sources lock handling` | Confirm and perform device-requested screen lock | P3 | Cancel/confirm behavior correct; no unintended authentication grant |
| [ ] | MAC-10 | `pam / PAMSocketServer.swift` | sudo and system-authorization PAM authentication with password fallback | P4 | Real sudo/authorization positive/negative tests; safe fallback and uninstall |
| [ ] | MAC-11 | `CLISources/PamKeyCommand.swift / pam` | Strong local PAM channel using nonce/HMAC, root-owned verifier and peer checks | P4 | Spoofed/replayed IPC denied; install/remove/status/verify-peer paths tested |
| [ ] | MAC-12 | `Sources/AuthContextDetector.swift / AuthInjector.swift` | Route pure PAM, credential injection and GUI-to-PAM bridge appropriately | P4 | Pending PAM wins; no recognized target means no injection or authorization |
| [ ] | MAC-13 | `AuthInjectionKit / whitelist` | Check running target identity, focused secure field and automation context | P4 | Untrusted app cannot receive passwords by imitating a bundle/name |
| [ ] | MAC-14 | `Sources password targets` | Unlock 1Password/Bitwarden and supported system dialogs with separate saved secrets | P5 | Each target explicitly configured/tested; disabled feature clears intended secret |
| [ ] | MAC-15 | `Sources/Automation* / TargetPicker.swift` | Create/edit/enable custom targets and import/export automation configuration | P5 | Rules and protected export round-trip; target selection remains explicit |
| [ ] | MAC-16 | `Sources/SSHAgentServer.swift / SSHKeyCache.swift` | OpenSSH identity enumeration and biometric signing proxy | P4 | SSH authentication and git signing work with ESP32-held keys |
| [ ] | MAC-17 | `Sources/KeystoreViewModel.swift / key views` | Browse/create/edit/delete SSH, OTP and API records with capacity/progress UI | P5 | Commands operate on intended records and honor all device gates |
| [ ] | MAC-18 | `Sources/SSHKeyImporter.swift` | Import supported existing P-256 SSH keys and show public fingerprints | P5 | Accepted encodings round-trip; unsupported types produce clear errors |
| [ ] | MAC-19 | `Sources OTP import/export` | OTP import/export workflow with explicit secret-export policy | P5 | Supported formats tested; no claim of non-exportability contradicts enabled export |
| [ ] | MAC-20 | `Sources/QuickFillPanel.swift / GlobalHotKey.swift` | Customizable Quick Fill hotkey, search/category filters and OTP/API/public-key insertion | P5 | Correct focused target receives selected value; cancel/timeout and secret lifetime tested |
| [ ] | MAC-21 | `CLISources main / EnvScanner.swift` | imk list/get/run/version/help and imk:// URI resolution | P5 | All categories, unknown references, env-file injection and exit codes tested |
| [ ] | MAC-22 | `Sources/CLISocketServer.swift / AgentGateOverlaySession.swift` | Exact-command approval overlay, reject/timeout and authorized subprocess lifecycle | P5 | Unapproved command never starts; no fallback after rejection; approved context scoped |
| [ ] | MAC-23 | `Sources settings / setup` | Per-feature toggles, login item, Accessibility/Bluetooth and PAM status/repair UI | P2 | Enable/disable and restart behavior consistent; settings survive upgrades |
| [ ] | MAC-24 | `Resources / LocalizationManager.swift` | System-language detection, bundled translations and custom translation overrides | P5 | All available resources load; missing strings fall back safely |
| [ ] | MAC-25 | `Sources logs / About` | Runtime logs, diagnostics, app version and update checks | P6 | Useful diagnosis without passwords/private keys; correct fork release endpoint |
| [ ] | MAC-26 | `FirmwareUpdateKit / update service` | ESP32 package validation, download/cache, progress, cancel and completion UI | P6 | No CH592 image accepted; image-size limits negotiated; interrupted transfer handled |
| [ ] | MAC-27 | `packaging / Tests` | Bundle signing, CLI deployment, updates and native regression suite | P6 | Fresh install/upgrade/uninstall verified; tests pass against ESP32 integration |

### app-win

Sources: [Windows README](https://github.com/immurok/app-win/blob/main/README.md), [implementation plan](../app-win/IMPLEMENTATION_PLAN.md), [projects](../app-win).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | WIN-01 | `ImmurokCommon` | Align command enums, framing, capabilities and authenticated events | P6 | Cross-language fixtures agree; retired/unsupported commands handled explicitly |
| [ ] | WIN-02 | `ImmurokService/Ble / Security` | BLE discovery/pairing/reconnect and protected per-user host credentials | P6 | ESP32 works for intended Windows user; other user/device cannot borrow authority |
| [ ] | WIN-03 | `ImmurokService transport` | Add USB control transport and explicit mode selection | P6 | USB/BLE feature behavior matches without double output |
| [ ] | WIN-04 | `ImmurokService/Ipc` | Hardened named-pipe IPC and per-user client authorization | P6 | Pipe/process/session spoofing and cross-user requests rejected |
| [ ] | WIN-05 | `ImmurokCredentialProvider / System` | Native logon/lock-screen credential delivery through trusted provider | P6 | Real Windows login, lock/unlock and second-account behavior verified |
| [ ] | WIN-06 | `ImmurokClient` | Tray/WPF UI, first-run pairing, fingerprint management and status/settings | P6 | Capability-driven ZW111 management and host slot flows tested |
| [ ] | WIN-07 | `ImmurokService/Ssh / ImmurokConsolePrompt` | SSH agent and visible interactive signing authorization | P6 | OpenSSH/git signing succeeds only for approved operation |
| [ ] | WIN-08 | `ImmurokClient / service key handlers` | SSH/TOTP/API management and supported secret retrieval workflows | P6 | Platform feature matrix reflects tested commands, not preview claims |
| [ ] | WIN-09 | `ImmurokCli` | CLI commands and meaningful service/permission errors | P6 | Every advertised command tested against ESP32 service |
| [ ] | WIN-10 | `ImmurokService/Ota` | ESP32 firmware validation/transfer and progress reporting | P6 | Wrong board/signature/version refused; disconnect handled |
| [ ] | WIN-11 | `packaging / build/install scripts` | Service/provider installation, auto-start, app updates and clean removal | P6 | Install/upgrade/uninstall and upgrade while LogonUI holds provider tested safely |

### app-linux-rs

Sources: [Linux README](https://github.com/immurok/app-linux-rs/blob/main/README.md), [crates](../app-linux-rs/crates), [packaging](../app-linux-rs/packaging).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | LIN-01 | `crates/immurok-common` | Protocol, crypto, paths and authenticated IPC contracts for ESP32 | P6 | Fixtures agree with firmware and other clients |
| [ ] | LIN-02 | `crates/immurok-daemon / scripts` | BlueZ BLE discovery/bonding/reconnect and notify helper integration | P6 | Cold start, suspend/resume and bond recovery tested with ESP32 |
| [ ] | LIN-03 | `crates daemon transport / device rules` | USB backend and user/service device access | P6 | Both transports work under intended service permissions |
| [ ] | LIN-04 | `crates/immurok-daemon / packaging` | Privileged separation, dedicated service user and protected /run sockets | P6 | Wrong UID/socket spoofing denied; service hardening maintained |
| [ ] | LIN-05 | `crates/immurok-client / immurok-cli` | Management CLI and TUI dashboard, keys, PAM, logs and firmware pages | P6 | Every advertised page/command handles supported device capabilities |
| [ ] | LIN-06 | `crates/immurok-gui` | GTK4/libadwaita management UI and auth dialogs | P6 | Pair/enroll/status/settings and prompt cancellation tested |
| [ ] | LIN-07 | `crates/immurok-session-agent` | Bridge authenticated system-service requests to correct desktop session | P6 | Active/user session selection tested; no cross-user credential injection |
| [ ] | LIN-08 | `pam / helper / packaging polkit` | sudo and polkit fingerprint approval, hook install/remove/repair and fallback | P6 | Real positive/negative PAM tests; rejected prompt grants nothing |
| [ ] | LIN-09 | `daemon screen / pam` | GNOME login/lock and KDE lock/session integration with documented limits | P6 | GNOME/GDM and KDE tested separately; SDDM manual requirements explicit |
| [ ] | LIN-10 | `polkit / desktop integrations` | Password-manager system authentication for supported 1Password/Bitwarden/KeePassXC setups | P6 | App-specific setup and Flatpak policy limits documented/tested |
| [ ] | LIN-11 | `daemon ssh_agent` | SSH identities and signing with device-held P-256 keys | P6 | OpenSSH/git signing and gate failures tested |
| [ ] | LIN-12 | `cli keys / daemon keystore` | SSH/OTP/API creation, listing, deletion and authorized retrieval | P6 | Record/capacity boundaries and masked secret reads tested |
| [ ] | LIN-13 | `cli slot / daemon` | Two-host status, owner registration and unbinding user flows | P6 | Real Mac/Linux host switch test passes |
| [ ] | LIN-14 | `cli imk / daemon socket` | imk URI/env-file workflows and visible agent command approval | P6 | Approve/reject/timeout, subprocess exit and context binding tested |
| [ ] | LIN-15 | `cli fw / daemon ota` | Update discovery/cache, progress and board-aware upgrade policy | P6 | ESP32 trust/size/battery rules used; no CH592 bridge image offered |
| [ ] | LIN-16 | `packaging / Makefile` | Debian/Ubuntu, Fedora and Arch builds, service startup and uninstall | P6 | Packages and permission/config migration tested on target distros |
| [ ] | LIN-17 | `docs / tests` | Wayland focus, GNOME/KDE/wlroots limitations and diagnostic regression coverage | P6 | Unsupported compositor behavior stated honestly; suite runs on Linux |

### ota

Sources: [OTA README](https://github.com/immurok/ota/blob/main/README.md), [package generator](../ota/ota-package.py), [v2 tests](../ota/test_imfw_v2.py).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | OTA-01 | `ota-package.py / generate_ota_keys.py` | ESP32 board-tagged v2 package generation and separate fork signing/encryption keys | P6 | Public source contains no private key; package fixtures verify metadata and signatures |
| [ ] | OTA-02 | `firmware OTA service / OTA engines` | Authenticated update entry, chunk erase/write/readback, status and inactivity abort | P6 | Wrong peer, malformed chunks and interrupted sessions handled safely |
| [ ] | OTA-03 | `package validation / device` | ECDSA signature, AES-CTR decrypt, plaintext SHA-256 and security-version floor | P6 | Tampered/wrong-board/wrong-key/downgrade images rejected |
| [ ] | OTA-04 | `jumpapp / iap replacement` | ESP-IDF inactive-slot install, boot confirmation and rollback | P6 | No CH592 startup/layout reused; failed boot and power-loss recovery verified |
| [ ] | OTA-05 | `ota-update.py / app sockets` | Update CLI delegation compatible with Mac/Windows/Linux transports | P6 | Correct app/device selected; progress/errors/exit status tested |
| [ ] | OTA-06 | `build/deploy/migration scripts` | USB recovery flashing and migration from our development firmware | P6 | Recoverable image and board instructions tested; no upstream CH592 bridge reused |
| [ ] | OTA-07 | `release scripts / web metadata` | Publish own firmware manifest/artifacts and self-build trust documentation | P6 | Apps fetch fork artifacts; official and fork signing identities never confused |

### hardware

Sources: [Hardware README](https://github.com/immurok/hardware/blob/main/README.md), [schematic/PCB references](../hardware).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | HW-01 | `ESP32 + ZW111 wiring` | Confirm pin map and document direct-solder assembly for current board | P0 | Photo/wiring review, continuity and voltage checks recorded before final assembly |
| [ ] | HW-02 | `battery / charging` | Verify selected cell protection, polarity and charge-current compatibility | P0 | Board/cell specifications and measured power paths support this assembly |
| [ ] | HW-03 | `sensor rail` | Add suitable sensor power switch while keeping touch wake available | P6 | Rail control and leakage measured; UART cannot back-power sensor |
| [ ] | HW-04 | `battery measurement` | Add ADC divider/protection and calibrated battery reporting | P6 | Full-charge voltage safe at GPIO; firmware agrees with multimeter |
| [ ] | HW-05 | `controls / indicators` | Map physical pair/reset button and LED states without boot-pin conflicts | P6 | Button timing and boot/recovery behavior tested on assembled board |
| [ ] | HW-06 | `tamper / power bypass` | Case switch, always-available tamper power path and interrupted-wipe design | P6 | Opening powered/off device produces intended response; current hardware lacks this |
| [ ] | HW-07 | `power / radio` | Measure sleep, active, charging and BLE reconnect performance | P6 | Publish measured ESP32 runtime; do not inherit CH592 month-long standby claim |
| [ ] | HW-08 | `schematic / pcb / enclosure` | Provide ESP32-specific circuit, board/assembly files and mechanical design | P6 | Editable sources/BOM published; USB data, antenna and sensor access retained |

### imk-skill

Sources: [Agent README](https://github.com/immurok/imk-skill/blob/main/README.md), [guide](../imk-skill/skills/using-imk/SKILL.md).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | AGENT-01 | `skills/using-imk` | Adapt command/SSH/secret approval guide to working tinyTouch CLI | P5 | Examples reference tested commands and describe actual authorization lifetime |
| [ ] | AGENT-02 | `.claude-plugin / convention files` | Claude plugin and Cursor/Windsurf/Continue/Cline/Codex/Aider/Gemini integration instructions | P5 | Local/monorepo paths install correctly; no deleted fork URL used |
| [ ] | AGENT-03 | `skill approval rules` | Document rejection/timeout handling, no bypass and secret-safe command display | P5 | Integration smoke test requests explicit command approval and respects rejection |
| [ ] | AGENT-04 | `README / troubleshooting` | Discovery, install/update, unsupported/headless workflows and troubleshooting | P5 | Fresh consuming project can find CLI and diagnose absent companion/device |

### website / organization profile / archived app-linux

Sources: [Website README](https://github.com/immurok/website/blob/main/README.md), [organization profile](https://github.com/immurok/.github/blob/main/profile/README.md), [archived Linux source](https://github.com/immurok/app-linux).

| Done | ID | Subfolder / area | Feature or deliverable | Implementation order | Completion criterion |
| --- | --- | --- | --- | --- | --- |
| [ ] | WEB-01 | `website (not imported)` | Create tinyTouch product/docs/download pages based on verified capabilities | P6 | Website source resides inside combined repo; no upstream sales/certification claims copied |
| [ ] | WEB-02 | `website/blog-src / theme` | Hugo blog, technical articles, shared layout and local preview/build | P6 | Build succeeds; ESP32 hardware/security claims reflect actual implementation |
| [ ] | WEB-03 | `website/tools / language pages` | Generated product/platform/agent/comparison pages and multilingual content | P6 | Generated output and translations consistent with tested feature matrix |
| [ ] | WEB-04 | `website/functions / firmware manifest` | App download routing, release links and board-specific firmware metadata | P6 | Only our tested releases served; no upstream binaries presented as ESP32 compatible |
| [ ] | WEB-05 | `website/3d / img` | Device renders/3D viewer and relevant visual assets | P6 | Use our board/enclosure and appropriately licensed assets |
| [ ] | WEB-06 | `website tools / metadata` | Sitemap, crawler text, structured data, redirects and 404 behavior | P6 | Build checks pass; metadata contains accurate product facts |
| [ ] | WEB-07 | `website deployment` | Own hosting configuration and optional analytics/feedback endpoints | P6 | Deployment reviewed for our account/domain; no upstream secrets or endpoints reused |
| [ ] | ORG-01 | `.github/profile (not imported)` | Update project/profile repository links and feature overview | P6 | Profile points to combined repo and verified releases; upstream org identity not impersonated |
| [ ] | LEGACY-01 | `archived app-linux (not imported)` | Compare Python BLE/security/daemon/CLI/PAM behavior against maintained Rust app | P6 | Any unique behavior captured under LIN items; no second competing Linux runtime needed |

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

For every future checked item, append or link evidence here: implementation commit/PR, validation command or manual scenario, platform/board, date and limitations. Reopen the checkbox if a regression invalidates its criterion. Update the related platform row separately when that integration passes. Failed/blocked work remains unchecked with its reason in the log; obsolete requirements need an explained replacement, not a fake completion.

| Item | Evidence | Date |
| --- | --- | --- |
| ROOT-01 | Combined repository remote verified; every removed component fork's branch heads were ancestors of combined history before deletion | 2026-10-07 |
| ROOT-02 | [Folder analysis](esp32-folder-analysis.md), commit `7beb65c` | 2026-10-07 |
| ROOT-03 | This document and pinned source inventory; IDs, duplicate names, order values and local links validated before publishing | 2026-10-07 |
| FW-02 | `make` in `firmware/test`: all ten host executables passed; fake storage tests do not validate ESP32 flash/peripherals | 2026-10-07 |
| BASE-01–BASE-05 | User-confirmed prototype measurements and operation from earlier setup; original helper/custom firmware, not the new native companion | Earlier setup; recorded 2026-10-07 |

No new flashing, PAM installation, app installation or hardware change was performed to create this checklist.
