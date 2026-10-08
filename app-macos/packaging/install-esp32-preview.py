#!/usr/bin/env python3
"""Install the built native app for this user, retaining any previous native bundle.
Does not replace the tinyTouch password helper or deploy PAM/login services.
"""
import pathlib
import plistlib
import re
import shutil
import subprocess
import uuid

root = pathlib.Path(__file__).resolve().parents[1]
source = root / '.build' / 'tinyTouch Native Preview.app'
applications = pathlib.Path.home() / 'Applications'
target = applications / 'tinyTouch Native.app'
identifier = 'com.tinytouch.native.preview'

def verify(bundle):
    info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != identifier or info.get('TinyTouchESP32Preview') is not True:
        raise SystemExit('The bundle is not the tinyTouch ESP32 native preview.')
    subprocess.run(['codesign', '--verify', '--strict', str(bundle)], check=True)
    subprocess.run(['codesign', '--verify', '--strict', str(bundle / 'Contents/Resources/tinyTouchCLI/tinytouch')], check=True)

verify(source)
applications.mkdir(exist_ok=True)
if target.is_symlink():
    raise SystemExit('Refusing to replace a symbolic-link application.')
if target.exists():
    verify(target)
    executable = str(target / 'Contents/MacOS/tinyTouch-native')
    running = subprocess.run(['pgrep', '-f', '^' + re.escape(executable) + '$'], stdout=subprocess.DEVNULL)
    if running.returncode == 0:
        raise SystemExit('Quit tinyTouch Native before installing an update.')
    if running.returncode != 1:
        raise SystemExit('Could not check whether the installed app is running.')
stage = applications / ('.tinyTouch-install-' + uuid.uuid4().hex + '.app')
try:
    shutil.copytree(source, stage, symlinks=True)
    verify(stage)
    backup = None
    if target.exists():
        backup = applications / ('.tinyTouch-previous-' + uuid.uuid4().hex + '.app')
        target.rename(backup)
    try:
        stage.rename(target)
    except Exception:
        if backup is not None:
            backup.rename(target)
        raise
    if backup is not None:
        print('Previous native app retained at: ' + str(backup))
finally:
    if stage.exists():
        shutil.rmtree(stage)
print(target)
