# Native Mac source build and test baseline

Validated 2026-10-07, before ESP32 protocol integration.

## Result

`swift test` in `app-macos` built the native `immurokApp` and `imk` targets and passed **97 tests with zero failures**. Toolchain: Apple Swift **6.2**, arm64 macOS, Swift package minimum macOS 13.

Test targets: `FirmwareUpdateKitTests`, `AuthInjectionKitTests`, `LocalizationTests`, `PamMacTests`. These are the imported host-side tests, not a live ESP32 acceptance suite. No app was installed or launched, PAM/login configuration changed, Keychain migration performed, or Windows/Linux test run.

The build emits an existing unused-variable warning in `Sources/FirmwareUpdateService.swift` (`code` in the error case). It does not prevent the build/tests; source behavior was not changed just to silence it.

## Next native-app work

- Implement an ESP32-compatible protocol/capabilities adapter and USB transport.
- Preserve pairing/Keychain identity and prevent duplicate credential output during migration from the original helper.
- Apply tinyTouch branding and standalone bundle packaging with upstream credits retained.
- Validate every supported Mac feature and failure path on this device; imported tests alone do not complete native-app parity.

Track these requirements in [the checklist](FEATURE_CHECKLIST.md). Windows follows Mac acceptance; Linux is on hold; PCB/additional hardware follow Windows.
