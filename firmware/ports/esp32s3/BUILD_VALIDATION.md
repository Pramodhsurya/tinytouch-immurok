# ESP32 baseline build validation

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
