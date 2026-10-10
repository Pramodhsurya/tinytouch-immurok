#include "tinytouch_pam_protocol.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/stat.h>
#include <dlfcn.h>
#include <pwd.h>
#include <security/pam_appl.h>
#include <security/pam_modules.h>

static unsigned checks;
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr, "Failed line %d\n", __LINE__); exit(1); } } while (0)
int main(void) {
    uint8_t key[32], nonce[32], changed[32];
    for (unsigned i = 0; i < 32; ++i) key[i] = i;
    memset(nonce, 0xa1, 32); memcpy(changed, nonce, 32); changed[0] ^= 1;
    const char *good = "OK:04178544916311c05fb201200846e857b6f8028074de518747d2f4611020a8a2\n";
    CHECK(tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", good));
    CHECK(!tt_pam_receipt(key, changed, 501, 1234, "fixture", "sudo", good));
    CHECK(!tt_pam_receipt(key, nonce, 502, 1234, "fixture", "sudo", good));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1235, "fixture", "sudo", good));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "other", "sudo", good));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo_local", good));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "login", good));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", "OK\n"));
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", "DENY\n"));
    char bad[80]; strcpy(bad, good); bad[3] ^= 1;
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", bad));
    strcpy(bad, good); strcat(bad, "x");
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", bad));
    strcpy(bad, good); bad[67] = 0;
    CHECK(!tt_pam_receipt(key, nonce, 501, 1234, "fixture", "sudo", bad));
    char dir[] = "/private/tmp/tt-pam-c-XXXXXX"; CHECK(mkdtemp(dir));
    char path[128], alias[128]; snprintf(path, sizeof(path), "%s/key", dir); snprintf(alias, sizeof(alias), "%s/alias", dir);
    FILE *file = fopen(path, "wb"); CHECK(file); CHECK(fwrite(key, 1, 32, file) == 32); fclose(file); chmod(path, 0600);
    uint8_t out[32];
    CHECK(tt_pam_load(path, getuid(), 0600, out, 32)); CHECK(!memcmp(out, key, 32));
    CHECK(!tt_pam_load(path, getuid() + 1, 0600, out, 32));
    CHECK(!memcmp(out, (uint8_t[32]){0}, 32));
    CHECK(!tt_pam_load(path, getuid(), 0644, out, 32));
    CHECK(symlink(path, alias) == 0); CHECK(!tt_pam_load(alias, getuid(), 0600, out, 32)); unlink(alias);
    CHECK(link(path, alias) == 0); CHECK(!tt_pam_load(path, getuid(), 0600, out, 32)); unlink(alias);
    CHECK(truncate(path, 31) == 0); CHECK(!tt_pam_load(path, getuid(), 0600, out, 32));
    unlink(path); rmdir(dir);
    // Exercise the built module's real no-authority exit, without configuring
    // any system PAM service or requesting a password/fingerprint.
    void *module = dlopen("./pam_tinytouch.so", RTLD_NOW); CHECK(module);
    int (*authenticate)(pam_handle_t *, int, int, const char **) = dlsym(module, "pam_sm_authenticate");
    int (*setcred)(pam_handle_t *, int, int, const char **) = dlsym(module, "pam_sm_setcred");
    CHECK(authenticate && setcred);
    struct passwd *pw = getpwuid(getuid()); CHECK(pw);
    struct pam_conv conversation = {0}; pam_handle_t *handle = NULL;
    CHECK(pam_start("sudo", pw->pw_name, &conversation, &handle) == PAM_SUCCESS);
    CHECK(authenticate(handle, 0, 0, NULL) == PAM_IGNORE); // Non-root verifier cannot grant.
    CHECK(setcred(handle, 0, 0, NULL) == PAM_IGNORE);
    CHECK(pam_set_item(handle, PAM_SERVICE, "login") == PAM_SUCCESS);
    CHECK(authenticate(handle, 0, 0, NULL) == PAM_IGNORE); // Service outside allowlist.
    pam_end(handle, PAM_SUCCESS); dlclose(module);
    printf("tinyTouch PAM: %u receipt/storage checks passed\n", checks);
    return 0;
}
