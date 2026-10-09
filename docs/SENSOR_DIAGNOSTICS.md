# ESP32 sensor diagnostics

The 0.1.38 runtime reports five templates and one host, but detailed inventory fails at the parameters stage. The user also reports rejection on the delayed-touch test. These results do not establish whether authentication fails before prompting, during capture/match or at expiry. Setup remains incomplete. No fingerprint is erased to work around a failed read.

Firmware **0.1.39 / esp32-sensor-diagnostics-1** adds numeric diagnostics to the existing 32-entry, volatile device event log. It does not change the presence gate, 30-second touch window, request deadlines, packet validation or authorization rules. Read events with the existing signed CLI's `logs` command after the failing operation. Read the inventory and its subsequent log sequentially so another foreground operation does not interrupt a sensor test.

| Event | Value / meaning |
| --- | --- |
| `fp_uart_write` | Instruction whose UART write failed |
| `fp_uart_tx_pending` | Instruction blocked because prior transmission did not drain |
| `fp_uart_not_quiet` | Instruction blocked because a bounded quiet receive period could not be established |
| `fp_uart_timeout` | Instruction without an acknowledged response before its deadline |
| `fp_reply_header` | Instruction receiving an invalid packet address or length |
| `fp_reply_oversize` | Instruction receiving a packet larger than the bounded receive buffer |
| `fp_reply_checksum` | Instruction receiving an invalid checksum |
| `fp_reply_shape` | Instruction × 256 + response payload length; incompatible/duplicate reply rejected |
| `fp_reply_incomplete` | Instruction whose successful ACK was not followed by all required data |
| `fp_parameters_confirm` | Confirmation byte for the failed parameter read; 255 means no valid confirmation was obtained |
| `fp_parameters_length` | Parameter bytes collected for that failed read; expected exactly 16 |
| `auth2_count_failed` | Count prerequisite failed before fresh authorization; negative value means unavailable |
| `auth_sensor_busy` | Sensor mutex could not be acquired |
| `auth_touch_prompt` | Gate armed; configured wait window in milliseconds |
| `auth_arm_timeout` | Milliseconds spent trying to establish confirmed absence |
| `auth_touch_timeout` | Milliseconds since the armed prompt without a new debounced touch |
| `auth_capture_failed` | Capture confirmation byte; 255 means no valid confirmation |
| `auth_match_failed` | Fresh capture did not produce a usable match |
| `auth_fresh_verified` / `auth_fresh_failed` | Capture/match duration in milliseconds and final gate outcome |
| `auth_cancelled` | Foreground cancellation observed |

Relevant instruction numbers are 1 (image capture), 2 (feature extraction), 3 (matching), 4 (search), 15 (system parameters), 19 (sensor verification), 29 (template count), 31 (index), 60 (lighting) and 96 (manual-light configuration). Only instruction numbers, packet lengths, confirmation codes, durations and stage names are recorded. UART payloads, fingerprint images/templates, keys, authentication tags and passwords are not logged. Existing event-log overflow discards older entries; absence of an event does not prove that stage never ran.

The revised Mac client distinguishes exact allowlisted AUTH2 failures: unavailable prerequisites, rejected handshake, and no fresh match before completion/expiry. Unknown errors use a generic message and never reflect raw wire text. These categories remain denials, not proof of a specific sensor fault.

Validation: all 153 Mac tests pass, including error-stage routing and suppression of unknown error text. The native bundle and ESP-IDF image build; esptool checksum/hash verification passes. Deployment and live diagnostic readback are pending. The sensor/UART root cause and physical timing acceptance remain unresolved.

Live 0.1.39 activation and readback now pass after enrolled-finger OTA approval and RESET. The failed parameters read receives confirmation 0 and only two bytes (the count-reply shape). The next count request receives and rejects a 17-byte payload (the parameters-reply shape); verification then rejects a three-byte payload. The commands and replies are out of step. A bounded UART quiet/drain boundary and rejection of incomplete inline data replies are being prepared. No enrollment is erased and no stale reply is accepted as a fresh capture.

Firmware 0.1.40 implements that correction: drain pending transmission, require 200 ms without received bytes before a new sensor command, and fail synchronization if the quiet period cannot be established within one second. The sensor mutex remains held. Fixed-size inline ACK data must be complete; ACK-only followed by bounded data packets remains supported. An incomplete successful response fails instead of returning a short result as success. The fresh-presence and request deadlines remain unchanged. Portable C tests cover late count/parameter traffic resetting the quiet timer, continuous traffic failing at the bound, clock regression, rejection of both foreign ACK shapes, valid replies and existing biometric/timing protections. ESP-IDF build and image verification pass. The first 0.1.40 OTA approval rejects before upload; activation and live fix acceptance remain pending. Device logs appear to restart between subsequent probes; whether this is user reset/unplugging or an unexpected restart is not yet established.

The user confirms no reset/unplugging during those failed attempts. The final 0.1.40 image adds numeric `reset_reason` to STATUS to distinguish reset categories once deployed. A single-session AUTH/OTA retry fails before the touch prompt and its immediate log retains sensor verification/count reply-shape errors; that attempt does not establish a fingerprint mismatch. The same-session log also shows continued uptime around 341 seconds, so a restart on every command is not established. A cold USB-only restart using the existing removable battery connector is requested; do not remove soldered leads or erase enrollment to perform this check. Deployment is still pending.

After the user confirms the cold USB-only restart, 0.1.39 reports sensor ready, five templates, one host and synchronized lighting. The next single-session update reaches `auth_touch_prompt value=7000`, then `auth_touch_timeout value=6674`; no new debounced touch is detected before the legacy deadline. This is a no-touch timeout rather than a reported fingerprint mismatch, and no firmware image is uploaded. The clean startup and successful prompt show communication recovery after this restart, but do not verify detailed inventory or the 0.1.40 correction.

For subsequent CLI device authorization, open a visible Terminal only when physical fingerprint approval is needed. Let the user press Return when their enrolled finger is beside the sensor; then display the actual device touch prompt and wait for physical approval. End the approval shell session automatically after completion, without a final Return wait; closing its window is the user's requested behavior. Use application prompts for reset, reconnect, diagnostics and native fingerprint tests instead of opening additional Terminals. The existing legacy update gate has roughly seven seconds total, including arming. The 30-second window applies to the native AUTH2 test after its prompt, not to this legacy update command. Keep the battery disconnected during these USB-only diagnostic tests.

The visible-Terminal retry succeeds: enrolled-finger approval passes, the verified final 0.1.40 image uploads completely and staging verification succeeds with uploader exit 0. RESET activation and post-activation inventory/biometric/output/wizard acceptance remain pending.

After RESET, activation of 0.1.40 / esp32-uart-sync-1 is verified with idle OTA, `auth_touch_ms=30000` and `reset_reason=1`. Initial sensor/count readback fails; logs retain incomplete count responses and count-shaped verification replies. A subsequent native management probe recovers sensor ready/five templates/one host, but detailed inventory still fails at parameters. The correction has not passed live acceptance. A full USB-only cold restart is requested in a visible Terminal, followed by sequential readback; biometric/output/wizard checks remain pending.

After the user confirms the USB-only cold restart, sequential background readback passes on 0.1.40: sensor ready, five templates, logical groups 1:4 and 10:1, eight free blocks, 14 settings and one registered host. Bluetooth readback confirms AUTO transport, encrypted bonded connection, keyboard/helper readiness and no active pairing. The current numeric log contains startup, USB attachment, Bluetooth encryption and successful count probe events, with no sensor reply errors. This establishes successful retained-inventory readback after a full sensor power cycle; it does not establish recovery after an ESP32-only reset or physical authentication/timing/output/wizard acceptance. The native app is reopened for the delayed-touch test.
