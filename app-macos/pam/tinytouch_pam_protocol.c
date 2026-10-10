#include "tinytouch_pam_protocol.h"
#include "hmac_sha256.h"
#include <fcntl.h>
#include <unistd.h>
#include <sys/stat.h>
#include <string.h>

int tt_pam_load(const char *path, uid_t owner, mode_t mode, void *bytes, size_t length) {
    int fd = open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC);
    if (fd < 0) return 0;
    struct stat info;
    int valid = fstat(fd, &info) == 0 && S_ISREG(info.st_mode) && info.st_nlink == 1 &&
                info.st_uid == owner && (info.st_mode & 0777) == mode && info.st_size == (off_t)length;
    if (valid) valid = read(fd, bytes, length) == (ssize_t)length;
    close(fd);
    if (!valid) secure_zero(bytes, length);
    return valid;
}
static int nibble(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -1;
}
int tt_pam_receipt(const uint8_t key[32], const uint8_t nonce[32], uint32_t uid, uint32_t pid,
                   const char *user, const char *service, const char *response) {
    if (strlen(response) != 68 || strncmp(response, "OK:", 3) || response[67] != '\n') return 0;
    size_t ul = strlen(user), sl = strlen(service);
    if (!ul || ul > 64 || (strcmp(service, "sudo") && strcmp(service, "sudo_local"))) return 0;
    uint8_t got[32], want[32], material[160];
    for (unsigned i = 0; i < 32; ++i) {
        int hi = nibble(response[3 + 2*i]), lo = nibble(response[4 + 2*i]);
        if (hi < 0 || lo < 0) return 0;
        got[i] = (uint8_t)((hi << 4) | lo);
    }
    static const char domain[] = "tinyTouch-pam-receipt-v1";
    size_t n = sizeof(domain) - 1;
    memcpy(material, domain, n); memcpy(material + n, nonce, 32); n += 32;
    uint32_t fields[] = { uid, pid };
    for (unsigned f = 0; f < 2; ++f) for (int shift = 24; shift >= 0; shift -= 8) material[n++] = (uint8_t)(fields[f] >> shift);
    memcpy(material + n, user, ul + 1); n += ul + 1;
    memcpy(material + n, service, sl + 1); n += sl + 1;
    hmac_sha256(key, 32, material, n, want);
    unsigned diff = 0;
    for (unsigned i = 0; i < 32; ++i) diff |= got[i] ^ want[i];
    secure_zero(want, sizeof(want)); secure_zero(material, sizeof(material));
    return diff == 0;
}
