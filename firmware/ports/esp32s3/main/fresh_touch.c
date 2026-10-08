#include "fresh_touch.h"
#include <string.h>

void fresh_touch_begin(fresh_touch_t *s, uint64_t now) {
  memset(s, 0, sizeof(*s)); s->started = s->latest = now;
}
void fresh_touch_begin_prompted(fresh_touch_t *s, uint64_t now, uint32_t window_ms) {
  fresh_touch_begin(s, now);
  if (!window_ms || window_ms > 30000) { s->phase = FRESH_CONSUMED; return; }
  s->touch_window_ms = window_ms;
}
void fresh_touch_cancel(fresh_touch_t *s) { s->phase = FRESH_CONSUMED; }
static bool active(fresh_touch_t *s, uint64_t now) {
  if (s->phase == FRESH_CONSUMED) return false;
  bool prompted = s->touch_window_ms && s->phase != FRESH_WAIT_LIFT;
  uint64_t started = prompted ? s->prompted_at : s->started;
  uint64_t limit = prompted ? s->touch_window_ms : 7000;
  if (prompted && s->phase == FRESH_CAPTURE) {
    started = s->capture_started; limit = 7000;
  }
  if (now < s->latest || now - started >= limit) {
    fresh_touch_cancel(s); return false;
  }
  s->latest = now;
  return true;
}
bool fresh_touch_observe(fresh_touch_t *s, uint64_t now, bool present, bool sensor_absent) {
  if (!active(s, now)) return false;
  if (s->phase == FRESH_WAIT_LIFT) {
    if (present) { s->low_seen = false; return false; }
    if (!s->low_seen) { s->low_seen = true; s->low_since = now; }
    if (sensor_absent && now >= s->low_since && now - s->low_since >= 250) {
      s->phase = FRESH_WAIT_TOUCH;
      s->prompted_at = now;
    }
  } else if (s->phase == FRESH_WAIT_TOUCH) {
    if (!present) { s->high_seen = false; return false; }
    if (!s->high_seen) { s->high_seen = true; s->high_since = now; }
    if (now >= s->high_since && now - s->high_since >= 80) {
      s->phase = FRESH_CAPTURE;
      s->capture_started = now;
    }
  } else if (!present) { fresh_touch_cancel(s); return false; }
  return s->phase == FRESH_CAPTURE;
}
bool fresh_touch_finish(fresh_touch_t *s, uint64_t now, bool present, bool captured, bool matched) {
  bool ok = active(s, now) && s->phase == FRESH_CAPTURE && present && captured && matched;
  fresh_touch_cancel(s); return ok;
}
