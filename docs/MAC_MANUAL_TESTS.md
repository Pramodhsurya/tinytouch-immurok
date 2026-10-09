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
| 20-second delayed touch | In progress | Features → Test enrolled fingerprint. Keep finger off until the touch prompt. Use a clock to wait 20 seconds, then hold the enrolled finger until the result. User reports success, but the latest logged positive completes only 1,912 ms after its prompt; that log does not verify the requested delay. | Fresh proof verified after the requested delay. The interval starts at the touch prompt. |
| Untouched 30-second expiry | Completed | User completed the untouched 35-second observation on 0.1.41. Log confirms `auth_touch_timeout=30034` ms after the prompt. | Request expires, no green success. |
| USB Mac unlock | Completed | User confirms the Mac unlocks once with USB connected, battery disconnected, AUTO transport and native tests finished. Existing helper/device output is tested separately from fresh-proof verification. | Mac unlocks once. |
| Startup wizard completion | Completed | User confirms Finish setup returns to Device without errors, using existing finger 2 and the passed output check. The saved setup serial matches TT-90706911C494. | Returns to Device with setup completed, no inventory error. |
| Window close and reopen | Completed | User confirms normal-window reopening after the requested Dock/menu-bar and quit/relaunch checks, without forcing the completed wizard. | Main window returns normally; completed setup stays completed. |
| Sleep/wake over USB | Completed | User confirms sleep/wake with USB connected, Device refresh and a new enrolled-finger request reconnects/verifies normally. | Correct device reconnects and new proof verifies. No separate output-duplication test was performed in this proof check. |
| Login launch | In progress | Features → Launch tinyTouch Native at login. If available, enable it, handle any macOS approval, save work, then log out/in. Later turn it off and repeat. | Launches when enabled and stays off when disabled. If status says App registration unavailable, report that instead; the test is blocked. |
| Bluetooth-only unlock | Yet to be done | Later, after USB/output checks and battery reconnection are arranged, remove USB, confirm tinyTouch is Bluetooth-connected, lock the Mac and touch the enrolled finger. | Unlock works once over Bluetooth, followed by successful USB reconnection/readback. Keep battery disconnected until this stage is explicitly resumed. |

Native fingerprint enrollment cancellation/rename, UI deletion, settings writes, host-management/second-device flows, installation recovery and permission changes remain separate feature acceptance work. Keys, PAM/sudo, SSH, Bluetooth management and automation execution still require implementation; do not attempt to validate inactive controls as working features.
