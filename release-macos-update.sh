#!/bin/bash
# Generate the signed macOS feed after building the final universal ZIP.
# Upload the ZIP as a GitHub Release asset BEFORE publishing this feed to main.
set -euo pipefail
cd "$(dirname "$0")"
sparkle="$(bash macos/prepare-sparkle.sh)"
account=com.aegowrj.touchline
archive="$PWD/dist/Touchline-macOS-universal.zip"
plist="$PWD/dist/Touchline.app/Contents/Info.plist"
version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$plist")"
build="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$plist")"
minimum="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$plist")"
tag="${1:-v$version-macos.1}"
if [[ ! "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+-macos\.[0-9]+$ ]] || [[ "$tag" != "v$version-macos."* ]] || [[ ! "$build" =~ ^[0-9]+$ ]]; then
    echo "Expected tag v$version-macos.N and a numeric bundle build version." >&2
    exit 1
fi
public_key="$("$sparkle/bin/generate_keys" --account "$account" -p)"
embedded_key="$(/usr/libexec/PlistBuddy -c 'Print :SUPublicEDKey' "$plist")"
if [[ "$public_key" != "$embedded_key" ]]; then
    echo "Signing key does not match the public key embedded in the app. Do not rotate the key accidentally." >&2
    exit 1
fi
if ! signature="$("$sparkle/bin/sign_update" --account "$account" "$archive")"; then
    echo "$signature" >&2
    echo "Update signing failed; the existing feed has not been changed." >&2
    exit 1
fi
feed="$PWD/macos/appcast.xml"
cat > "$feed" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
  <channel>
    <title>看球 · Touchline macOS 更新</title>
    <link>https://github.com/AEGO-WRJ/touchline-desktop/releases</link>
    <description>macOS 通用版本更新，独立于 Windows 稳定版。</description>
    <language>zh-CN</language>
    <item>
      <title>看球 $version</title>
      <pubDate>$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S +0000')</pubDate>
      <sparkle:version>$build</sparkle:version>
      <sparkle:shortVersionString>$version</sparkle:shortVersionString>
      <sparkle:minimumSystemVersion>$minimum</sparkle:minimumSystemVersion>
      <sparkle:fullReleaseNotesLink>https://github.com/AEGO-WRJ/touchline-desktop/releases/tag/$tag</sparkle:fullReleaseNotesLink>
      <enclosure url="https://github.com/AEGO-WRJ/touchline-desktop/releases/download/$tag/Touchline-macOS-universal.zip" $signature type="application/octet-stream" />
    </item>
  </channel>
</rss>
EOF
"$sparkle/bin/sign_update" --account "$account" "$feed"
echo "Signed $feed. Upload the unchanged ZIP before publishing this feed."
