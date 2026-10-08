# ESP32 request-bound fingerprint proof

Implemented October 8, 2026 in ESP32 firmware **0.1.36**, build **esp32-auth-proof-1**, and the native Mac preview. This USB protocol-6 extension uses an existing tinyTouch HID host key. Upstream ECDH pairing, encrypted management, BLE proofs, PAM and device-held SSH/vault operations remain pending.

## Capability and prerequisites

`STATUS` reports `auth_proof=1` in the normal build. Missing capability means unsupported; version strings do not establish support. Recovery and fingerprint-bypass builds report `0` and reject `AUTH2 BEGIN`. The device requires a registered host with its existing 32-byte key, connected CDC and an enrolled fingerprint. First-setup authorization cannot produce this proof.

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
tinyTouch-auth2-v1|<role>|<serial>|<host-id>|<client-nonce>|<device-nonce>|<context-sha256>
```

Distinct roles are `challenge`, `host` and `match`. The Mac verifies the challenge before sending its host tag; the device verifies that tag before asking for a fingerprint. The match tag is emitted only after an enrolled-finger match. Test context is SHA-256 of `native-auth-test-v1|<serial>`; future privileged integrations must bind their exact operation and consume the result at their trusted verifier.

Only one device request may be pending. Its lifetime is 30 seconds on a monotonic clock, including the sensor's seven-second touch window. Expiry, backward clock, invalid host proof, no match, abort or CDC disconnect clears pending device key/request state. A removed or rekeyed host cannot finish a pending request. `AUTH2 ABORT` clears state and returns `OK AUTH2 ABORT`. Errors return `ERR AUTH2 ...` and never grant authority. A failed BEGIN does not replace a live request. Client parsing rejects duplicate/unknown fields, malformed tags, changed nonces/context, role reflection and expired responses; every verification attempt consumes the client request.

Successful AUTH2 does not open the legacy two-minute configuration authorization window or set the native app's legacy global verification flag. The preview reports test success only. Legacy `AUTH` and USB/BLE keyboard helpers remain compatible.

## Entropy, ownership and limits

The Mac uses `SecRandomCopyBytes` for its 32-byte nonce. Firmware uses `esp_fill_random` only while the Bluetooth controller is enabled; otherwise it fails closed. See the RF entropy prerequisite in [Espressif's ESP-IDF 5.3.4 RNG documentation](https://docs.espressif.com/projects/esp-idf/en/v5.3.4/esp32s3/api-reference/system/random.html).

The native client acquires the existing foreground-helper flock lease and waits for its matching acknowledgement before opening CDC. It closes CDC before releasing the lease. Cancellation, deadlines and bounded output are enforced. Existing helpers retain password-output ownership and resume afterward; launchd registration is unchanged.

HMAC authenticates possession of a shared key; a compromised host with that key can forge tags. It provides no confidentiality, new host commissioning, trusted local IPC, physical anti-tamper or production firmware trust. Secure boot and flash encryption remain disabled in this development image. Legacy configuration authorization remains separately available; AUTH2 does not strengthen every legacy command. BLE proofs and the privileged verifier must be implemented and tested before FW-14/FW-15/MAC-04 can be completed.

## Validation

C and Swift share public known-answer vectors independently checked with Python HMAC-SHA256. C tests cover all tag-character changes, replay, missing host proof, no match, expiry/backward clocks, cancellation, state clearing and RNG/HMAC failure. Normal and UndefinedBehaviorSanitizer runs pass. AddressSanitizer hung in loader/malloc shadow initialization before the test entry point on this Mac; it is not recorded as passed.

The Mac suite passes **140 tests**, including proof parsing/domain separation, request consumption, context/serial binding, capability negotiation and lease exclusion/cleanup/acknowledgement. The live helper lease handshake passed; both helpers remained Running and no lease records remained. Deployment and live native proof are recorded separately when observed.

Derived from tinyTouch by Zimeng Xiong and the immurok companion structure. Preserve [credits](../CREDITS.md) and component licenses.
