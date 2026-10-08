#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define AUTH_PROOF_TTL_MS 30000u
typedef bool (*auth_proof_hmac_fn)(const uint8_t key[32], const char *, uint8_t out[32]);
typedef bool (*auth_proof_random_fn)(uint8_t out[32]);
typedef struct {
  uint8_t key[32];
  char serial[16], host[17], client[65], device[65], context[65];
  uint64_t issued_ms;
  bool pending, verified;
} auth_proof_t;

void auth_proof_clear(auth_proof_t *state);
bool auth_proof_expire(auth_proof_t *state, uint64_t now_ms);
bool auth_proof_begin(auth_proof_t *state, const uint8_t key[32], const char *serial,
                      const char *host, const char *client, const char *context,
                      uint64_t now_ms, auth_proof_random_fn random,
                      auth_proof_hmac_fn hmac, char nonce[65], char mac[65]);
bool auth_proof_verify_host(auth_proof_t *state, const char *client, const char *mac,
                           uint64_t now_ms, auth_proof_hmac_fn hmac);
// Always consumes and erases the session, including negative matches/expiry.
bool auth_proof_finish(auth_proof_t *state, bool matched, uint64_t now_ms,
                      auth_proof_hmac_fn hmac, char mac[65]);
