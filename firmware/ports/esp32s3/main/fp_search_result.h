#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef enum { FP_SEARCH_ERROR, FP_SEARCH_NO_MATCH, FP_SEARCH_MATCH } fp_search_result_t;

// Hi-Link ZW111 manual V1.5.1, section 3.3.1.4: SEARCH 0x09 means
// no matching template. Only a validated response can establish this outcome.
static inline fp_search_result_t fp_search_result(bool answered, uint8_t confirm,
                                                 size_t length, uint16_t score,
                                                 bool usable) {
  if (!answered) return FP_SEARCH_ERROR;
  if (confirm == 0x09) return FP_SEARCH_NO_MATCH;
  if (confirm == 0 && length == 4 && score > 0 && usable) return FP_SEARCH_MATCH;
  return FP_SEARCH_ERROR;
}

// A confirmed mismatch is final. Fresh prompted requests also fail closed
// on transport/response errors rather than trying more commands on the image.
static inline bool fp_search_allow_fallback(bool quiet, fp_search_result_t result) {
  return !quiet && result == FP_SEARCH_ERROR;
}
