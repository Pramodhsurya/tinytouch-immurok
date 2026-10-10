# ESP32 fingerprint approval for sudo

Implemented October 9, 2026 for the native ESP32-S3 + ZW111 app. This is the next major Mac function. The adapter is built and tested locally; administrator installation and real sudo acceptance are pending. MAC-10 and MAC-11 remain **In progress**. Firmware stays at 0.1.42 and fingerprints/host registration are preserved.

## Install and use

1. Open the updated **tinyTouch Native.app** from your Applications folder. Keep USB connected, refresh Device, then open Features.
2. Click **Prepare sudo setup**. Handle any Keychain prompt yourself. This permits a root installer to retrieve a separate sudo channel key once, for five minutes; it does not enable fingerprint sudo yet.
3. Open the locally built **tinyTouch Sudo Setup.pkg** in `app-macos/.build/sudo-installer`. Approve administrator installation and enter your Mac password yourself. Keep the native app open throughout installation.
4. In Features click **Refresh setup status**, then enable **Fingerprint approval for sudo**. The installed module, key identifier, app code hash and PAM entry must match before enabling.
5. Use your existing terminal to request fresh sudo authentication:

   ```sh
   sudo -k
   sudo -v
   ```

   The app opens Features for this request. Keep your finger off until the touch prompt, then hold the enrolled finger until the result. The touch window is 30 seconds. No new terminal is opened. Report only the result, never your password.

The normal sudo timestamp policy still applies: commands within sudo's existing cache window may not invoke PAM. `sudo -k` invalidates that cache for a fresh check. Each PAM authentication invocation receives its own nonce and device proof; the app does not preauthorize later invocations.

## Scope and failure behavior

This adapter accepts only `sudo` and `sudo_local` services over USB. System dialog authorization, login, SSH, Bluetooth PAM and password-manager integrations remain separate work. The existing USB/Bluetooth password-output helpers continue their previous role.

Cancellation, no match, timeout, disconnect, app quit, missing Keychain access, a different app build, a busy USB connection or failed receipt verification return no fingerprint authority. The PAM module returns `PAM_IGNORE`, allowing the remaining configured providers to continue, normally with the Mac password prompt. The app must stay open for fingerprint sudo. Proof testing and earlier successful authentications cannot authorize sudo.

The package adds only this provider line, ahead of the existing sudo providers:

```text
auth sufficient /Library/Security/tinyTouch/pam_tinytouch.so
```

On this Mac, `/private/etc/pam.d/sudo` includes `sudo_local`, so installation creates/updates that optional file. Existing smart-card, Open Directory, account and session providers are preserved. Other service files are not changed. The original target contents are backed up under `/Library/Application Support/tinyTouch/PAM/<uid>.sudo-backup` with root-only access.

## Local trust and storage

The app owns `~/Library/Application Support/tinyTouch/pam.sock`, mode 0600, in a directory that other users cannot write. It checks the root peer identity and socket peer PID before accepting a request. A request binds the account UID, short username, allowed service, peer PID and random 32-byte nonce into the AUTH2 device context. Failed attempts consume their nonce too.

The root module checks the app's live audit-token code signature against the root-owned code hash. This avoids trusting a user-controlled path or PID alone. It accepts only a complete HMAC-SHA256 receipt binding the same nonce, UID, PID, username and service; bare `OK`, preauthorization and cached test results are unsupported. Receipts have a 45-second overall deadline, including USB setup and the touch window. A disconnected requester cancels the active proof.

The independent channel key is stored in the login Keychain as service `tinyTouch.pam-channel-key`, account equal to the UID. A denied read never overwrites an existing key. Only an explicitly prepared root installer can retrieve it through the local socket, after verifying the current app signature. No saved password, device pairing key or fingerprint template is exported by setup. Root holds `<uid>.key` (32 bytes, mode 0600), `<uid>.cdhash` (40 hex characters, mode 0644) and `<uid>.keyid` (public 16-character key identifier, mode 0644) under `/Library/Application Support/tinyTouch/PAM`.

This local preview uses an exact ad hoc app code hash and hardened runtime without debugging/library-injection exceptions. The package builder refuses an unhardened app or those exceptions. Rebuilding/updating the app changes its hash; sudo falls back until the matching installer is run again. Distribution signing and a production update policy remain pending.

## Disable and remove

Turn off **Enable fingerprint approval for sudo** for immediate password fallback. To remove system activation, open **tinyTouch Sudo Remove.pkg** and authorize it yourself. It removes this user's root key/hash/identifier and removes only the exact tinyTouch provider line when no other installed user keys remain. Existing providers are retained. Shared module/helper files, the root backup and the user's Keychain key remain available for review or reinstallation. Real uninstall acceptance is pending.

## Build and evidence

After building the native preview, build the local packages with:

```sh
cd app-macos
python3 packaging/build-tinytouch-sudo.py
```

The builder stages files and runs `pkgbuild`; it does not invoke sudo, read Keychain or install anything. The setup executable refuses non-root execution. The PAM module is universal arm64/x86_64; the native app and Swift setup executable target this Mac. The package includes the app license and upstream credits. The new adapter reuses immurok's retained `pam/hmac_sha256.c`; AUTH2 uses the credited tinyTouch-derived ESP32 firmware.

Validation: **165 Swift tests pass**, including ten new PAM tests for identity/service parsing, cross-language receipts, context changes, bounded replay protection, provider-preserving configuration edits, real Unix socket framing, non-root rejection, cancellation/disconnect, stale-socket recovery and live audit-token code-hash checks. **34 C checks pass**, covering receipts, storage ownership/modes/symlinks/hard links/truncation and the built module's non-root/unsupported-service `PAM_IGNORE` behavior. Installer activation, real enrolled/wrong/no-finger sudo, fallback and uninstall remain unverified until performed on the Mac.
