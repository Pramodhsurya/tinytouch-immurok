#!/usr/bin/env python3
"""Build local administrator packages for the current ESP32 native app.

Build the native preview first. This stages packages only; it never invokes
sudo, installs a provider, reads a Keychain item or changes system PAM files.
"""
import hashlib
import json
import pathlib
import plistlib
import re
import shutil
import subprocess

root = pathlib.Path(__file__).resolve().parents[1]
bundle = root / '.build/tinyTouch Native Preview.app'
info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
if info.get('CFBundleIdentifier') != 'com.tinytouch.native.preview' or info.get('TinyTouchESP32Preview') is not True:
    raise SystemExit('Build the ESP32 native preview before packaging sudo.')
subprocess.run(['codesign', '--verify', '--strict', str(bundle)], check=True)
signature = subprocess.run(['codesign', '-d', '--verbose=4', str(bundle)], capture_output=True, text=True, check=True)
match = re.search(r'^CDHash=([0-9a-f]{40})$', signature.stderr, re.M)
flags = re.search(r'flags=0x([0-9a-f]+)', signature.stderr)
if not match or not flags or not int(flags.group(1), 16) & 0x10000:
    raise SystemExit('The native app must have a verifiable code hash and hardened runtime.')
entitlements = subprocess.run(['codesign', '-d', '--entitlements', '-', str(bundle)], capture_output=True, check=True)
if entitlements.stdout.strip():
    allowed = plistlib.loads(entitlements.stdout)
    if any(allowed.get(k) for k in ['com.apple.security.get-task-allow',
                                  'com.apple.security.cs.disable-library-validation',
                                  'com.apple.security.cs.allow-dyld-environment-variables',
                                  'com.apple.security.cs.allow-unsigned-executable-memory',
                                  'com.apple.security.cs.allow-jit']):
        raise SystemExit('Refusing a native app with debugging or runtime exceptions.')
app_hash = match.group(1)
subprocess.run(['swift', 'build', '--product', 'tinyTouchPAMSetup'], cwd=root, check=True)
subprocess.run(['make', 'pam_tinytouch.so'], cwd=root / 'pam', check=True)
output = root / '.build/sudo-installer'
if output.exists():
    shutil.rmtree(output)
tools = output / 'payload/Library/Security/tinyTouch'
tools.mkdir(parents=True)
for source, name, mode in [(root / 'pam/pam_tinytouch.so', 'pam_tinytouch.so', 0o644),
                            (root / '.build/debug/tinyTouchPAMSetup', 'tinyTouchPAMSetup', 0o755)]:
    target = tools / name
    shutil.copy2(source, target)
    target.chmod(mode)
    options = ['--options', 'runtime'] if name == 'tinyTouchPAMSetup' else []
    subprocess.run(['codesign', '--force', '--sign', '-', *options, str(target)], check=True)
    subprocess.run(['codesign', '--verify', '--strict', str(target)], check=True)
for source, name in [(root / 'LICENSE', 'LICENSE-app'), (root.parent / 'CREDITS.md', 'CREDITS.md')]:
    target = tools / name
    shutil.copy2(source, target)
    target.chmod(0o644)
install = output / 'install-scripts'
remove = output / 'remove-scripts'
for directory, action in [(install, 'install'), (remove, 'remove')]:
    directory.mkdir()
    arguments = f'install "$tt_console_uid" "{app_hash}"' if action == 'install' else 'remove "$tt_console_uid"'
    script = directory / 'postinstall'
    script.write_text('#!/bin/sh\nset -eu\n'
                      'tt_console_uid=$(/usr/bin/stat -f %u /dev/console)\n'
                      '[ "$tt_console_uid" -gt 0 ] && [ "$tt_console_uid" -lt 4294967295 ] || exit 1\n'
                      f'exec /Library/Security/tinyTouch/tinyTouchPAMSetup {arguments}\n')
    script.chmod(0o755)
setup_pkg = output / 'tinyTouch Sudo Setup.pkg'
remove_pkg = output / 'tinyTouch Sudo Remove.pkg'
subprocess.run(['pkgbuild', '--root', str(output / 'payload'), '--ownership', 'recommended',
                '--scripts', str(install), '--identifier', 'com.tinytouch.sudo.local', '--version', '0.1.0',
                '--install-location', '/', str(setup_pkg)], check=True)
subprocess.run(['pkgbuild', '--nopayload', '--scripts', str(remove), '--identifier', 'com.tinytouch.sudo.remove.local',
                '--version', '0.1.0', '--install-location', '/', str(remove_pkg)], check=True)
manifest = {'native_app_cdhash': app_hash, 'scope': 'sudo and sudo_local only',
            'architecture': 'local native app and setup helper; universal PAM module',
            'packages': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (setup_pkg, remove_pkg)}}
(output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print('Prepared local sudo packages: ' + str(output))
