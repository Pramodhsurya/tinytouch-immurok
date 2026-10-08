#include "auth_proof.h"
#include <stdio.h>
#include <string.h>

static void wipe(void *p, size_t size) {
  volatile uint8_t *bytes = p;
  while (size--) *bytes++ = 0;
}
void auth_proof_clear(auth_proof_t *s) { wipe(s, sizeof(*s)); }
static bool hex(const char *s, size_t length) {
  if (!s || strlen(s) != length) return false;
  for (size_t i = 0; i < length; i++)
    if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'a' && s[i] <= 'f'))) return false;
  return true;
}
static bool serial_valid(const char *s) {
  if (!s || strlen(s) != 15 || strncmp(s, "TT-", 3)) return false;
  for (size_t i = 3; i < 15; i++)
    if (!((s[i] >= '0' && s[i] <= '9') || (s[i] >= 'A' && s[i] <= 'F'))) return false;
  return true;
}
static void encode(const uint8_t bytes[32], char out[65]) {
  static const char digits[] = "0123456789abcdef";
  for (size_t i = 0; i < 32; i++) {
    out[i * 2] = digits[bytes[i] >> 4]; out[i * 2 + 1] = digits[bytes[i] & 15];
  }
  out[64] = 0;
}
static bool tag(auth_proof_t *s, const char *role, auth_proof_hmac_fn hmac, char out[65]) {
  char material[320]; uint8_t bytes[32] = {0};
  int n = snprintf(material, sizeof(material), "tinyTouch-auth2-v1|%s|%s|%s|%s|%s|%s",
                   role, s->serial, s->host, s->client, s->device, s->context);
  bool ok = n > 0 && n < (int)sizeof(material) && hmac(s->key, material, bytes);
  if (ok) encode(bytes, out);
  wipe(bytes, sizeof(bytes)); wipe(material, sizeof(material));
  return ok;
}
bool auth_proof_expire(auth_proof_t *s, uint64_t now_ms) {
  if (s->pending && (now_ms < s->issued_ms || now_ms - s->issued_ms >= AUTH_PROOF_TTL_MS)) {
    auth_proof_clear(s); return true;
  }
  return false;
}
bool auth_proof_begin(auth_proof_t *s, const uint8_t key[32], const char *serial,
                      const char *host, const char *client, const char *context,
                      uint64_t now_ms, auth_proof_random_fn random,
                      auth_proof_hmac_fn hmac, char nonce[65], char mac[65]) {
  auth_proof_expire(s, now_ms);
  if (s->pending || !key || !random || !hmac || !serial_valid(serial) ||
      !hex(host, 16) || !hex(client, 64) || !hex(context, 64)) return false;
  uint8_t bytes[32] = {0};
  if (!random(bytes)) { wipe(bytes, sizeof(bytes)); return false; }
  memcpy(s->key, key, 32); strcpy(s->serial, serial); strcpy(s->host, host);
  strcpy(s->client, client); strcpy(s->context, context); encode(bytes, s->device);
  wipe(bytes, sizeof(bytes)); s->issued_ms = now_ms; s->pending = true;
  if (!tag(s, "challenge", hmac, mac)) { auth_proof_clear(s); return false; }
  strcpy(nonce, s->device); return true;
}
bool auth_proof_verify_host(auth_proof_t *s, const char *client, const char *mac,
                           uint64_t now_ms, auth_proof_hmac_fn hmac) {
  char expected[65] = {0};
  bool ok = !auth_proof_expire(s, now_ms) && s->pending && !s->verified && hmac &&
            hex(client, 64) && hex(mac, 64) && strcmp(client, s->client) == 0 &&
            tag(s, "host", hmac, expected);
  // Compare all tag bytes, even when an early byte differs.
  uint8_t diff = 0;
  if (ok) for (size_t i = 0; i < 64; i++) diff |= (uint8_t)mac[i] ^ (uint8_t)expected[i];
  ok = ok && diff == 0; wipe(expected, sizeof(expected));
  if (!ok) auth_proof_clear(s); else s->verified = true;
  return ok;
}
bool auth_proof_finish(auth_proof_t *s, bool matched, uint64_t now_ms,
                      auth_proof_hmac_fn hmac, char mac[65]) {
  bool ok = !auth_proof_expire(s, now_ms) && s->pending && s->verified && matched &&
            hmac && tag(s, "match", hmac, mac);
  auth_proof_clear(s); return ok;
}
