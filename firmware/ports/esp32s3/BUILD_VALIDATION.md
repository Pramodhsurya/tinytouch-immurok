# ESP32 firmware build validation

## Fresh-presence correction — October 8, 2026

Firmware **0.1.37 / esp32-fresh-touch-1** replaces the unsafe polling-only authorization path with sensor-confirmed GPIO absence, a new debounced presence transition, a fresh capture/match and cancellation checks. Sensor UART ownership is held throughout the request; incompatible/duplicate/excess replies are rejected. The native client requires the new `auth_fresh=1` capability in addition to cryptographic proof. **0.1.36 failed the user's no-touch test; its apparent positive results are withdrawn.** Privileged features remain inactive.

Build passed with ESP-IDF 5.3.4; image size **668,672 bytes**, **379,904 bytes** free per OTA slot, SHA-256 `0e8ac73012ec4fa49673adb9a6e26509764f0422e95b67050f20fbebfadd558a`. esptool checksum/validation hash pass. Bypass/recovery remain OFF; partition/NVS layout is unchanged. Portable presence/packet-shape tests pass with UndefinedBehaviorSanitizer; 142 Mac tests pass, including rejection of cryptographic capability without fresh-presence capability and the old HMAC domain. The revised native app is installed. Each test click clears an earlier success, including a request rejected by startup guards. Physical no-touch/wrong-finger/positive/cancel acceptance remains pending.

**Deployment and activation:** the first OTA attempt stopped before upload because live sensor-count authorization returned unavailable. One retry completed upload and verification. After the user's RESET, fresh runtime confirms `firmware=0.1.37`, `build=esp32-fresh-touch-1`, `auth_proof=1`, `auth_fresh=1`, `touch_present=0`, `ota=idle`, sensor ready, five physical templates and one registered host. No enrollment/NVS erase, recovery image or authorization bypass was used. The updated app was reopened for no-touch testing first; this activation alone does not establish biometric acceptance.

**Physical follow-up:** the user reports no-touch rejection but also rejection after touching an enrolled finger when prompted. The native prompt still said “then lift it,” which conflicts with the fresh gate. It is corrected to require holding until the result, and the rebuilt/signature-verified app is installed and reopened for retest. Firmware remains 0.1.37; positive/wrong-finger/cancellation acceptance is pending. Read-only `fingers` diagnosis fails with `reason=parameters`; the device log includes sensor recovery failures. No raw sensor data, pairing keys or saved passwords were collected. A single image-capture attempt can also reject before acquisition is ready; investigate bounded retries if the corrected hold test still rejects.

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
