#include "transport_policy.h"
#include <assert.h>
#include <stdio.h>

int main(void) {
  // Cold BLE startup, mounted USB, then cable removal reported only as suspend.
  assert(transport_policy_uses_ble(0, false, false, false, false));
  assert(!transport_policy_uses_ble(0, true, false, false, false));
  assert(transport_policy_uses_ble(0, true, true, false, false));
  assert(transport_policy_uses_ble(0, false, true, false, false));
  // With the divider fitted, VBUS low selects BLE even while TinyUSB is
  // still mounted; VBUS high retains USB.
  assert(transport_policy_uses_ble(0, true, false, true, false));
  assert(!transport_policy_uses_ble(0, true, false, true, true));
  assert(transport_policy_uses_ble(0, true, true, true, true));
  // USB resumes/remounts: only a new request should prefer it again.
  assert(!transport_policy_uses_ble(0, true, false, true, true));
  // A failed explicit transport must not silently route credentials elsewhere.
  for (unsigned mounted = 0; mounted < 2; mounted++) {
    for (unsigned suspended = 0; suspended < 2; suspended++) {
      assert(!transport_policy_uses_ble(1, mounted, suspended, true, false));
      assert(transport_policy_uses_ble(2, mounted, suspended, true, true));
      assert(!transport_policy_uses_ble(3, mounted, suspended, true, true));
    }
  }
  puts("transport_policy: AUTO suspend/resume and explicit selection passed");
}
