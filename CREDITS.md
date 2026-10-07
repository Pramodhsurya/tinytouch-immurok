# Credits and source provenance

## tinyTouch ESP32 firmware

The ESP32-S3 + ZW111 baseline in [`firmware/ports/esp32s3`](firmware/ports/esp32s3) derives from **tinyTouch by Zimeng Xiong**: [upstream project](https://github.com/zimengxiong/tinytouch), version 0.1.35. It includes local Bluetooth transport, pairing and reconnect work developed for this prototype. These modifications do not erase the upstream contribution or copyright.

The original **MIT License, Copyright (c) 2026 Zimeng Xiong**, is retained in [`firmware/ports/esp32s3/LICENSE`](firmware/ports/esp32s3/LICENSE). Preserve that notice in source and firmware distribution bundles. App branding may say tinyTouch; legal attribution remains intact.

## immurok system and applications

The companion apps, firmware reference, OTA tools, hardware documentation, protocol documentation and agent integration originate from [immurok](https://github.com/immurok). Imported revisions and paths are recorded in [`components.json`](components.json); the feature review revisions are in [`docs/upstream-feature-sources.json`](docs/upstream-feature-sources.json).

Each imported component retains its own license and third-party notices. The Mac, Windows and maintained Linux apps use Apache-2.0; other imported components retain their upstream BSL-1.1 terms. The MIT license on the tinyTouch baseline does not relicense immurok code added during the port.

## Firmware provided by this repository

The usable ESP32 development firmware is built from `firmware/ports/esp32s3`. Original CH592F sources are retained as a porting reference and must not be offered as ESP32 firmware. Build success is separate from device acceptance and full immurok compatibility. A release bundle must identify the target board, source revision, checksum, validation status and applicable notices.
