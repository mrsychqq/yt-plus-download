#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")/.."
YTPLUS_BUNDLE_TOOLS="${YTPLUS_BUNDLE_TOOLS:-1}" bash scripts/build.sh

project_dir="$PWD"
staging="$(mktemp -d "$project_dir/.build/image.XXXXXX")"
device=""
cleanup() {
    if [[ -n "$device" ]]; then
        hdiutil detach "$device" -quiet 2>/dev/null || hdiutil detach "$device" -force -quiet 2>/dev/null || true
    fi
    rm -rf "$staging"
}
trap cleanup EXIT

mkdir -p "$staging/contents/.background"
ditto "dist/YT+.app" "$staging/contents/YT+.app"
ln -s /Applications "$staging/contents/Applications"

if [[ -f "Resources/dmg-background.png" ]]; then
    cp "Resources/dmg-background.png" "$staging/contents/.background/background.png"
fi
if [[ -f "Resources/AppIcon.icns" ]]; then
    cp "Resources/AppIcon.icns" "$staging/contents/.VolumeIcon.icns"
fi

version="$(plutil -extract CFBundleShortVersionString raw 'dist/YT+.app/Contents/Info.plist')"
image="YTPlus-$version-$(uname -m).dmg"
rw_image="$staging/rw.dmg"
vol_name="YT+"

hdiutil create -srcfolder "$staging/contents" -fs HFS+ -format UDRW -volname "$vol_name" "$rw_image" >/dev/null

attach_out="$(hdiutil attach "$rw_image" -readwrite -noverify -noautoopen)"
device="$(printf '%s\n' "$attach_out" | awk '/^\/dev\// {print $1; exit}')"
mount_point="$(printf '%s\n' "$attach_out" | awk -F'\t' '/\/Volumes\// {print $NF; exit}')"
mounted_name="$(basename "$mount_point")"

if [[ -f "$mount_point/.VolumeIcon.icns" ]]; then
    if command -v SetFile >/dev/null 2>&1; then
        SetFile -a C "$mount_point" || true
        SetFile -a V "$mount_point/.VolumeIcon.icns" || true
    elif [[ -x /usr/bin/SetFile ]]; then
        /usr/bin/SetFile -a C "$mount_point" || true
        /usr/bin/SetFile -a V "$mount_point/.VolumeIcon.icns" || true
    fi
fi

if [[ -f "$mount_point/.background/background.png" ]]; then
    osascript <<APPLESCRIPT || true
tell application "Finder"
    tell disk "$mounted_name"
        open
        set current view of container window to icon view
        set toolbar visible of container window to false
        set statusbar visible of container window to false
        set the bounds of container window to {220, 140, 840, 540}
        set opts to the icon view options of container window
        set arrangement of opts to not arranged
        set icon size of opts to 108
        set text size of opts to 13
        set background picture of opts to file ".background:background.png"
        set position of item "YT+.app" of container window to {175, 225}
        set position of item "Applications" of container window to {445, 225}
        update without registering applications
        delay 2
        close
    end tell
end tell
APPLESCRIPT
fi

sync
hdiutil detach "$device" -quiet || hdiutil detach "$device" -force -quiet
device=""

hdiutil convert "$rw_image" -format ULFO -o "$staging/$image" >/dev/null
hdiutil verify "$staging/$image"
mv -f "$staging/$image" "$project_dir/dist/$image"
printf '%s\n' "$project_dir/dist/$image"
