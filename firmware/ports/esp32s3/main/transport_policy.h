#pragma once

#include <stdbool.h>
#include <stdint.h>

// A configured USB device can remain mounted after cable removal when the
// board runs on battery without VBUS sensing. Suspension means USB cannot
// currently deliver reports; AUTO uses BLE for a new request in that state.
// This is an availability policy, not physical cable detection. Explicit
// USB/BLE selections remain authoritative, even if that transport is down.
static inline bool transport_policy_uses_ble(uint8_t mode, bool usb_mounted,
                                             bool usb_suspended,
                                             bool vbus_sense_available,
                                             bool vbus_present) {
  bool usb_unavailable = !usb_mounted || usb_suspended ||
                         (vbus_sense_available && !vbus_present);
  return mode == 2 || (mode == 0 && usb_unavailable);
}
