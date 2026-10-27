#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ -z "${DEVELOPER_DIR:-}" && -d /Library/Developer/CommandLineTools ]]; then
    export DEVELOPER_DIR=/Library/Developer/CommandLineTools
fi
mkdir -p macos/.build
xcrun swiftc -swift-version 5 macos/Sources/Core.swift macos/Sources/AppModel.swift macos/Tests/main.swift -o macos/.build/TouchlineTests
macos/.build/TouchlineTests
