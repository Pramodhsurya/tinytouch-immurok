/* ESP32 sudo-only adapter. Missing/broken key, wrong peer, denial, timeout or
 * disconnect always falls through to the existing password providers.
 * No compatibility bare-OK or pre-authorization mode exists. */
#include <Security/Security.h>
#include <bsm/audit.h>
#include <sys/socket.h>
#include <sys/un.h>
#include <sys/poll.h>
#include <sys/time.h>
#include <unistd.h>
#include <pwd.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <time.h>
#define PAM_SM_AUTH
#include <security/pam_modules.h>
#include <security/pam_appl.h>
#include "tinytouch_pam_protocol.h"
#include "hmac_sha256.h"

static int trusted_peer(int fd, uid_t expected_uid, const char *hash) {
    uid_t uid; gid_t gid; audit_token_t token; socklen_t n = sizeof(token);
    if (getpeereid(fd, &uid, &gid) || uid != expected_uid ||
        getsockopt(fd, SOL_LOCAL, LOCAL_PEERTOKEN, &token, &n) || n != sizeof(token)) return 0;
    CFDataRef audit = CFDataCreate(NULL, (const UInt8 *)&token, sizeof(token));
    if (!audit) return 0;
    const void *keys[] = { kSecGuestAttributeAudit }, *values[] = { audit };
    CFDictionaryRef attributes = CFDictionaryCreate(NULL, keys, values, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    if (!attributes) { CFRelease(audit); return 0; }
    SecCodeRef code = NULL; SecStaticCodeRef static_code = NULL; CFDictionaryRef info = NULL;
    int valid = SecCodeCopyGuestWithAttributes(NULL, attributes, kSecCSDefaultFlags, &code) == errSecSuccess &&
        SecCodeCheckValidity(code, kSecCSStrictValidate, NULL) == errSecSuccess &&
        SecCodeCopyStaticCode(code, kSecCSDefaultFlags, &static_code) == errSecSuccess &&
        SecCodeCopySigningInformation(static_code, kSecCSSigningInformation, &info) == errSecSuccess;
    if (valid) {
        CFDataRef cdhash = CFDictionaryGetValue(info, kSecCodeInfoUnique);
        valid = cdhash && CFGetTypeID(cdhash) == CFDataGetTypeID() && CFDataGetLength(cdhash) == 20;
        if (valid) {
            static const char hex[] = "0123456789abcdef"; const UInt8 *b = CFDataGetBytePtr(cdhash);
            for (int i = 0; i < 20; i++) if (hash[2*i] != hex[b[i] >> 4] || hash[2*i+1] != hex[b[i] & 15]) valid = 0;
        }
    }
    if (info) CFRelease(info); if (static_code) CFRelease(static_code); if (code) CFRelease(code);
    CFRelease(attributes); CFRelease(audit);
    return valid;
}
static uint64_t millis(void) {
    struct timespec t;
    if (clock_gettime(CLOCK_MONOTONIC, &t)) return 0;
    return (uint64_t)t.tv_sec * 1000 + (uint64_t)t.tv_nsec / 1000000;
}
PAM_EXTERN int pam_sm_authenticate(pam_handle_t *pamh, int flags, int argc, const char **argv) {
    (void)flags; (void)argc; (void)argv;
    const char *user = NULL, *service = NULL;
    if (pam_get_user(pamh, &user, NULL) != PAM_SUCCESS || !user || strlen(user) > 64 ||
        pam_get_item(pamh, PAM_SERVICE, (const void **)&service) != PAM_SUCCESS || !service ||
        (strcmp(service, "sudo") && strcmp(service, "sudo_local"))) return PAM_IGNORE;
    struct passwd *pw = getpwnam(user);
    if (!pw || !pw->pw_uid || !pw->pw_dir || geteuid() != 0) return PAM_IGNORE;
    uint8_t key[32] = {0}, nonce[32]; char hash[41] = {0}, path[256];
    snprintf(path, sizeof(path), TT_PAM_DIR "/%u.key", pw->pw_uid);
    if (!tt_pam_load(path, 0, 0600, key, sizeof(key))) return PAM_IGNORE;
    snprintf(path, sizeof(path), TT_PAM_DIR "/%u.cdhash", pw->pw_uid);
    if (!tt_pam_load(path, 0, 0644, hash, 40)) { secure_zero(key, sizeof(key)); return PAM_IGNORE; }
    struct sockaddr_un address = {0}; address.sun_family = AF_UNIX; address.sun_len = sizeof(address);
    int count = snprintf(address.sun_path, sizeof(address.sun_path), "%s/Library/Application Support/tinyTouch/pam.sock", pw->pw_dir);
    if (count < 0 || count >= (int)sizeof(address.sun_path)) { secure_zero(key, sizeof(key)); return PAM_IGNORE; }
    int fd = socket(AF_UNIX, SOCK_STREAM, 0), result = PAM_IGNORE;
    if (fd < 0) { secure_zero(key, sizeof(key)); return result; }
    int no_pipe = 1; setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &no_pipe, sizeof(no_pipe));
    struct timeval send_timeout = { 2, 0 }; setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &send_timeout, sizeof(send_timeout));
    if (connect(fd, (struct sockaddr *)&address, sizeof(address)) || !trusted_peer(fd, pw->pw_uid, hash)) goto done;
    arc4random_buf(nonce, sizeof(nonce)); char hex[65];
    for (unsigned i = 0; i < sizeof(nonce); i++) snprintf(hex + 2*i, 3, "%02x", nonce[i]);
    char request[256]; count = snprintf(request, sizeof(request), "AUTH:%u:%s:%s:%d:%s\n", pw->pw_uid, user, service, getpid(), hex);
    if (count < 0 || count >= (int)sizeof(request) || send(fd, request, (size_t)count, 0) != count) goto done;
    /* The application's proof prompt is the visible approval surface. */
    uint64_t started = millis();
    if (!started) goto done;
    uint64_t deadline = started + 45000, now; char response[80] = {0}; size_t used = 0;
    while ((now = millis()) != 0 && now < deadline) {
        struct pollfd poller = { fd, POLLIN, 0 };
        int ready = poll(&poller, 1, 100);
        if (ready < 0) goto done; /* Including Ctrl+C: password fallback. */
        if (!ready) continue;
        ssize_t received = recv(fd, response + used, sizeof(response) - used - 1, MSG_DONTWAIT);
        if (received <= 0) goto done;
        used += (size_t)received; response[used] = 0;
        char *newline = strchr(response, '\n');
        if (newline) {
            now = millis();
            if (now && now < deadline && newline == response + used - 1 && tt_pam_receipt(key, nonce, pw->pw_uid, (uint32_t)getpid(), user, service, response)) result = PAM_SUCCESS;
            goto done;
        }
        if (used >= sizeof(response) - 1) goto done;
    }
done:
    close(fd); secure_zero(key, sizeof(key)); secure_zero(nonce, sizeof(nonce));
    return result;
}
PAM_EXTERN int pam_sm_setcred(pam_handle_t *pamh, int flags, int argc, const char **argv) {
    (void)pamh; (void)flags; (void)argc; (void)argv; return PAM_IGNORE;
}
