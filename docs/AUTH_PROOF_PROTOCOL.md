# ESP32 request-bound fingerprint proof

AUTH2 was introduced October 8, 2026 in firmware 0.1.36. **That build failed physical no-touch testing: a valid cryptographic result was displayed without a new finger touch. Its biometric acceptance is withdrawn.** Replacement firmware **0.1.37 / esp32-fresh-touch-1** adds a fresh-presence gate and the native preview requires that capability. Enrolled-finger success, different-finger and no-touch rejection, cancellation, a new request after cancellation, disconnect denial and a new request after reconnect all pass with user confirmation. This USB protocol-6 extension uses an existing tinyTouch HID host key; upstream ECDH pairing, encrypted management, BLE proofs, PAM and device-held SSH/vault operations remain pending.

## Capability and prerequisites

`STATUS` must report both `auth_proof=1` and `auth_fresh=1`; absent/malformed freshness support cannot enable the native test, even when cryptographic proof is supported. Version strings do not establish support. Recovery and fingerprint-bypass builds report `0` and reject `AUTH2 BEGIN`. The device requires a registered host with its existing 32-byte key, connected CDC, an enrolled fingerprint and a functioning TouchOut connection to GPIO2. First-setup authorization cannot produce this proof.

The native test explicitly reads only the pairing key from macOS Keychain service `tinyTouch-pairing`, account equal to the USB serial. The user handles any Keychain prompt. No saved password is read, exported, logged or migrated; access controls remain unchanged. Host ID is the first 16 lowercase hex characters of SHA-256(key), matching the existing helper registration.

## Wire exchange

ASCII lines end with LF. Hex fields are lowercase: host ID is 16 characters; nonces, context digest and tags are 64. USB serial is `TT-` followed by 12 uppercase hex characters.

```text
AUTH2 BEGIN <host-id> <client-nonce> <context-sha256>
OK AUTH2 CHALLENGE nonce=<device-nonce> mac=<challenge-tag> ttl_ms=60000
AUTH2 PROVE <client-nonce> <host-tag>
EVENT TOUCH
OK AUTH2 MATCH nonce=<client-nonce> context=<context-sha256> mac=<match-tag>
```

Each tag is HMAC-SHA256 with the registered 32-byte key over these ASCII bytes, without a trailing newline:

```text
tinyTouch-auth2-fresh-v1|<role>|<serial>|<host-id>|<client-nonce>|<device-nonce>|<context-sha256>
```

Distinct roles are `challenge`, `host` and `match`. The Mac verifies the challenge before sending its host tag; the device verifies that tag before asking for a fingerprint. The match tag is emitted only after an enrolled-finger match. Test context is SHA-256 of `native-auth-test-v1|<serial>`; future privileged integrations must bind their exact operation and consume the result at their trusted verifier.

Only one device request may be pending. Firmware 0.1.38 gives AUTH2 a 60-second monotonic lifetime. Sensor absence must arm within seven seconds; the 30-second touch window starts when the gate arms and emits the touch prompt. A debounced touch within that window receives a bounded seven-second capture/match budget, allowing a late touch to finish. The overall request deadline still applies. Legacy `AUTH` retains its seven-second gate for existing CLI compatibility. Firmware 0.1.37 advertises a 30-second request lifetime and has the original seven-second gate. The Mac accepts only the exact advertised lifetime values `30000` or `60000` and applies the corresponding local deadline. `STATUS auth_touch_ms=30000` selects the explicit 30-second prompt on the new build; absent metadata keeps the older prompt.

Expiry, backward clock, invalid host proof, no match, abort or CDC disconnect clears pending device key/request state. A removed or rekeyed host cannot finish a pending request. `AUTH2 ABORT` clears state and returns `OK AUTH2 ABORT`. Errors return `ERR AUTH2 ...` and never grant authority. A failed BEGIN does not replace a live request. Client parsing rejects duplicate/unknown fields, malformed tags, changed nonces/context, role reflection and expired responses; every verification attempt consumes the client request.

Successful AUTH2 does not open the legacy two-minute configuration authorization window or set the native app's legacy global verification flag. The preview reports test success only. Legacy `AUTH` and USB/BLE keyboard helpers remain compatible.

The fresh HMAC domain deliberately differs from 0.1.36's `tinyTouch-auth2-v1`. Spoofing an unprotected STATUS capability cannot make the new client accept the old device's challenge; old host proof tags are also rejected by the new firmware. Both C and Swift fixtures cover this downgrade rejection. Pairing keys and existing keyboard-event formats remain unchanged.

## Entropy, ownership and limits

The Mac uses `SecRandomCopyBytes` for its 32-byte nonce. Firmware uses `esp_fill_random` only while the Bluetooth controller is enabled; otherwise it fails closed. See the RF entropy prerequisite in [Espressif's ESP-IDF 5.3.4 RNG documentation](https://docs.espressif.com/projects/esp-idf/en/v5.3.4/esp32s3/api-reference/system/random.html).

The native client acquires the existing foreground-helper flock lease and waits for its matching acknowledgement before opening CDC. It closes CDC before releasing the lease. Cancellation, deadlines and bounded output are enforced. Existing helpers retain password-output ownership and resume afterward; launchd registration is unchanged.

The Mac explicitly sets serial minimum-read/timer values, treats an empty nonblocking read as a retry within the same deadline, and drops DTR/releases its own exclusive port flag before closing. Setup/read/write errors identify the failing stage without exposing request tags or keys. `tinyTouchProbe --auth-transport-test` checks native PING/status and releases USB without reading Keychain or prompting a finger. Firmware 0.1.37 checks cancellation between sensor operations; latency is bounded by an in-flight UART operation rather than a guarantee of instantaneous interruption.

The fresh gate holds the sensor UART mutex throughout authorization, quarantines prior responses, requires GPIO absence for 250 ms with an acknowledged sensor no-finger response, then a new GPIO presence held for 80 ms. Only then may a new image capture and match be attempted. Presence must still be asserted when the match finishes, and the applicable capture/request deadlines must remain valid. A held/stuck-high pin, cached match without a new touch, missing capture, mismatch, cancellation or clock regression cannot complete the gate. Missing/unreliable presence fails closed; there is no polling-only fallback. UART reply shapes reject stale SEARCH/MATCH/COUNT payloads as capture ACKs, duplicate ACKs, excess data and data before an ACK; data is not silently truncated to a different command shape.

HMAC authenticates possession of a shared key; a compromised host with that key can forge tags. It provides no confidentiality, new host commissioning, trusted local IPC, physical anti-tamper or production firmware trust. Secure boot and flash encryption remain disabled in this development image. Legacy configuration authorization remains separately available; AUTH2 does not strengthen every legacy command. BLE proofs and the privileged verifier must be implemented and tested before FW-14/FW-15/MAC-04 can be completed.

## Validation

C and Swift share public known-answer vectors independently checked with Python HMAC-SHA256. C tests cover all tag-character changes, replay, missing host proof, no match, expiry/backward clocks, cancellation, state clearing and RNG/HMAC failure. Normal and UndefinedBehaviorSanitizer runs pass. AddressSanitizer hung in loader/malloc shadow initialization before the test entry point on this Mac; it is not recorded as passed.

The Mac suite passes **152 tests**, including proof parsing/domain separation, rejection of the old HMAC domain, request consumption, context/serial binding, capability negotiation, rejection of 0.1.36 without `auth_fresh`, lease exclusion/cleanup/acknowledgement and safe inventory retries. The new cases cover both request lifetimes, expiry and bounded canonical touch-window metadata. Portable C presence/packet-shape tests pass under UndefinedBehaviorSanitizer, including cached matches without a touch, stuck-high/held-finger input, missing sensor absence, debounce, wrong match, lift, cancellation, expiry and clock regression. New timing cases cover late touch, the 30-second boundary, capture completion after that boundary, capture expiry, no touch, initial arming expiry and unchanged legacy timing. Cryptographic AUTH2 vectors remain separately tested. Live 0.1.36 proof acceptance was withdrawn after the user's no-touch report. Firmware 0.1.37 uploaded, verified and activated after RESET, retaining five templates and one host. Each new native test click clears any earlier success before startup guards. Full logical inventory/settings/host reads now pass, including after the user's solder repair; the earlier parameter error is intermittent and its underlying cause remains unconfirmed. Firmware 0.1.38 builds successfully; deployment and physical timing acceptance are pending. Privileged/BLE acceptance is separate work.

Physical USB acceptance on 0.1.37 (user-operated tests, October 8):

**0.1.38 activation update:** enrolled-finger OTA authorization, upload and verification pass. After RESET, runtime confirms the new version/build, both authentication capabilities, `auth_touch_ms=30000`, idle OTA, sensor ready, five templates and one host. AUTO transport selection persists. Detailed native inventory readback fails at the parameters stage in two probes; the earlier successful logical inventory is not credited as post-activation readback. Physical delayed-touch/no-touch timing acceptance remains pending, separately from the 0.1.37 cases below.

| Case | Status | Observed result |
| --- | --- | --- |
| Enrolled finger after prompt | Passed | Fresh proof verified; repeated after solder repair |
| Different finger | Passed | Rejected |
| Repeated no-touch attempts | Passed | Rejected, no green success |
| Cancel without touching | Passed | Authentication cancelled, no success |
| New request after cancel | Passed | Fresh proof verified |
| Unplug USB during prompt | Passed | Screenshot shows USB disconnected; no success, test disabled |
| New request after reconnect | Passed | User confirms fresh proof verified after Device refresh |

Derived from tinyTouch by Zimeng Xiong and the immurok companion structure. Preserve [credits](../CREDITS.md) and component licenses.

The corrected native prompt requires holding until the final result. The earlier 0.1.37 positive rejection was resolved by this instruction correction, without changing firmware. Quiet matching does not schedule keyboard-helper LED feedback. The earlier 0.1.36 cancellation evidence is not credited to the replacement; all acceptance above was repeated on 0.1.37.
