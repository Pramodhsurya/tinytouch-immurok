#pragma once
#include <stdbool.h>
#include <stdint.h>

typedef struct {
  uint64_t started, latest, last_byte;
  bool failed;
} fp_uart_quiet_t;

static inline fp_uart_quiet_t fp_uart_quiet_begin(uint64_t now) {
  return (fp_uart_quiet_t){.started = now, .latest = now, .last_byte = now};
}

// Require 200 ms without received bytes; continuous/late traffic may never
// postpone a new command beyond the one-second synchronization deadline.
static inline bool fp_uart_quiet_observe(fp_uart_quiet_t *s, uint64_t now, bool received) {
  if (s->failed) return false;
  if (now < s->latest || now - s->started >= 1000) {
    s->failed = true; return false;
  }
  s->latest = now;
  if (received) s->last_byte = now;
  return now - s->last_byte >= 200;
}
