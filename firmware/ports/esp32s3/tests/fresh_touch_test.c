#include "fresh_touch.h"
#include "fp_reply_shape.h"
#include <assert.h>
#include <stdio.h>

static void arm(fresh_touch_t *s) {
  assert(!fresh_touch_observe(s, 0, false, true));
  assert(!fresh_touch_observe(s, 250, false, true));
  assert(s->phase == FRESH_WAIT_TOUCH);
}
int main(void) {
  // Prior SEARCH/MATCH/COUNT data must never be read as a fresh capture ACK.
  assert(fp_reply_shape_valid(0x07, 1, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 5, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 3, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 1, 0, 0, true));
  assert(!fp_reply_shape_valid(0x07, 0, 0, 0, false));
  assert(fp_reply_shape_valid(0x07, 33, 32, 0, false));
  assert(!fp_reply_shape_valid(0x07, 34, 32, 0, false));
  assert(!fp_reply_shape_valid(0x02, 32, 32, 0, false));
  assert(fp_reply_shape_valid(0x08, 32, 32, 0, true));
  assert(!fp_reply_shape_valid(0x08, 32, 32, 1, true));
  assert(!fp_reply_shape_valid(0x02, 1, 0, 0, true));
  assert(!fp_reply_shape_valid(0x02, 1, 1, 2, true));
  fresh_touch_t s;
  // A matching cached image without a physical touch must never authorize.
  fresh_touch_begin(&s, 0); arm(&s);
  for (unsigned t = 300; t < 6900; t += 100) assert(!fresh_touch_observe(&s, t, false, true));
  assert(!fresh_touch_finish(&s, 6900, false, true, true));
  // Even a stuck-high pin plus cached match cannot bypass initial absence.
  fresh_touch_begin(&s, 0);
  for (unsigned t = 0; t < 6900; t += 100) assert(!fresh_touch_observe(&s, t, true, false));
  assert(!fresh_touch_finish(&s, 6900, true, true, true));
  // GPIO low alone is insufficient when the sensor still claims an image.
  fresh_touch_begin(&s, 0);
  for (unsigned t = 0; t < 1000; t += 100) assert(!fresh_touch_observe(&s, t, false, false));
  assert(s.phase == FRESH_WAIT_LIFT);
  assert(!fresh_touch_finish(&s, 1000, true, true, true));
  // Debounced low + sensor absence, then new high + capture + match succeeds once.
  fresh_touch_begin(&s, 0); arm(&s);
  assert(!fresh_touch_observe(&s, 300, true, false));
  assert(!fresh_touch_observe(&s, 379, true, false));
  assert(fresh_touch_observe(&s, 380, true, false));
  assert(fresh_touch_finish(&s, 400, true, true, true));
  assert(!fresh_touch_finish(&s, 401, true, true, true));
  // A brief pulse, missing new image, mismatch or lift cannot produce a result.
  fresh_touch_begin(&s, 0); arm(&s);
  assert(!fresh_touch_observe(&s, 300, true, false));
  assert(!fresh_touch_observe(&s, 350, false, true));
  assert(!fresh_touch_observe(&s, 400, true, false));
  assert(!fresh_touch_observe(&s, 479, true, false));
  assert(fresh_touch_observe(&s, 480, true, false));
  assert(!fresh_touch_finish(&s, 500, true, false, true));
  fresh_touch_begin(&s, 0); arm(&s); fresh_touch_observe(&s, 300, true, false);
  assert(fresh_touch_observe(&s, 380, true, false));
  assert(!fresh_touch_finish(&s, 400, true, true, false));
  fresh_touch_begin(&s, 0); arm(&s); fresh_touch_observe(&s, 300, true, false);
  assert(fresh_touch_observe(&s, 380, true, false));
  assert(!fresh_touch_finish(&s, 400, false, true, true));
  fresh_touch_begin(&s, 0); arm(&s); fresh_touch_cancel(&s);
  assert(!fresh_touch_observe(&s, 300, true, false));
  assert(!fresh_touch_finish(&s, 400, true, true, true));
  fresh_touch_begin(&s, 0); arm(&s);
  assert(!fresh_touch_observe(&s, 7000, true, false));
  fresh_touch_begin(&s, 100);
  assert(!fresh_touch_observe(&s, 99, false, true));
  fresh_touch_begin(&s, 0); arm(&s);
  assert(!fresh_touch_observe(&s, 300, true, false));
  assert(!fresh_touch_observe(&s, 299, true, false));
  assert(!fresh_touch_finish(&s, 500, true, true, true));
  puts("fresh_touch: cached/no-touch/stuck-high, transitions, mismatch, cancel and expiry passed");
}
