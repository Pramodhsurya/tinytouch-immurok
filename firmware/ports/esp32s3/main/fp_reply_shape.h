#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// Sensor replies have no command echo. Reject incompatible/duplicate payloads
// instead of truncating stale replies into a different command's result.
static inline bool fp_reply_shape_valid(uint8_t id, size_t payload, size_t capacity,
                                        size_t used, bool acknowledged) {
  if (used > capacity) return false;
  if (id == 0x07) return !acknowledged && payload >= 1 && payload - 1 <= capacity;
  if (id == 0x02 || id == 0x08)
    return acknowledged && capacity > 0 && payload > 0 && payload <= capacity - used;
  return false;
}
