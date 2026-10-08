# tinyTouch native Mac setup

The native app currently supports ESP32-S3 + ZW111 USB inventory, fingerprint enrollment/management, device settings, registered-computer inventory/removal, startup checks and bonded Bluetooth identity. The existing tinyTouch password helpers remain responsible for fingerprint output. Keys, PAM/sudo, SSH, automation execution, native host registration and Bluetooth management are still being ported. Refer to [FEATURE_CHECKLIST.md](FEATURE_CHECKLIST.md) for status.

## Build and install on this Mac

Use the existing complete signed tinyTouch CLI directory, including its `_internal` runtime. Packaging preserves its signed bytes and existing credential access. Run from the combined repository:

```sh
cd app-macos
swift test
python3 packaging/build-esp32-preview.py /path/to/signed/tinytouch-directory
python3 packaging/install-esp32-preview.py
open "$HOME/Applications/tinyTouch Native.app"
```

The installer uses your Applications folder and verifies both the native bundle and signed CLI. It retains an existing native app as a hidden `.tinyTouch-previous-…app` backup. Quit the installed native app before installing an update. The original `tinyTouch.app` password helper is separate. No PAM module or login item is automatically installed. This local app is ad hoc signed, not notarized; distribution signing is pending.

The native ESP32 app uses normal Mac application mode: its icon appears in the Dock, and its menu-bar control remains available. Minimize with the yellow window button; click the Dock icon to restore the main window. Closing the window keeps the app running and allows reopening from either place. Menu-bar settings/wizard actions dismiss the popup before presenting the requested window. Initial launch still offers the startup wizard when needed; reopening from the Dock restores settings even when setup was deferred. The app builds, its signature verifies and the installed plist reports `LSUIElement=false`; both helpers remain Running after installation and reopening. The user confirms Dock presence, the window staying open during use, and restoration after minimizing and clicking the Dock icon. Close-window/menu-bar reopening remains a separate acceptance case.

## First launch and settings

Connect USB. The startup wizard checks device status, reads fingerprint inventory, permits enrollment in an empty slot and asks you to perform and confirm a separate fingerprint-output test. Existing enrollment and pairing are preserved. Complete HID-mode setup through the existing tinyTouch helper if no host is configured; native first-host pairing is pending. Once complete, the wizard records the checked device serial. It can be reopened from the menu or Features.

For the separate output check, finish any native authentication request first so the helper has USB again. Lock the Mac with Control–Command–Q, activate the masked password field, and touch the enrolled finger. If it unlocks, return to the wizard and confirm the output check. If it fails, unlock manually and leave that check unconfirmed. Do not share the typed value. This check tests helper keyboard output separately from the fresh-proof test.

October 8 retest: the user reports that fingerprint output did not unlock the Mac. Device events show a successful match followed by `hid_failed`; read-only Bluetooth status reports forced `mode=BLE` while USB is connected, and the Bluetooth helper reports availability errors. Automatic transport selection and a new USB output test are pending. The wizard remains incomplete; a Running helper or a successful fresh proof does not satisfy this step.

Features shows native Bluetooth/Accessibility permission status and whether each password helper is running. The current native management functions do not require Accessibility. Bluetooth identity checking requires native Bluetooth permission; helper permissions are separate. A running helper does not prove its credentials are valid, so test fingerprint output separately.

To start the native app after login, enable **Launch tinyTouch Native at login** in Features. If macOS requires approval, use the displayed Login Items settings button and approve it yourself. Turn the toggle off to unregister the native app. This setting is separate from the password helpers. Login/restart acceptance is still pending.

## Troubleshooting and recovery

### Fresh fingerprint proof test

Use firmware 0.1.37 or newer advertising both `auth_proof=1` and `auth_fresh=1`. Firmware 0.1.36 failed the no-touch test and is blocked by the revised native app. Connect USB and open **Features → Fingerprint authentication test → Test enrolled fingerprint**. Approve the pairing-key read yourself if Keychain asks, keep the sensor clear until the touch prompt, then touch an enrolled finger and hold it until the result. Firmware 0.1.38 adds 30 seconds to touch after the prompt, with bounded processing time afterward; its deployment and physical timing check are pending. A verified result is scoped to this test only; it does not execute a privileged action. **Cancel** closes the request and releases USB to the existing helper. Failed/expired requests require a new test. The native app never reads your saved password. The seven USB acceptance cases pass on 0.1.37. See [AUTH_PROOF_PROTOCOL.md](AUTH_PROOF_PROTOCOL.md).

Wait for a cancellation to complete before starting a new request. A read-only native transport check is available as `swift run tinyTouchProbe --auth-transport-test`; it does not read Keychain or request a fingerprint. Known issue: sensor inventory reads can intermittently report unavailable even though sensor/count status is ready. Two subsequent native management probes returned the expected complete inventory/settings/host state. The app retries only certain read-only inventory failures, at most three attempts; cancellation stops the retry. It displays a finite sensor failure stage if unavailable persists. A failed read never becomes an empty inventory, and enrollment/deletion remain blocked until a successful verified read. Protected writes never repeat automatically.

After the hold instruction was corrected, the user confirmed enrolled-finger success, different-finger rejection and repeated no-touch rejection on 0.1.37. Cancellation without success and a fresh successful request after cancellation are also user-confirmed. After solder repair, the disconnect screenshot shows denial without success, and the user confirms a new successful request after reconnect. The test uses quiet matching, so it does not schedule the usual keyboard-helper sensor LED feedback; the app's verified result is the test outcome.

The original signed CLI stages firmware through its fingerprint-approved `update --file` command. Keep USB connected during upload; after successful staging, press RESET to activate the image (disconnecting USB alone may leave a battery-powered board running). Do not use BOOT or erase NVS to bypass authorization. Verify sensor, template and host inventory after activation.

### Connection and installation

- USB unavailable: reconnect the device, wait for the status check, then refresh. An active sensor operation can be cancelled; reconnect and read inventory afterward to inspect partial enrollment/cleanup.
- Bluetooth unavailable: check the Features permission status and macOS Bluetooth settings. Existing helper pairing supplies the remembered peripheral identity. Native Bluetooth pairing and management are pending.
- Helper stopped/unavailable: check the existing tinyTouch setup and its background-service settings. The native app only reports helper health; it does not reinstall or repair credentials.
- Native update fails: the previous native app is retained during replacement. Quit the native app and restore that backup to `~/Applications/tinyTouch Native.app` if needed. Do not move or overwrite the separate password helper.
- Uninstall this local native preview: turn off its login item, quit it and move `tinyTouch Native.app` to Trash. System PAM uninstall is not part of this preview.

Read-only health diagnosis without opening USB:

```sh
swift run tinyTouchProbe --helper-health
```

Remaining acceptance includes login launch/restart, install-update-restore, permission changes and the full Mac feature gate. Keep incomplete checklist items open until their tests pass.
