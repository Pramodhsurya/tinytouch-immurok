# Battery charging and Mac reporting requirements

Requested October 8, 2026. This document expands existing checklist items without duplicating them. These requirements are pending implementation and hardware acceptance; the current native app reports battery measurement unavailable.

The current firmware's Bluetooth Battery Service returns a fixed 100% placeholder. The 100% shown by macOS Bluetooth settings is therefore not a measured battery level or proof of charging completion. HW-04/FW-30 must replace that placeholder with verified telemetry or omit unsupported reporting.

| Checklist item | Required behavior / verification | Status |
| --- | --- | --- |
| HW-02 | Verify that USB-C actually charges the selected battery, including polarity, cell protection, compatible charge current, temperature and charge termination. Exercise both this Mac's USB-C supply and a compatible external USB-C charger. | Yet to be done |
| HW-04 | Provide a safe, calibrated battery measurement path and verify how this assembly can observe charging and full-charge status. Compare readings to the multimeter and actual charger behavior. | Yet to be done |
| FW-30 | Report measured voltage, calibrated percentage and supported charging/full states over USB and Bluetooth. Continue updates when Bluetooth is connected to the Mac while an external charger powers USB-C. | Yet to be done |
| MAC-05 / MAC-23 | Offer a persistent option to show battery percentage in the app. Show charging status when available and clearly identify unknown, unavailable or stale data. | In progress |
| MAC-31 / MAC-23 | Offer a persistent enable/disable option for a macOS charging-complete notification that includes the current measured percentage. | Yet to be done |

## Implementation order

Keep basic charging compatibility and safety verification in HW-02's P0 foundation work. Detailed sensing, percentage calibration and charging notifications depend on HW-04 and FW-30 in the deferred P8 hardware phase. Mac UI work can prepare for those capabilities, but must not claim battery support before actual telemetry and acceptance are available. Mac-first and Windows-before-PCB priorities remain unchanged.

## Charging-complete acceptance

1. Connect the device to this Mac over Bluetooth and start charging from a compatible external USB-C charger. The Mac must receive battery and charging updates without a USB data connection to it.
2. Verify a real charging-to-full transition using the charger-status sensing path. Deliver one notification, for example **“tinyTouch charging complete — 100%”**, using the actual measured percentage rather than a fixed value.
3. Verify the same behavior while charging through this Mac's USB-C connection, without duplicate alerts from the two transports.
4. Disconnect/reconnect Bluetooth after full charge and confirm the same completed cycle does not produce duplicate alerts. A later genuine charging cycle can notify again.
5. Disable notifications and verify no alert is delivered. Verify macOS notification permission denial, permission restoration and persistence of both notification and percentage-display settings after app restart.
6. Confirm stale, unavailable or unknown telemetry and another device's updates cannot trigger a charging-complete alert. Never treat cable attachment or an estimated 100% alone as proof of charger termination.

If the current board does not expose reliable charging/full status, document the limitation and required sensing hardware before implementing completion alerts. Do not invent a charge state from voltage alone.

No charging cycle has been run as part of adding these requirements. The removable battery remains disconnected for the current USB-only authentication tests; reconnecting it and physical charging acceptance are separate manual steps.
