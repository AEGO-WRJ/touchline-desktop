#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Pin the upstream binary and its SHA-256; never build from a mutable latest URL.
version=2.10.1
sha256=1a856d2a9edb8e628de88121d7be04cf0e2167f8641650b78fca00b14244cf27
archive="$PWD/macos/.build/Sparkle-$version.tar.xz"
destination="$PWD/macos/.build/Sparkle-$version"
mkdir -p "$PWD/macos/.build"
if [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 2 "https://github.com/sparkle-project/Sparkle/releases/download/$version/Sparkle-$version.tar.xz" -o "$archive.download"
    mv "$archive.download" "$archive"
fi
if [[ "$(shasum -a 256 "$archive" | cut -d ' ' -f 1)" != "$sha256" ]]; then
    echo "Sparkle archive checksum mismatch. Remove $archive and download again." >&2
    exit 1
fi
if [[ ! -d "$destination/Sparkle.framework" ]]; then
    mkdir -p "$destination"
    tar -xf "$archive" -C "$destination"
fi
echo "$destination"
