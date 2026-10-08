#include "fresh_touch.h"
#include <string.h>

void fresh_touch_begin(fresh_touch_t *s, uint64_t now) {
  memset(s, 0, sizeof(*s)); s->started = s->latest = now;
}
void fresh_touch_cancel(fresh_touch_t *s) { s->phase = FRESH_CONSUMED; }
static bool active(fresh_touch_t *s, uint64_t now) {
  if (s->phase == FRESH_CONSUMED) return false;
  if (now < s->latest || now - s->started >= 7000) {
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
    if (sensor_absent && now >= s->low_since && now - s->low_since >= 250)
      s->phase = FRESH_WAIT_TOUCH;
  } else if (s->phase == FRESH_WAIT_TOUCH) {
    if (!present) { s->high_seen = false; return false; }
    if (!s->high_seen) { s->high_seen = true; s->high_since = now; }
    if (now >= s->high_since && now - s->high_since >= 80) s->phase = FRESH_CAPTURE;
  } else if (!present) { fresh_touch_cancel(s); return false; }
  return s->phase == FRESH_CAPTURE;
}
bool fresh_touch_finish(fresh_touch_t *s, uint64_t now, bool present, bool captured, bool matched) {
  bool ok = active(s, now) && s->phase == FRESH_CAPTURE && present && captured && matched;
  fresh_touch_cancel(s); return ok;
}
