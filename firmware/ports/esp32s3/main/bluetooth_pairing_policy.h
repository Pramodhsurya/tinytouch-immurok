#pragma once
#include <stdbool.h>
#include <stdint.h>

static inline bool bluetooth_repair_allowed(int64_t now, int64_t until,
                                            bool secure_connection, unsigned key_size) {
  return now < until && secure_connection && key_size >= 16;
}
