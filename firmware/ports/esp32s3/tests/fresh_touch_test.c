#include "fresh_touch.h"
#include "fp_reply_shape.h"
#include "fp_uart_quiet.h"
#include "fp_search_result.h"
#include <assert.h>
#include <stdio.h>

static void arm(fresh_touch_t *s) {
  assert(!fresh_touch_observe(s, 0, false, true));
  assert(!fresh_touch_observe(s, 250, false, true));
  assert(s->phase == FRESH_WAIT_TOUCH);
}
int main(void) {
  // A validated negative SEARCH ends matching: neither prompted requests nor
  // normal output may scan slots and turn a confirmed mismatch into a match.
  fp_search_result_t search = fp_search_result(true, 0x09, 4, 0, false);
  assert(search == FP_SEARCH_NO_MATCH);
  assert(!fp_search_allow_fallback(true, search));
  assert(!fp_search_allow_fallback(false, search));
  assert(fp_search_result(true, 0x09, 0, 0, false) == FP_SEARCH_NO_MATCH);
  // A stale confirmation or malformed/failed response is not a biometric verdict.
  search = fp_search_result(false, 0x09, 4, 0, false);
  assert(search == FP_SEARCH_ERROR && !fp_search_allow_fallback(true, search));
  assert(fp_search_allow_fallback(false, search));
  assert(fp_search_result(true, 0, 3, 100, true) == FP_SEARCH_ERROR);
  assert(fp_search_result(true, 0, 4, 0, true) == FP_SEARCH_ERROR);
  assert(fp_search_result(true, 0, 4, 100, false) == FP_SEARCH_ERROR);
  assert(fp_search_result(true, 0, 4, 100, true) == FP_SEARCH_MATCH);
  // Once a fresh captured image mismatches, holding/replacing the finger cannot
  // resurrect that request; a new explicit request must arm absence again.
  fresh_touch_t mismatch;
  fresh_touch_begin_prompted(&mismatch, 0, 30000); arm(&mismatch);
  assert(!fresh_touch_observe(&mismatch, 300, true, false));
  assert(fresh_touch_observe(&mismatch, 380, true, false));
  assert(!fresh_touch_finish(&mismatch, 1000, true, true, false));
  assert(mismatch.phase == FRESH_CONSUMED);
  assert(!fresh_touch_finish(&mismatch, 1001, true, true, true));
  // Prior SEARCH/MATCH/COUNT data must never be read as a fresh capture ACK.
  assert(fp_reply_shape_valid(0x07, 1, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 5, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 3, 0, 0, false));
  assert(!fp_reply_shape_valid(0x07, 1, 0, 0, true));
  assert(!fp_reply_shape_valid(0x07, 0, 0, 0, false));
  assert(fp_reply_shape_valid(0x07, 33, 32, 0, false));
  assert(!fp_reply_shape_valid(0x07, 3, 16, 0, false)); // count ACK at parameter read
  assert(!fp_reply_shape_valid(0x07, 17, 2, 0, false)); // parameter ACK at count read
  assert(fp_reply_shape_valid(0x07, 17, 16, 0, false));
  assert(fp_reply_shape_valid(0x07, 3, 2, 0, false));
  assert(!fp_reply_shape_valid(0x07, 34, 32, 0, false));
  assert(!fp_reply_shape_valid(0x02, 32, 32, 0, false));
  assert(fp_reply_shape_valid(0x08, 32, 32, 0, true));
  assert(!fp_reply_shape_valid(0x08, 32, 32, 1, true));
  assert(!fp_reply_shape_valid(0x02, 1, 0, 0, true));
  assert(!fp_reply_shape_valid(0x02, 1, 1, 2, true));
  fp_uart_quiet_t quiet = fp_uart_quiet_begin(100);
  assert(!fp_uart_quiet_observe(&quiet, 299, false));
  assert(fp_uart_quiet_observe(&quiet, 300, false));
  quiet = fp_uart_quiet_begin(100);
  assert(!fp_uart_quiet_observe(&quiet, 250, true)); // late count response drained
  assert(!fp_uart_quiet_observe(&quiet, 400, true)); // late parameters drained
  assert(!fp_uart_quiet_observe(&quiet, 599, false));
  assert(fp_uart_quiet_observe(&quiet, 600, false));
  quiet = fp_uart_quiet_begin(0);
  for (unsigned t = 0; t < 1000; t += 100) assert(!fp_uart_quiet_observe(&quiet, t, true));
  assert(!fp_uart_quiet_observe(&quiet, 1000, false) && quiet.failed);
  assert(!fp_uart_quiet_observe(&quiet, 1200, false));
  quiet = fp_uart_quiet_begin(100);
  assert(!fp_uart_quiet_observe(&quiet, 99, false) && quiet.failed);
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
  // The native touch window starts at the prompt, not at initial arming.
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  assert(!fresh_touch_observe(&s, 10000, true, false));
  assert(fresh_touch_observe(&s, 10080, true, false));
  assert(fresh_touch_finish(&s, 10100, true, true, true));
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  assert(!fresh_touch_observe(&s, 30100, true, false));
  assert(fresh_touch_observe(&s, 30180, true, false));
  assert(fresh_touch_finish(&s, 30249, true, true, true));
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  assert(!fresh_touch_observe(&s, 30100, true, false));
  assert(fresh_touch_observe(&s, 30180, true, false));
  // A touch made within the window gets a bounded capture/match budget.
  assert(fresh_touch_finish(&s, 30250, true, true, true));
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  assert(!fresh_touch_observe(&s, 30100, true, false));
  assert(fresh_touch_observe(&s, 30180, true, false));
  assert(!fresh_touch_finish(&s, 37180, true, true, true));
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  assert(!fresh_touch_observe(&s, 30249, false, true));
  assert(!fresh_touch_observe(&s, 30250, false, true));
  assert(s.phase == FRESH_CONSUMED);
  // A held finger still cannot extend initial arming beyond seven seconds.
  fresh_touch_begin_prompted(&s, 0, 30000);
  assert(!fresh_touch_observe(&s, 6999, true, false));
  assert(!fresh_touch_observe(&s, 7000, true, false));
  assert(s.phase == FRESH_CONSUMED);
  fresh_touch_begin_prompted(&s, 0, 30000); arm(&s);
  fresh_touch_cancel(&s);
  assert(!fresh_touch_finish(&s, 10000, true, true, true));
  for (unsigned window = 0; window <= 30001; window += 30001) {
    fresh_touch_begin_prompted(&s, 0, window);
    assert(s.phase == FRESH_CONSUMED);
  }
  puts("fresh_touch: presence, 30-second prompt boundary, legacy arming, cancel and expiry passed");
}
