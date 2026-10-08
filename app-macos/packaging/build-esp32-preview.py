#!/usr/bin/env python3
"""Package the native ESP32 preview with an explicitly supplied signed USB backend.
Usage: python3 packaging/build-esp32-preview.py /path/to/tinytouch-onedir
The existing installed helper and its Keychain identity are never replaced.
"""
import pathlib
import plistlib
import shutil
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
backend = pathlib.Path(sys.argv[1]).resolve()
if not (backend / 'tinytouch').is_file() or not (backend / '_internal').is_dir():
    raise SystemExit('Supply the complete tinyTouch signed CLI directory.')
subprocess.run(['codesign', '--verify', '--strict', str(backend / 'tinytouch')], check=True)
subprocess.run(['swift', 'build', '--product', 'immurokApp'], cwd=root, check=True)
bundle = root / '.build' / 'tinyTouch Native Preview.app'
if bundle.exists():
    shutil.rmtree(bundle)
contents = bundle / 'Contents'
resources = contents / 'Resources'
(contents / 'MacOS').mkdir(parents=True)
resources.mkdir()
shutil.copy2(root / '.build/debug/immurokApp', contents / 'MacOS/tinyTouch-native')
for source in (root / 'Resources').iterdir():
    if source.suffix in ('.png', '.icns') or source.name == 'Localization':
        if source.is_dir():
            shutil.copytree(source, resources / source.name)
        else:
            shutil.copy2(source, resources / source.name)
# copy2 preserves signed bytes and the backend's existing Keychain identity.
shutil.copytree(backend, resources / 'tinyTouchCLI', symlinks=True)
shutil.copy2(root / 'LICENSE', resources / 'LICENSE-app')
shutil.copy2(root.parent / 'CREDITS.md', resources / 'CREDITS.md')
shutil.copy2(root.parent / 'firmware/ports/esp32s3/LICENSE', resources / 'LICENSE-tinyTouch')
info = plistlib.loads((root / 'Resources/Info.plist').read_bytes())
info.update(CFBundleName='tinyTouch Native Preview', CFBundleDisplayName='tinyTouch Native Preview',
            CFBundleIdentifier='com.tinytouch.native.preview', CFBundleExecutable='tinyTouch-native',
            CFBundleShortVersionString='0.1.0', CFBundleVersion='1', TinyTouchESP32Preview=True,
            LSUIElement=False,
            NSBluetoothAlwaysUsageDescription='tinyTouch reads the identity of your paired ESP32 fingerprint device.')
(contents / 'Info.plist').write_bytes(plistlib.dumps(info))
subprocess.run(['codesign', '--force', '--sign', '-', str(bundle)], check=True)
subprocess.run(['codesign', '--verify', '--strict', str(bundle)], check=True)
print(bundle)
