# USB VBUS sensing for automatic transport selection

The current XIAO ESP32-S3 breadboard has no VBUS signal connected to an ESP32 GPIO. When USB is removed while the battery keeps the board powered, TinyUSB can remain mounted, so AUTO cannot distinguish “USB cable removed” from “USB cable still present.” Explicit BLE selection works around that limitation.

The Seeed XIAO ESP32-S3 exposes its `5V` pin as `VBUS`; Seeed documents that this pin has no voltage while the board is powered only by the battery. The official pin table maps `D0` to `GPIO1`, which is unused by the current sensor wiring (`D1/GPIO2`, `D6/GPIO43` and `D7/GPIO44`). Seeed’s schematic and power documentation are linked from the [XIAO ESP32-S3 hardware resources](https://wiki.seeedstudio.com/xiao_esp32s3_getting_started/).

## Divider wiring

Use a temporary breadboard divider before soldering the final PCB:

```text
XIAO 5V / VBUS ── 100 kΩ ──●── 150 kΩ ── GND
                            │
                         XIAO D0 / GPIO1
```

The 100 kΩ resistor is the upper resistor, from VBUS to the midpoint. The 150 kΩ resistor is the lower resistor, from the midpoint to ground. A 100 nF capacitor from the midpoint to ground is optional for noise filtering. At 5.0 V VBUS the midpoint is about 3.0 V; at the USB 5.25 V upper limit it is about 3.15 V. Never connect the XIAO 5V/VBUS pad directly to GPIO1 or any other ESP32 GPIO.

Keep the existing sensor wiring unchanged: TouchOut remains D1/GPIO2, sensor TX remains D7/GPIO44, and sensor RX remains D6/GPIO43. Disconnect USB and the battery before changing the breadboard. Before powering the board, verify that the divider midpoint is not shorted to 3V3 or GND and that VBUS-to-GND does not beep as a short.

## Verification sequence

1. With USB disconnected and the battery disconnected, verify the divider connections and resistor values.
2. With the battery connected but USB disconnected, measure the XIAO 5V/VBUS pad to GND. It should be approximately 0 V. Do not measure GPIO1 above 3.3 V.
3. Disconnect the battery, connect USB, and measure 5V/VBUS to GND. It should be in the USB VBUS range, normally about 5 V. Disconnect USB again before reconnecting the battery.
4. After the divider is checked, build the VBUS-enabled image with `-DTINYTOUCH_VBUS_SENSE=ON`, upload it through the existing enrolled-finger approval flow, and perform a full USB-only power cycle for activation.
5. USB status should report `vbus_sense=1 vbus_present=1 active=USB` while USB is attached. After unplugging only USB on battery power, a new status read after reconnect should report `vbus_present=0`; a fresh fingerprint request in AUTO should log `usb_vbus=0`, `hid_transport=2` and complete through BLE.

The firmware option is intentionally disabled in the currently running image until this divider is physically wired and checked. The host-side transport-policy test covers USB mounted with VBUS high, USB mounted with VBUS low, suspension, and explicit USB/BLE selections.
