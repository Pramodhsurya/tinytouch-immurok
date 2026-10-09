# ESP32 sensor diagnostics

The 0.1.38 runtime reports five templates and one host, but detailed inventory fails at the parameters stage. The user also reports rejection on the delayed-touch test. These results do not establish whether authentication fails before prompting, during capture/match or at expiry. Setup remains incomplete. No fingerprint is erased to work around a failed read.

Firmware **0.1.39 / esp32-sensor-diagnostics-1** adds numeric diagnostics to the existing 32-entry, volatile device event log. It does not change the presence gate, 30-second touch window, request deadlines, packet validation or authorization rules. Read events with the existing signed CLI's `logs` command after the failing operation. Read the inventory and its subsequent log sequentially so another foreground operation does not interrupt a sensor test.

| Event | Value / meaning |
| --- | --- |
| `fp_uart_write` | Instruction whose UART write failed |
| `fp_uart_timeout` | Instruction without an acknowledged response before its deadline |
| `fp_reply_header` | Instruction receiving an invalid packet address or length |
| `fp_reply_oversize` | Instruction receiving a packet larger than the bounded receive buffer |
| `fp_reply_checksum` | Instruction receiving an invalid checksum |
| `fp_reply_shape` | Instruction × 256 + response payload length; incompatible/duplicate reply rejected |
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
