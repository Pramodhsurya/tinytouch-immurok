# ESP32 firmware build validation

## Prompt rejection without slot scanning — October 8, 2026

Firmware **0.1.41 / esp32-fast-reject-1** builds with ESP-IDF 5.3.4. Image size **669,888 bytes**, **378,688 bytes** free per OTA slot; SHA-256 `a67786d0ae25fe5e756605159a5355e5f471e8a3b0c9a178d412e073ce665ae6`. esptool checksum/hash pass; recovery and fingerprint bypass remain OFF, and partition/NVS layout is unchanged.

The user reports that an unenrolled finger does not produce an immediate visible rejection. The 0.1.40 log contains two failed matches taking 10,901/10,911 ms after capture began. Code inspection identifies fallback scanning of individual template slots after a failed SEARCH. The new implementation treats validated SEARCH confirmation 0x09 as final no-match; prompted matching also fails on search transport/response errors without extra slot comparisons. Native AUTH2 returns distinct mismatch/expiry denials, and the app shows a specific fingerprint-not-recognized message. The 30-second untouched window, presence gate, request/HMAC domains, consumption and cancellation remain intact.

All **153 Mac tests** pass, including exact new-error routing and suppression of unknown wire text. Portable C UndefinedBehaviorSanitizer regressions pass for terminal no-match/no fallback, failed response classification, request consumption after mismatch and existing fresh-presence/quiet/timing protections. The new native app builds, verifies and installs with the prior bundle retained. Firmware deployment and live prompt-rejection acceptance are pending. Existing new fingerprint block 2 and host/settings are preserved.

The first protected upload attempt reaches the legacy approval prompt but times out without a new debounced touch (5,597 ms after arming). The same-session log shows a valid ordinary match before that authorization prompt; no image data uploads. The retry holds the foreground lease and adds a readiness Return step plus an explicit APPROVE NOW message at the actual device prompt. Authorization/deadlines remain unchanged.

The retry passes enrolled-finger approval, uploads the complete verified 0.1.41 image, confirms staging verification and exits 0. A full USB-only power cycle is requested for activation with a clean sensor start, keeping the removable battery disconnected. Activation/inventory and live prompt rejection are pending.

**Activation/inventory passed:** after the user confirms the USB-only restart, STATUS verifies 0.1.41 / esp32-fast-reject-1, fresh-proof capabilities, `auth_touch_ms=30000`, idle OTA and sensor ready with four templates. The native management probe verifies only block 2:4, nine free blocks, 14 settings and one registered host. AUTO mode, encrypted Bluetooth, keyboard and helper readiness are retained. Startup/count logs contain no sensor reply errors; one Bluetooth security reconnect is followed by successful encryption and ready readback. The updated native app is reopened for the unenrolled-finger rejection test. Live latency, subsequent positive/no-touch/cancellation/disconnect tests, USB output and wizard acceptance remain pending.

**Prompt mismatch acceptance passed:** the user confirms the unenrolled-finger attempt fails promptly without success. Subsequent logs record a validated `auth_search_no_match`, match failure and consumed denial after **868 ms** of capture/matching. The prompt-to-result interval is 3,942 ms including the pre-touch wait. This passes the prompt wrong-finger case on 0.1.41; positive/delayed-touch and other negative/lifecycle/output/wizard acceptance remain pending.

**Enrolled-finger positive passed:** after the user's denial report and continuous-hold instruction, the user completes the test and asks to continue. Sequential logs confirm two fresh verified results, each with 994 ms capture/match duration. The new enrollment and final no-match behavior coexist with valid positive matching. No new firmware/enrollment change is needed for this retest. Exact delayed-touch timing, cancellation/retry, disconnect/reconnect, untouched expiry, USB output and wizard acceptance remain pending on 0.1.41.

**Cancellation check passed:** the user confirms completion of the cancel-without-touch check with expected cancellation and no success. A new enrolled-finger request after cancellation is pending next. Other lifecycle/timing/output/wizard acceptance remains open.

**Retry after cancellation passed:** the user confirms a new enrolled-finger request verifies after cancellation. USB disconnect/reconnect and timing/output/wizard cases remain pending on 0.1.41.

**USB disconnect denial passed:** the user confirms unplugging USB during the untouched prompt fails without success. Refresh and a new positive request after reconnect are pending next; delayed/no-touch timing and output/wizard acceptance remain open.

**Reconnect positive passed:** the user confirms a newly started enrolled-finger request verifies after reconnect/Device refresh. Current physical positive, wrong-finger, cancel/retry and disconnect/reconnect cases pass. Exact delayed/no-touch timing retests and helper output/wizard acceptance remain open; see [the manual Mac plan](../../../docs/MAC_MANUAL_TESTS.md).

**20-second report not independently timed:** the user reports the requested 20-second delayed-touch test works. The latest available post-restart trace instead shows prompt at 19,218 ms and success at 21,130 ms (995 ms capture/match duration), so it does not independently verify that delay. Positive matching is verified; the manual delayed-touch timing check remains open. Untouched expiry on 0.1.41 is next.

**0.1.41 no-touch expiry passed:** the user completes the untouched observation. Subsequent logs show prompt at 685,371 ms and timeout at 715,405 ms, confirming 30,034 ms without a verified result. Read-only health confirms both helpers Running; Bluetooth status confirms AUTO/encrypted/keyboard/helper readiness. Separate USB lock-screen output is the next human check; helper health does not itself establish password output success.

**USB-connected lock-screen output passed:** the user confirms enrolled-finger output unlocks the Mac once with USB connected, battery disconnected and native tests finished. This is acceptance of the existing helper/device output path, separate from native fresh-proof verification. Wizard completion using the existing fingerprint and confirmed output is pending next; native privileged/unlock adapters and broader transport acceptance remain open.

**Wizard/window lifecycle acceptance passed:** the user completes the startup wizard using fingerprint 2 and the passed output check, returning to Device without inventory errors; the saved setup serial is verified. The user also confirms normal-window reopening/relaunch without forcing the wizard. MAC-01 is complete under its local app/wizard criterion; USB sleep/wake, login, Bluetooth-only output and wider native feature acceptance remain open.

## UART command-boundary correction — October 8, 2026

Firmware **0.1.40 / esp32-uart-sync-1** builds with ESP-IDF 5.3.4. The final image also exposes numeric `reset_reason` in STATUS for restart diagnosis: **669,808 bytes**, **378,768 bytes** free per OTA slot; SHA-256 `8fa48fad0b07ecfd1069e2358c50c43d2c4c5d71ad87b0bf3e32e038c6ef1c5a`. esptool checksum/validation hash pass. Recovery/bypass remain OFF; partition/NVS layout is unchanged. The earlier 669,760-byte build is superseded before deployment.

The 0.1.39 diagnostic readback shows mismatched parameter/count/verification reply lengths consistent with stale cross-command replies. The correction holds the sensor mutex while draining prior transmission and requiring 200 ms without received bytes, with a one-second synchronization limit. No new command is sent if synchronization fails. Fixed-size inline data ACKs must contain exactly the requested length; ACK-only plus data packets is still supported. Missing required data is rejected. Fresh-presence/HMAC gates, 30-second prompt wait and legacy/request deadlines remain unchanged. UndefinedBehaviorSanitizer C regressions pass for stale reply shapes, quiet reset after late bytes, continuous traffic/clock regression and existing fresh-touch boundaries; the unchanged native app has 153 passing tests.

The first OTA approval fails with no match and writes no image data. Current runtime remains 0.1.39, sensor ready, five templates, one host and idle OTA. Subsequent logs contain new boot sequences; user reset/unplugging versus unexpected restart is not yet established. Upload/activation, full retained inventory and physical biometric/output/wizard acceptance remain pending. No recovery image, erase or authorization bypass is used.

The user confirms only touching the sensor during those failed attempts. A subsequent update uses the existing foreground helper lease and standard protected AUTH/OTA commands in one continuous CDC session; it does not read Keychain or change authorization. It fails before a touch prompt and the same-session diagnostic read reports count-shaped replies rejected during sensor verification, with no fresh-authentication stage event. The cause of earlier restart sequences remains unresolved; the last same-session log includes continued uptime around 341 seconds. A cold restart using USB alone, with the removable battery connector disconnected, is requested to clear both MCU/sensor power and separate battery/power conditions before another protected upload. No soldered lead removal is requested.

**USB-only upload passed:** after the user confirms a cold restart with the removable battery disconnected, 0.1.39 reports sensor ready, five templates, one host and synchronized lighting. One authorization reaches the legacy touch prompt but times out without a new touch (`7000` ms configured, `6674` ms elapsed after arming). The next retry opens a visible Terminal and waits for the user to press Return before starting. Enrolled-finger approval succeeds, the final 669,808-byte 0.1.40 image uploads completely, verification succeeds and the uploader exits 0 with staging confirmed. RESET activation, full inventory and physical acceptance are pending. No Keychain read, erase or bypass is used.

**0.1.40 activation verified, live correction not accepted:** after the user's RESET, STATUS confirms `firmware=0.1.40`, `build=esp32-uart-sync-1`, `auth_touch_ms=30000`, `ota=idle`, one host and numeric `reset_reason=1`. The first read reports sensor offline/count unavailable. Logs show incomplete count replies and three-byte count-shaped ACKs rejected by verification. A subsequent native management read reports sensor ready/five templates but fails detailed inventory at parameters. This does not establish reliable UART synchronization or physical authentication. A visible Terminal now requests a full USB-only cold restart with the battery still disconnected, followed by sequential status, inventory and numeric log readback. No authentication or inventory acceptance is credited yet.

**0.1.40 cold-start inventory passed:** following the user's full USB-only cold restart, sequential background status and native management reads pass: sensor ready, five templates, logical slots 1:4 and 10:1, eight free blocks, 14 settings and one registered host. Bluetooth retains AUTO, encryption and keyboard/helper readiness. The current volatile log shows successful startup/count probing without sensor reply errors. Cold-start inventory is verified; warm-reset recovery and physical timing/biometric/output/wizard acceptance remain open. The native app is reopened for testing.

**Fresh inventory created at user request:** protected enrollment of empty block 2 passes all four views and readback. Protected deletion of old blocks 1 and 10 then passes, preserving the complete new fingerprint throughout. Final native management probe verifies sensor ready, four templates, only block 2:4, nine free blocks, 14 settings and one host. No factory reset or host/settings/credential clearing is performed. These live CLI enrollment/deletion checks pass; native timing, UI cancellation/rename, USB output and wizard acceptance remain pending with the newly enrolled finger.

**0.1.40 delayed-touch positive passed:** after new block 2 enrollment, the user confirms fresh proof verifies when waiting 15–20 seconds after the native touch prompt, then holding the new enrolled finger until the result. This verifies the delayed-touch positive case with the current inventory. No-touch 30-second expiry, wrong-finger, cancellation/retry and disconnect/reconnect checks on this firmware remain pending, as do USB output and wizard acceptance.

**0.1.40 no-touch expiry passed:** the user confirms the prompted request expires without success when leaving the sensor untouched for the requested 35-second observation. This is a user-observed expiry check, not a precise measured duration. Wrong-finger, cancellation/retry, disconnect/reconnect, USB output and wizard cases remain pending.

## Sensor failure diagnostics — October 8, 2026

Firmware **0.1.39 / esp32-sensor-diagnostics-1** builds with ESP-IDF 5.3.4. Image size **669,472 bytes**, **379,104 bytes** free per OTA slot; SHA-256 `4aaf0d45b1efb6e2404224ca29feff087234ea6d9062a11a1500822dbc9422b6`. esptool checksum and validation hash pass. Recovery/bypass remain OFF and the partition/NVS layout is unchanged.

It adds whitelisted event names and numeric sensor-command/packet-length/confirmation/duration diagnostics to the existing volatile log. Parameter-read failures and fresh-authentication stages can now be distinguished without logging UART payloads, fingerprints, pairing keys, tags or passwords. The presence gate, packet rejection rules, 30-second prompt window and deadlines are unchanged. The revised native app distinguishes start/handshake/match errors using an exact allowlist; unknown error text is never reflected. All 153 Mac tests pass, including safe error routing. The new native bundle builds and installs with its previous bundle retained. Firmware upload/activation and live diagnosis remain pending; this build is diagnostic work, not a claimed fix or physical acceptance. See [SENSOR_DIAGNOSTICS.md](../../../docs/SENSOR_DIAGNOSTICS.md).

**Deployment and live diagnosis:** initial approval failed with no match; the next attempt reported sensor unavailable before upload. After the user's explicit retry, status returned sensor ready/count five, and fingerprint approval, complete upload and image verification passed. After RESET, runtime confirms 0.1.39 / esp32-sensor-diagnostics-1, sensor ready, five templates and one host. Inventory still fails. The log records parameter confirmation 0 with only two data bytes; the subsequent count request rejects payload length 17 (`fp_reply_shape=7441`, instruction 29), and verification rejects payload length three (`4867`, instruction 19). This demonstrates cross-command stale replies rather than an empty fingerprint database. A UART synchronization/strict inline-length correction is being prepared; physical authentication and wizard acceptance remain unverified.

## Thirty-second native touch window — October 8, 2026

Firmware **0.1.38 / esp32-touch-window-1** builds with ESP-IDF 5.3.4. Image size **668,816 bytes**, **379,760 bytes** free per OTA slot; SHA-256 `a2401c3773023100498c7f05207dc7e384e84b63dd28b907e7db424c9d00d614`. esptool checksum and validation hash pass. Recovery and fingerprint bypass remain OFF; partition/NVS layout and the fresh-proof HMAC domain are unchanged.

AUTH2 now allows 30 seconds for a new debounced touch after confirmed absence arms the gate and emits the prompt. Initial arming remains bounded to seven seconds, capture/match receives a bounded seven-second budget, and the complete request expires at 60 seconds. Legacy AUTH keeps its seven-second gate for the signed CLI's existing read timeout. STATUS advertises `auth_touch_ms=30000`; the native app displays the corresponding instruction and accepts only the known 30/60-second request lifetimes.

All **152 Mac tests** pass. Portable fresh-presence and proof C regressions pass with UndefinedBehaviorSanitizer, covering the new timing boundaries and existing no-touch/cancellation/replay protections. The revised native bundle builds and preserves the embedded signed CLI. Firmware deployment passes as recorded below; delayed-touch/no-touch timing acceptance and USB keyboard-output retest remain pending. The 0.1.37 physical results below are not credited to this build.

The startup-wizard output check failed to unlock the Mac. Device events show a match followed by a helper-response timeout. Read-only status reports forced BLE output with USB connected, while the Bluetooth helper reports availability errors. Fingerprint-approved AUTO selection has now passed; status confirms `mode=AUTO`, encrypted BLE and helper readiness. USB output retest remains pending; no saved password was inspected, no credential access controls changed, and no sensor enrollment was erased.

**Live upload staged:** after the user's readiness confirmation, enrolled-finger authorization passed and the complete 0.1.38 image uploaded and verified. Read-only status confirms the existing 0.1.37 runtime with `ota=staged`, sensor ready, five templates and one host. RESET activation and post-activation inventory/timing tests are pending. The revised signature-verified Mac bundle is installed with the prior bundle retained.

**Activation verified:** after the user's RESET, status confirms `firmware=0.1.38`, `build=esp32-touch-window-1`, `auth_proof=1`, `auth_fresh=1`, `auth_touch_ms=30000`, `ota=idle`, sensor ready, five templates and one host. Separate Bluetooth readback confirms AUTO remains selected, with encrypted connection and helper readiness. Two detailed native inventory probes fail at the parameters stage, so logical slot/settings readback is not credited as passed on this build. Existing enrollment is preserved; no write, recovery or bypass is used. Physical timing and output acceptance remain pending.

**Delayed-touch attempt failed:** the user reports rejection/timeout and supplies the generic rejection screenshot. The Device screen confirms 0.1.38 and the parameters-stage inventory error. Device logs show repeated sensor recovery events. No phase-specific AUTH2 reason or measured failure time is available, so this does not establish a timer defect or a valid mismatch. A no-touch timing check is pending; physical timing acceptance remains unverified.

## Fresh-presence correction — October 8, 2026

Firmware **0.1.37 / esp32-fresh-touch-1** replaces the unsafe polling-only authorization path with sensor-confirmed GPIO absence, a new debounced presence transition, a fresh capture/match and cancellation checks. Sensor UART ownership is held throughout the request; incompatible/duplicate/excess replies are rejected. The native client requires the new `auth_fresh=1` capability in addition to cryptographic proof. **0.1.36 failed the user's no-touch test; its apparent positive results are withdrawn.** Privileged features remain inactive.

Build passed with ESP-IDF 5.3.4; image size **668,672 bytes**, **379,904 bytes** free per OTA slot, SHA-256 `0e8ac73012ec4fa49673adb9a6e26509764f0422e95b67050f20fbebfadd558a`. esptool checksum/validation hash pass. Bypass/recovery remain OFF; partition/NVS layout is unchanged. Portable presence/packet-shape tests pass with UndefinedBehaviorSanitizer; 142 Mac tests pass, including rejection of cryptographic capability without fresh-presence capability and the old HMAC domain. The revised native app is installed. Each test click clears an earlier success, including a request rejected by startup guards. Physical no-touch/wrong-finger/positive/cancel acceptance remains pending.

**Deployment and activation:** the first OTA attempt stopped before upload because live sensor-count authorization returned unavailable. One retry completed upload and verification. After the user's RESET, fresh runtime confirms `firmware=0.1.37`, `build=esp32-fresh-touch-1`, `auth_proof=1`, `auth_fresh=1`, `touch_present=0`, `ota=idle`, sensor ready, five physical templates and one registered host. No enrollment/NVS erase, recovery image or authorization bypass was used. The updated app was reopened for no-touch testing first; this activation alone does not establish biometric acceptance.

**Physical follow-up:** the user reports no-touch rejection but also rejection after touching an enrolled finger when prompted. The native prompt still said “then lift it,” which conflicts with the fresh gate. It is corrected to require holding until the result, and the rebuilt/signature-verified app is installed and reopened for retest. Firmware remains 0.1.37; positive/wrong-finger/cancellation acceptance is pending. Read-only `fingers` diagnosis fails with `reason=parameters`; the device log includes sensor recovery failures. No raw sensor data, pairing keys or saved passwords were collected. A single image-capture attempt can also reject before acquisition is ready; investigate bounded retries if the corrected hold test still rejects.

**Subsequent retest passed:** with the corrected hold instruction, the user confirms enrolled-finger verification and rejection with a different finger. Repeated no-touch rejection was reported earlier. These physical cases pass on the unchanged 0.1.37 image; no capture-retry firmware update was necessary. Quiet prompted matching does not schedule the normal keyboard-helper ring feedback. Cancellation/retry and disconnect cases on this firmware remain pending, and the inventory parameter error remains open.

**Further 0.1.37 acceptance:** the user confirms cancellation without success and a new successful fingerprint request afterward. Two later native management probes pass full retained inventory/settings/host readback (slots 1:4 and 10:1, eight free blocks, 14 settings, one host), establishing that the earlier parameter failure is intermittent. Firmware remains unchanged; the Mac adapter now has bounded retries only for read-only inventory failures and sanitized stage diagnostics. All 148 Mac tests pass. The sensor/UART root cause, physical enrollment/deletion and disconnect/reconnect acceptance remain open.

**Post-repair readback:** following the user's pause for an unsoldered sensor wire and request to resume, USB discovery and native management pass on TT-90706911C494 / 0.1.37. Sensor ready, all five templates, slots 1:4 and 10:1, eight free blocks, 14 settings and the registered host remain intact; both helpers are Running. No new firmware, enrollment or deletion was applied. The specific repaired wire and the cause of earlier parameter failures remain unconfirmed. The pending physical disconnect case must be performed afresh; prior successful readback alone does not complete it.

**Resumed physical checks passed:** the user confirms enrolled-finger verification after repair. Unplugging USB during the touch prompt produces the expected disconnect error, no success and a disabled test button (user screenshot). After reconnect and Device refresh, the user confirms a new fresh proof verifies. These results close the pending USB disconnect/reconnect test cases for 0.1.37; broader BLE/privileged and fingerprint-management acceptance remain open.

Presence regression check from the repository root:

```sh
clang -std=c11 -Wall -Wextra -Werror -fsanitize=undefined \
  -I firmware/ports/esp32s3/main \
  firmware/ports/esp32s3/main/fresh_touch.c \
  firmware/ports/esp32s3/tests/fresh_touch_test.c \
  -o /tmp/tinytouch-fresh-touch-test
/tmp/tinytouch-fresh-touch-test
```

## AUTH2 development build — October 8, 2026

Firmware **0.1.36**, default build identifier **esp32-auth-proof-1**, builds with the same ESP-IDF 5.3.4 toolchain. The **666,912-byte** image fits each existing 1,048,576-byte slot with **381,664 bytes** free. SHA-256: `d9c2a62dfb4f86e1268c5e0e459bc0a861a4709858c4c467491590a483acd5b4`. Fingerprint bypass/recovery, secure boot and flash encryption remain OFF. No partition table or NVS layout changes are introduced.

Portable C AUTH2 known-answer, tamper/replay, no-match, expiration/cancellation and crypto failure tests pass normally and under UndefinedBehaviorSanitizer. Mac authentication/lease fixtures are included in 140 passing Swift tests. AddressSanitizer did not initialize on this Mac and is not recorded as passed. See the [USB proof specification](../../../docs/AUTH_PROOF_PROTOCOL.md). Activation passed as recorded below; native Keychain/sensor proof is pending.

`esptool 4.12.0 image_info` validates the ESP32-S3 image checksum and validation hash. Fresh pre-update runtime remains 0.1.35 / esp32-baseline-1 with sensor ready, five physical templates, logical slots 1 (four views) and 10 (one view), one registered host and 14 settings. No erase, recovery image or authorization bypass was used.

**October 8 deployment:** enrolled-finger OTA authorization passed on the first attempt. The existing signed CLI uploaded all 666,912 bytes, completed verification and exited successfully with the update-ready confirmation. Immediate pre-restart readback showed `ota=staged` on 0.1.35, retaining five templates, one host and 14 settings.

**Activation passed after RESET:** fresh runtime reports firmware `0.1.36`, build `esp32-auth-proof-1`, `auth_proof=1`, `ota=idle`, sensor ready, five templates and one registered host. The initial native management probe passed the new firmware status but failed during a follow-up read; complete logical inventory/settings readback is pending retry. Native Keychain/sensor AUTH2 acceptance is still pending.

**0.1.36 biometric acceptance withdrawn:** the app displayed a valid proof, but subsequent user testing confirmed success without touching, including the original apparent positive test. Cancellation returned no success; this does not establish safe authentication. Replacement firmware validation is recorded above. Native raw PING/status reads pass; repeated management readback returns `ERR FINGER inventory_unavailable` while count stays five. Phase-specific diagnostics are included in 0.1.37. Do not erase enrollment/NVS or relax occupancy checks to hide the failure.

Reproduce the portable Mac C check from the repository root (use a temporary output path):

```sh
clang -std=c11 -Wall -Wextra -Werror -fsanitize=undefined \
  -I firmware/ports/esp32s3/main \
  firmware/ports/esp32s3/main/auth_proof.c \
  firmware/ports/esp32s3/tests/auth_proof_test.c \
  -o /tmp/tinytouch-auth-proof-test
/tmp/tinytouch-auth-proof-test
```

Validated on 2026-10-07 for Seeed XIAO ESP32-S3 + ZW111.

## Build result

- Clean imported baseline build: passed using ESP-IDF **5.3.4**, Xtensa GCC **13.2.0**, target **esp32s3**.
- Rebuilt with fork identity **esp32-baseline-1**: passed. Firmware version remains **0.1.35**; only development build identification changed.
- Dependencies: `espressif/esp_tinyusb 2.2.1`, `espressif/tinyusb 0.19.0~3`, recorded in `dependencies.lock`.
- Application image: **664,400 bytes**, fits each **1,048,576-byte** OTA slot with **384,176 bytes** free.
- Application SHA-256: `5e0ed244ade6603a68298ab7d266f92f6eb3e1944d4fd2dac25b514391104a3e`.
- `esptool image_info`: ESP32-S3 image parsing, checksum and validation hash passed. Expected build identifier present in image.
- Fingerprint authorization bypass **OFF**; reset/recovery build **OFF**. Secure boot and flash encryption remain disabled in this development baseline. This is not a production signed-OTA release.

From the combined repository root, with ESP-IDF 5.3.4 activated:

```sh
idf.py -C firmware/ports/esp32s3 build
python -m esptool --chip esp32s3 image_info firmware/ports/esp32s3/build/tiny_touch_unified.bin
```

Generated artifacts and managed components remain ignored. The local build used the existing ESP-IDF toolchain; no new system toolchain or companion app was installed.

## Connected device and deployment

Before upload, USB enumeration and the existing control protocol confirmed `tinyTouch`, serial `TT-90706911C494`, `/dev/cu.usbmodem1103`. Runtime: firmware `0.1.35`, build `ble-reconnect-4`, sensor `ready`, fingerprints `1`, hosts `1`, mode `hid`, OTA `idle`.

The existing private pre-Bluetooth USB recovery backup passed checksum and restricted-permission checks. It is not committed or distributed and is not a fresh backup of the current BLE bond state.

**Upload staged successfully:** after two unsuccessful fingerprint AUTH attempts that wrote no image data, the visible Terminal retry passed the enrolled-finger gate, uploaded the complete image, and received `OK OTA STAGED` / update-ready confirmation. After the user power-cycled and reconnected the board, runtime confirmed `esp32-baseline-1`, sensor `ready`, fingerprints `1`, hosts `1`, mode `hid`, OTA `idle`. USB `PING` returned `PONG 6`; BLE status confirmed connected/encrypted/keyboard/helper/ready all `1`. The visible Terminal test then passed live enrolled-finger authentication with `EVENT TOUCH` and `OK AUTH`. Preserve enrollment, bonds and NVS; do not use reset/recovery firmware to bypass authorization.

A credited application image, manifest, checksums and ZIP bundle are uploaded as a **development prerelease**: [ESP32 baseline 1](https://github.com/Pramodhsurya/tinytouch-immurok/releases). This is a baseline development release, not completed native Mac feature parity.

The update uses the existing application OTA path and preserves the partition layout. Current slots: NVS `0x9000/0x6000`, app0 `0x10000/0x100000`, app1 `0x110000/0x100000`, OTA metadata `0x210000/0x2000`, recovery data `0x212000/0x1000`. USB OTA stages the inactive app and changes the boot selection; it does not write a replacement partition table or NVS.

## Device acceptance and remaining tests

- Passed: device reports `esp32-baseline-1` after activation and retains one enrolled finger and one host.
- Passed: sensor ready, USB control/PING and live enrolled-finger `OK AUTH`.
- Passed: BLE bond, encrypted reconnect and helper readiness. A new battery-powered Mac lock-screen unlock test remains pending.
- Native immurok-derived Mac integration remains future work: this baseline still uses the tinyTouch protocol.

## Credits

Derived from **tinyTouch by Zimeng Xiong**, with local Bluetooth additions. Preserve [MIT license](LICENSE) and [repository credits](../../../CREDITS.md) in firmware bundles. CH592F code elsewhere is upstream reference, not an ESP32 image.
