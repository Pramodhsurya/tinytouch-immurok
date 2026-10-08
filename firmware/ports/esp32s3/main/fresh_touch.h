#pragma once
#include <stdbool.h>
#include <stdint.h>

typedef enum { FRESH_WAIT_LIFT, FRESH_WAIT_TOUCH, FRESH_CAPTURE, FRESH_CONSUMED } fresh_touch_phase_t;
typedef struct {
  fresh_touch_phase_t phase;
  uint64_t started, latest, low_since, high_since;
  bool low_seen, high_seen;
} fresh_touch_t;

void fresh_touch_begin(fresh_touch_t *s, uint64_t now);
void fresh_touch_cancel(fresh_touch_t *s);
// GPIO absence must be stable and accompanied by an acknowledged no-finger
// capture. Only a subsequent stable rising presence permits a new capture.
bool fresh_touch_observe(fresh_touch_t *s, uint64_t now, bool present, bool sensor_absent);
bool fresh_touch_finish(fresh_touch_t *s, uint64_t now, bool present, bool captured, bool matched);
