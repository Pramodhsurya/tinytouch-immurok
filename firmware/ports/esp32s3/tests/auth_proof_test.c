#include "auth_proof.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <CommonCrypto/CommonHMAC.h>

static bool hmac(const uint8_t key[32], const char *material, uint8_t out[32]) {
  CCHmac(kCCHmacAlgSHA256, key, 32, material, strlen(material), out); return true;
}
static bool random_bytes(uint8_t out[32]) { memset(out, 0xb2, 32); return true; }
static bool broken_random(uint8_t out[32]) { (void)out; return false; }
static bool broken_hmac(const uint8_t key[32], const char *m, uint8_t out[32]) {
  (void)key; (void)m; (void)out; return false;
}
static const char *host = "630dcd2966c43366";
static const char *client = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
static const char *context = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
static const char *host_mac = "cf0d4fb19a6229af0f14e8dd3cf499275522ffcb98c6d0904830bca837cba417";
static uint8_t key[32];
static void begin(auth_proof_t *s) {
  char nonce[65], mac[65];
  assert(auth_proof_begin(s, key, "TT-001122334455", host, client, context, 100,
                          random_bytes, hmac, nonce, mac));
  assert(!strcmp(mac, "c1ef24a244a5abc8e5db050bd91af1614c1a9dac7be37288f2daec51209fed97"));
}
static void erased(auth_proof_t *s) {
  uint8_t zero[sizeof(*s)] = {0}; assert(!memcmp(s, zero, sizeof(*s)));
}
int main(void) {
  for (unsigned i = 0; i < 32; i++) key[i] = (uint8_t)i;
  auth_proof_t s = {0}; char nonce[65], mac[65];
  begin(&s);
  assert(!auth_proof_begin(&s, key, "TT-001122334455", host, client, context, 101, random_bytes, hmac, nonce, mac));
  assert(auth_proof_verify_host(&s, client, host_mac, 200, hmac));
  assert(auth_proof_finish(&s, true, 300, hmac, mac));
  assert(!strcmp(mac, "9933afacb8616a8292656188b47fcb63f6da118451f913a2573ff089a73854a7"));
  erased(&s);
  assert(!auth_proof_finish(&s, true, 301, hmac, mac));
  assert(!auth_proof_verify_host(&s, client, host_mac, 301, hmac));
  for (unsigned i = 0; i < 64; i++) {
    char corrupt[65]; strcpy(corrupt, host_mac); corrupt[i] = corrupt[i] == '0' ? '1' : '0';
    begin(&s); assert(!auth_proof_verify_host(&s, client, corrupt, 200, hmac)); erased(&s);
  }
  begin(&s); assert(!auth_proof_finish(&s, true, 200, hmac, mac)); erased(&s);
  begin(&s); assert(auth_proof_verify_host(&s, client, host_mac, 200, hmac));
  assert(!auth_proof_finish(&s, false, 300, hmac, mac)); erased(&s);
  begin(&s); assert(!auth_proof_verify_host(&s, client, host_mac, 30100, hmac)); erased(&s);
  begin(&s); assert(!auth_proof_verify_host(&s, client, host_mac, 99, hmac)); erased(&s);
  begin(&s); assert(auth_proof_verify_host(&s, client, host_mac, 200, hmac));
  assert(!auth_proof_finish(&s, true, 30100, hmac, mac)); erased(&s);
  begin(&s); auth_proof_clear(&s); erased(&s); // cancellation/disconnect
  assert(!auth_proof_begin(&s, key, "TT-001122334455", host, client, context, 100, broken_random, hmac, nonce, mac)); erased(&s);
  assert(!auth_proof_begin(&s, key, "TT-001122334455", host, client, context, 100, random_bytes, broken_hmac, nonce, mac)); erased(&s);
  assert(!auth_proof_begin(&s, key, "TT-001122334455", host, "invalid", context, 100, random_bytes, hmac, nonce, mac));
  puts("auth_proof: vectors, replay, all tag-byte tampering, no-match, timeout, cancel and crypto failure passed");
}
