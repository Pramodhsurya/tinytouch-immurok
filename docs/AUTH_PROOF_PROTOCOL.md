# ESP32 request-bound fingerprint proof

AUTH2 was introduced October 8, 2026 in firmware 0.1.36. **That build failed physical no-touch testing: a valid cryptographic result was displayed without a new finger touch. Its biometric acceptance is withdrawn.** Replacement firmware **0.1.37 / esp32-fresh-touch-1** adds a fresh-presence gate and the native preview requires that capability. The user has confirmed enrolled-finger success, different-finger rejection and repeated no-touch rejection after the native hold instruction was corrected. Cancellation/retry and disconnect acceptance on this replacement remain pending. This USB protocol-6 extension uses an existing tinyTouch HID host key; upstream ECDH pairing, encrypted management, BLE proofs, PAM and device-held SSH/vault operations remain pending.

## Capability and prerequisites

`STATUS` must report both `auth_proof=1` and `auth_fresh=1`; absent/malformed freshness support cannot enable the native test, even when cryptographic proof is supported. Version strings do not establish support. Recovery and fingerprint-bypass builds report `0` and reject `AUTH2 BEGIN`. The device requires a registered host with its existing 32-byte key, connected CDC, an enrolled fingerprint and a functioning TouchOut connection to GPIO2. First-setup authorization cannot produce this proof.

The native test explicitly reads only the pairing key from macOS Keychain service `tinyTouch-pairing`, account equal to the USB serial. The user handles any Keychain prompt. No saved password is read, exported, logged or migrated; access controls remain unchanged. Host ID is the first 16 lowercase hex characters of SHA-256(key), matching the existing helper registration.

## Wire exchange

ASCII lines end with LF. Hex fields are lowercase: host ID is 16 characters; nonces, context digest and tags are 64. USB serial is `TT-` followed by 12 uppercase hex characters.

```text
AUTH2 BEGIN <host-id> <client-nonce> <context-sha256>
OK AUTH2 CHALLENGE nonce=<device-nonce> mac=<challenge-tag> ttl_ms=30000
AUTH2 PROVE <client-nonce> <host-tag>
EVENT TOUCH
OK AUTH2 MATCH nonce=<client-nonce> context=<context-sha256> mac=<match-tag>
```

Each tag is HMAC-SHA256 with the registered 32-byte key over these ASCII bytes, without a trailing newline:

```text
tinyTouch-auth2-fresh-v1|<role>|<serial>|<host-id>|<client-nonce>|<device-nonce>|<context-sha256>
```

Distinct roles are `challenge`, `host` and `match`. The Mac verifies the challenge before sending its host tag; the device verifies that tag before asking for a fingerprint. The match tag is emitted only after an enrolled-finger match. Test context is SHA-256 of `native-auth-test-v1|<serial>`; future privileged integrations must bind their exact operation and consume the result at their trusted verifier.

Only one device request may be pending. Its lifetime is 30 seconds on a monotonic clock, including the sensor's seven-second touch window. Expiry, backward clock, invalid host proof, no match, abort or CDC disconnect clears pending device key/request state. A removed or rekeyed host cannot finish a pending request. `AUTH2 ABORT` clears state and returns `OK AUTH2 ABORT`. Errors return `ERR AUTH2 ...` and never grant authority. A failed BEGIN does not replace a live request. Client parsing rejects duplicate/unknown fields, malformed tags, changed nonces/context, role reflection and expired responses; every verification attempt consumes the client request.

Successful AUTH2 does not open the legacy two-minute configuration authorization window or set the native app's legacy global verification flag. The preview reports test success only. Legacy `AUTH` and USB/BLE keyboard helpers remain compatible.

The fresh HMAC domain deliberately differs from 0.1.36's `tinyTouch-auth2-v1`. Spoofing an unprotected STATUS capability cannot make the new client accept the old device's challenge; old host proof tags are also rejected by the new firmware. Both C and Swift fixtures cover this downgrade rejection. Pairing keys and existing keyboard-event formats remain unchanged.

## Entropy, ownership and limits

The Mac uses `SecRandomCopyBytes` for its 32-byte nonce. Firmware uses `esp_fill_random` only while the Bluetooth controller is enabled; otherwise it fails closed. See the RF entropy prerequisite in [Espressif's ESP-IDF 5.3.4 RNG documentation](https://docs.espressif.com/projects/esp-idf/en/v5.3.4/esp32s3/api-reference/system/random.html).

The native client acquires the existing foreground-helper flock lease and waits for its matching acknowledgement before opening CDC. It closes CDC before releasing the lease. Cancellation, deadlines and bounded output are enforced. Existing helpers retain password-output ownership and resume afterward; launchd registration is unchanged.

The Mac explicitly sets serial minimum-read/timer values, treats an empty nonblocking read as a retry within the same deadline, and drops DTR/releases its own exclusive port flag before closing. Setup/read/write errors identify the failing stage without exposing request tags or keys. `tinyTouchProbe --auth-transport-test` checks native PING/status and releases USB without reading Keychain or prompting a finger. Firmware 0.1.37 checks cancellation between sensor operations; latency is bounded by an in-flight UART operation rather than a guarantee of instantaneous interruption.

The fresh gate holds the sensor UART mutex throughout authorization, quarantines prior responses, requires GPIO absence for 250 ms with an acknowledged sensor no-finger response, then a new GPIO presence held for 80 ms. Only then may a new image capture and match be attempted. Presence must still be asserted when the match finishes, and the seven-second monotonic window must remain valid. A held/stuck-high pin, cached match without a new touch, missing capture, mismatch, cancellation or clock regression cannot complete the gate. Missing/unreliable presence fails closed; there is no polling-only fallback. UART reply shapes reject stale SEARCH/MATCH/COUNT payloads as capture ACKs, duplicate ACKs, excess data and data before an ACK; data is not silently truncated to a different command shape.

HMAC authenticates possession of a shared key; a compromised host with that key can forge tags. It provides no confidentiality, new host commissioning, trusted local IPC, physical anti-tamper or production firmware trust. Secure boot and flash encryption remain disabled in this development image. Legacy configuration authorization remains separately available; AUTH2 does not strengthen every legacy command. BLE proofs and the privileged verifier must be implemented and tested before FW-14/FW-15/MAC-04 can be completed.

## Validation

C and Swift share public known-answer vectors independently checked with Python HMAC-SHA256. C tests cover all tag-character changes, replay, missing host proof, no match, expiry/backward clocks, cancellation, state clearing and RNG/HMAC failure. Normal and UndefinedBehaviorSanitizer runs pass. AddressSanitizer hung in loader/malloc shadow initialization before the test entry point on this Mac; it is not recorded as passed.

The Mac suite passes **142 tests**, including proof parsing/domain separation, rejection of the old HMAC domain, request consumption, context/serial binding, capability negotiation, rejection of 0.1.36 without `auth_fresh`, and lease exclusion/cleanup/acknowledgement. Portable C presence/packet-shape tests pass under UndefinedBehaviorSanitizer, including cached matches without a touch, stuck-high/held-finger input, missing sensor absence, debounce, wrong match, lift, cancellation, expiry and clock regression. Cryptographic AUTH2 vectors remain separately tested. Live 0.1.36 proof acceptance was withdrawn after the user's no-touch report; a cancellation check returned no success. Firmware 0.1.37 uploaded, verified and activated after RESET, reporting both capabilities, no finger present, five templates and one host. Physical positive/no-touch/wrong-finger/cancel tests remain pending. Each new native test click clears any earlier success before startup guards. Full logical sensor inventory previously returned unavailable and remains open for phase-specific diagnosis; count still reports five. Privileged/BLE acceptance is separate work.

Derived from tinyTouch by Zimeng Xiong and the immurok companion structure. Preserve [credits](../CREDITS.md) and component licenses.

**Latest physical evidence, October 8:** the corrected native prompt requires holding until the final result. The user confirms that the enrolled finger succeeds and a different finger rejects; preceding repeated no-touch attempts also reject. The earlier 0.1.37 positive rejection was resolved by this instruction correction, without changing firmware. The quiet matcher does not schedule keyboard-helper LED feedback. Repeat cancellation/retry and disconnect cases on 0.1.37 before closing those acceptance gates; the cancellation evidence from 0.1.36 is not credited to the replacement.
