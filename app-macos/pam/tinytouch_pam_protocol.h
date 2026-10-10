#pragma once
#include <stdint.h>
#include <sys/types.h>
#define TT_PAM_DIR "/Library/Application Support/tinyTouch/PAM"
int tt_pam_load(const char *path, uid_t owner, mode_t mode, void *bytes, size_t length);
int tt_pam_receipt(const uint8_t key[32], const uint8_t nonce[32], uint32_t uid, uint32_t pid,
                   const char *user, const char *service, const char *response);
