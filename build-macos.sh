#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

if [[ "$(uname -s)" != Darwin ]]; then
    echo "Please build on macOS 13 or later with Xcode Command Line Tools installed." >&2
    exit 1
fi
architecture="${1:-universal}"
case "$architecture" in
    universal) architectures=(arm64 x86_64) ;;
    arm64|x86_64) architectures=("$architecture") ;;
    *) echo "Usage: $0 [universal|arm64|x86_64]" >&2; exit 1 ;;
esac
# Prefer the standalone tools when available; respect an explicitly selected Xcode.
if [[ -z "${DEVELOPER_DIR:-}" && -d /Library/Developer/CommandLineTools ]]; then
    export DEVELOPER_DIR=/Library/Developer/CommandLineTools
fi
if ! xcrun --find swiftc >/dev/null 2>&1; then
    echo "Install Apple's developer tools with: xcode-select --install" >&2
    exit 1
fi
sdk="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
build="$PWD/macos/.build"
bundle="$PWD/dist/Touchline.app"
mkdir -p "$build" "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
binaries=()
for arch in "${architectures[@]}"; do
    binary="$build/Touchline-$arch"
    xcrun swiftc -swift-version 5 -O -sdk "$sdk" -target "$arch-apple-macosx13.0" \
        macos/Sources/*.swift -o "$binary"
    binaries+=("$binary")
done
if [[ "${#binaries[@]}" -gt 1 ]]; then
    xcrun lipo -create "${binaries[@]}" -output "$bundle/Contents/MacOS/Touchline"
else
    cp "${binaries[0]}" "$bundle/Contents/MacOS/Touchline"
fi
cp macos/Info.plist "$bundle/Contents/Info.plist"
cp assets/teams.json assets/team-names.zh-Hans.json "$bundle/Contents/Resources/"
cp macos/Touchline.icns "$bundle/Contents/Resources/Touchline.icns"
if [[ -n "${SIGN_IDENTITY:-}" ]]; then
    codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$bundle"
else
    codesign --force --timestamp=none --sign - "$bundle"
fi
codesign --verify --strict "$bundle"
archive="$PWD/dist/Touchline-macOS-$architecture.zip"
# ditto replaces the archive; no user data is stored inside the app bundle.
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$archive"
echo "Built $bundle"
echo "Packaged $archive"
