# Manual Mac acceptance checks

Current device: ESP32-S3 + ZW111, firmware 0.1.41, one enrolled finger in block 2 with four views. Use that finger for positive tests. Keep the removable battery disconnected for the USB tests. Finish/cancel each authentication request before a helper-output test. Report results only, never a typed password or pairing key.

This is a physical test plan, not a second feature checklist. Feature completion and implementation order remain in [FEATURE_CHECKLIST.md](FEATURE_CHECKLIST.md). A test passing does not automatically complete every associated feature.

## Already passed on 0.1.41

| Check | Status | Evidence |
| --- | --- | --- |
| Firmware activation and retained inventory | Completed | Native readback: block 2:4, nine free blocks, 14 settings, one host |
| Unenrolled finger rejected promptly | Completed | User confirmation; validated no-match, 868 ms capture/match duration |
| Enrolled finger held until result verifies | Completed | User completion; two verified proofs, 994 ms capture/match duration |
| Cancel without touch gives no success | Completed | User confirmation |
| A new request verifies after cancellation | Completed | User confirmation |
| USB unplugged during prompt denies without success | Completed | User confirmation |
| A new request verifies after reconnect/refresh | Completed | User confirmation |

## Remaining checks, in order

| Check | Status | Manual steps | Expected result |
| --- | --- | --- | --- |
| 20-second delayed touch | Completed | October 8: user confirms the repeated clock-based test works after the explicit instruction to wait 20 seconds from the touch prompt, then hold the enrolled finger until the result. This is manual acceptance; no new correlated timing trace was collected. The earlier 1,912 ms trace did not establish the delay and is not used as timing evidence. | Fresh proof verified after the requested delay. The interval starts at the touch prompt. |
| Untouched 30-second expiry | Completed | User completed the untouched 35-second observation on 0.1.41. Log confirms `auth_touch_timeout=30034` ms after the prompt. | Request expires, no green success. |
| USB Mac unlock | Completed | User confirms the Mac unlocks once with USB connected, battery disconnected, AUTO transport and native tests finished. Existing helper/device output is tested separately from fresh-proof verification. | Mac unlocks once. |
| Startup wizard completion | Completed | User confirms Finish setup returns to Device without errors, using existing finger 2 and the passed output check. The saved setup serial matches TT-90706911C494. | Returns to Device with setup completed, no inventory error. |
| Window close and reopen | Completed | User confirms normal-window reopening after the requested Dock/menu-bar and quit/relaunch checks, without forcing the completed wizard. | Main window returns normally; completed setup stays completed. |
| Sleep/wake over USB | Completed | User confirms sleep/wake with USB connected, Device refresh and a new enrolled-finger request reconnects/verifies normally. | Correct device reconnects and new proof verifies. No separate output-duplication test was performed in this proof check. |
| Login launch | In progress | Deferred at the user's request on October 8; no enable/disable logout/login result is recorded. When resumed: Features → Launch tinyTouch Native at login. If available, enable it, handle any macOS approval, save work, then log out/in. Later turn it off and repeat. | Launches when enabled and stays off when disabled. If status says App registration unavailable, report that instead; the test is blocked. |
| Bluetooth-only unlock | In progress | October 8 attempt failed: user reports green sensor LED but no password output with USB removed; USB output works again after reconnecting. Device logs confirm two matches followed by approximately six-second helper-response timeouts, then successful USB response/output. Bluetooth helper logs show repeated Bluetooth-unavailable errors; after helper restart, Keychain errors occur. Both app Bluetooth permission switches are enabled. Existing Bluetooth setup reopened for user-handled Keychain approval; repair/retest pending. | Unlock works once over Bluetooth, followed by successful USB reconnection/readback. Battery charging/telemetry acceptance remains separate. |

Native fingerprint enrollment cancellation/rename, UI deletion, settings writes, host-management/second-device flows, installation recovery and permission changes remain separate feature acceptance work. Keys, PAM/sudo, SSH, Bluetooth management and automation execution still require implementation; do not attempt to validate inactive controls as working features.
