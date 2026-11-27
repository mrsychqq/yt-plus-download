#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")/.."
project_dir="$PWD"
mkdir -p .build/module-cache dist
staging="$(mktemp -d "$project_dir/.build/package.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
bundle="$staging/YT+.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
cp Resources/Info.plist "$bundle/Contents/Info.plist"
cp Resources/AppIcon.icns "$bundle/Contents/Resources/AppIcon.icns"
cp Resources/IconYouTube.png Resources/IconTikTok.png Resources/IconPinterest.png "$bundle/Contents/Resources/"

module_cache="$project_dir/.build/module-cache"
profiles="$staging/DownloadProfilesData.swift"
{
    printf 'let embeddedDownloadProfiles = #"""\n'
    cat Resources/DownloadProfiles.json
    printf '\n"""#\n'
} > "$profiles"
swiftc -O -swift-version 6 -parse-as-library \
    -target "$(uname -m)-apple-macosx14.0" -module-cache-path "$module_cache" \
    -o "$bundle/Contents/MacOS/YTPlus" Sources/*.swift "$profiles"

codesign --force --sign - "$bundle"
codesign --verify --strict "$bundle"

destination="$project_dir/dist/YT+.app"
if [[ -e "$destination" ]]; then
    mv "$destination" "$staging/previous.app"
fi
if ! mv "$bundle" "$destination"; then
    if [[ -d "$staging/previous.app" ]]; then
        mv "$staging/previous.app" "$destination"
    fi
    exit 1
fi
printf '%s\n' "$destination"
