#include "transport_policy.h"
#include <assert.h>
#include <stdio.h>

int main(void) {
  // Cold BLE startup, mounted USB, then cable removal reported only as suspend.
  assert(transport_policy_uses_ble(0, false, false));
  assert(!transport_policy_uses_ble(0, true, false));
  assert(transport_policy_uses_ble(0, true, true));
  assert(transport_policy_uses_ble(0, false, true));
  // USB resumes/remounts: only a new request should prefer it again.
  assert(!transport_policy_uses_ble(0, true, false));
  // A failed explicit transport must not silently route credentials elsewhere.
  for (unsigned mounted = 0; mounted < 2; mounted++) {
    for (unsigned suspended = 0; suspended < 2; suspended++) {
      assert(!transport_policy_uses_ble(1, mounted, suspended));
      assert(transport_policy_uses_ble(2, mounted, suspended));
      assert(!transport_policy_uses_ble(3, mounted, suspended));
    }
  }
  puts("transport_policy: AUTO suspend/resume and explicit selection passed");
}
