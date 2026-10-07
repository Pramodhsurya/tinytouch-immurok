# ESP32-S3 + ZW111 port baseline

This directory imports the existing tinyTouch ESP-IDF implementation used by the working XIAO ESP32-S3 + ZW111 prototype, derived from **tinyTouch by Zimeng Xiong**. Its source baseline is `ble-reconnect-4`; the fork's development build ID is `esp32-baseline-1`. Battery-powered Bluetooth lock-screen typing was verified on the original prototype. The imported project was independently built successfully with ESP-IDF 5.3.4 on 2026-10-07. See [build validation](BUILD_VALIDATION.md) for image details and device deployment status.

**This is not an immurok-protocol-compatible release.** The stock immurok apps cannot communicate with this baseline yet. The original CH592F implementation remains elsewhere in this repository for feature-by-feature porting.

## Hardware

| Sensor pin | Wire in prototype | Signal | XIAO connection |
|---|---|---|---|
| 1 | Green | VTouch | 3V3 |
| 2 | Blue | TouchOut | D1 / GPIO2 |
| 3 | White | VCC | 3V3 |
| 4 | Yellow | Sensor TX | D7 / GPIO44, MCU RX |
| 5 | Black | Sensor RX | D6 / GPIO43, MCU TX |
| 6 | Red | GND | GND |

Battery wires are separate: red to BAT+, black to BAT-. USB data, BLE HID, and sensor matching must remain supported during the port.

## Build

Use ESP-IDF 5.3.4, target esp32s3. From the combined repository root, with ESP-IDF activated:

```sh
idf.py -C firmware/ports/esp32s3 build
```

Component dependencies are declared in `main/idf_component.yml`; generated components and build output are ignored. Never copy local flash backups, pairing keys, sensor templates, or signing keys into this repository. This development baseline does not establish a production secure-boot or signed-OTA release process.

## Port boundaries

Replace WCH TMOS scheduling, UART/GPIO, BLE stack and flash APIs with ESP-IDF/FreeRTOS/NimBLE/NVS interfaces. Implement the immurok binary GATT protocol, owner-approved pairing and command authorization before claiming compatibility with any companion app. Keep the existing USB transport and nonce/replay protections while migrating.

Future sensor power gating, battery sensing and tamper/button functionality require validated board wiring. The current breadboard does not provide every peripheral from immurok's PCB. Do not claim its standby current on this prototype.

## License and origin

The imported files in this directory originate from [zimengxiong/tinytouch](https://github.com/zimengxiong/tinytouch), upstream v0.1.35, with local Bluetooth additions. Their MIT notice is retained in this directory's `LICENSE`. It does not relicense immurok code elsewhere in this repository. Adapted immurok firmware retains the root BSL-1.1 obligations. Keep provenance and notices when integrating the implementations.
