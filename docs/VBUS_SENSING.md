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

The 100 kΩ resistor is the upper resistor, from VBUS to the midpoint. The 150 kΩ resistor is the lower resistor, from the midpoint to ground. A 100 nF capacitor from the midpoint to ground is optional for noise filtering. At 5.0 V VBUS the midpoint is about 3.0 V; at 5.25 V it is about 3.15 V before resistor tolerance. Measure the actual midpoint before connecting D0. Never connect the XIAO 5V/VBUS pad directly to GPIO1 or any other ESP32 GPIO.

Keep the existing sensor wiring unchanged: TouchOut remains D1/GPIO2, sensor TX remains D7/GPIO44, and sensor RX remains D6/GPIO43. Disconnect USB and the battery before changing the breadboard. Before powering the board, verify that the divider midpoint is not shorted to 3V3 or GND and that VBUS-to-GND does not beep as a short.

## Verification sequence

1. With USB disconnected and the battery disconnected, verify the divider connections and resistor values. Leave the midpoint disconnected from D0 during the first voltage checks.
2. Set the multimeter to DC volts, with the black probe in COM and red probe in the V/Ω socket. Keep the black probe on XIAO GND. With only the battery connected, place the red probe on 5V/VBUS and then the divider midpoint; both should be approximately 0 V.
3. Disconnect the battery and connect only USB. Keep the black probe on GND; measure 5V/VBUS (normally about 5 V), then the midpoint (about 3.0 V). If the midpoint exceeds 3.3 V, disconnect power and correct the divider before connecting D0.
4. Disconnect all power, connect the checked midpoint to D0/GPIO1, then reconnect USB for the protected update. Upload the VBUS-enabled image through the existing enrolled-finger approval flow, and perform a full USB-only power cycle for activation. Select AUTO using the protected transport selector; the current explicit BLE preference persists across updates.
5. In AUTO, USB status should report `vbus_sense=1 vbus_present=1 active=USB` while connected to the Mac. On battery power, unplug only USB and start a fresh fingerprint request; retained numeric events should show `usb_vbus=0`, `hid_transport=2` and completed BLE output. Reconnect USB to read those events: current status should then report `vbus_present=1 active=USB`, and a new request should use USB.

The firmware option is intentionally disabled in the currently running image until this divider is physically wired and checked. The host-side transport-policy test covers USB mounted with VBUS high, USB mounted with VBUS low, suspension, and explicit USB/BLE selections.

This input selects the transport for a new output request. Physical removal during an outstanding request and repeated unplug/replug behavior still require hardware acceptance; the divider alone is not evidence that those cases pass.
