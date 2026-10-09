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
| 20-second delayed touch | Yet to be done | Features → Test enrolled fingerprint. Keep finger off until the touch prompt. Use a clock to wait 20 seconds, then hold the enrolled finger until the result. | Fresh proof verified. The interval starts at the touch prompt. |
| Untouched 30-second expiry | Yet to be done | Start another test. After the touch prompt, leave the sensor untouched for 35 seconds. | Request expires, no green success. Previously passed on 0.1.40; repeat on 0.1.41. |
| USB Mac unlock | Yet to be done | Finish all native tests. Keep USB connected. Press Control–Command–Q, activate the masked password field and touch the enrolled finger. | Mac unlocks once. If it fails, unlock manually with your own password and report only the result. |
| Startup wizard completion | Yet to be done | Features → Run startup wizard. Continue with existing finger 2; do not enroll again. Confirm the output checkbox only after the separate output test passes, then Finish setup. | Returns to Device with setup completed, no inventory error. |
| Window close and reopen | Yet to be done | Close the main window with its red button. Reopen from the Dock, then close and reopen from the tinyTouch menu-bar control. Quit/relaunch the app once. | The main window returns normally; completed setup does not force the wizard to reopen. Minimize/Dock restoration passed previously. |
| Sleep/wake over USB | Yet to be done | With USB connected and no native request active, sleep/wake the Mac. Refresh Device and run a new enrolled-finger test. | Correct device reconnects and new proof verifies; no duplicated output. |
| Login launch | Yet to be done | Features → Launch tinyTouch Native at login. If available, enable it, handle any macOS approval, then log out/in. Later turn it off and repeat. | Launches when enabled and stays off when disabled. If status says App registration unavailable, report that instead; the test is blocked. |
| Bluetooth-only unlock | Yet to be done | Later, after USB/output checks and battery reconnection are arranged, remove USB, confirm tinyTouch is Bluetooth-connected, lock the Mac and touch the enrolled finger. | Unlock works once over Bluetooth, followed by successful USB reconnection/readback. Keep battery disconnected until this stage is explicitly resumed. |

Native fingerprint enrollment cancellation/rename, UI deletion, settings writes, host-management/second-device flows, installation recovery and permission changes remain separate feature acceptance work. Keys, PAM/sudo, SSH, Bluetooth management and automation execution still require implementation; do not attempt to validate inactive controls as working features.
