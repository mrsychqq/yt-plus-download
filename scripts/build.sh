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

find_tool() {
    local name="$1"
    local dir
    for dir in \
        "$HOME/.local/bin" \
        "$HOME/.local/node/bin" \
        "$HOME/.deno/bin" \
        "/opt/homebrew/bin" \
        "/usr/local/bin"
    do
        if [[ -x "$dir/$name" && ! -d "$dir/$name" ]]; then
            printf '%s\n' "$dir/$name"
            return 0
        fi
    done
    command -v "$name" 2>/dev/null || return 1
}

if [[ "${YTPLUS_BUNDLE_TOOLS:-0}" == "1" ]]; then
    tools_dir="$bundle/Contents/Resources/tools"
    mkdir -p "$tools_dir"

    yt_dlp_src="$(find_tool yt-dlp || true)"
    ffmpeg_src="$(find_tool ffmpeg || true)"
    ffprobe_src="$(find_tool ffprobe || true)"
    js_src="$(find_tool deno || find_tool node || true)"

    if [[ -z "$yt_dlp_src" || -z "$ffmpeg_src" || -z "$ffprobe_src" || -z "$js_src" ]]; then
        echo "Missing required tools for bundling (need yt-dlp, ffmpeg, ffprobe, and deno or node)." >&2
        exit 1
    fi

    js_name="$(basename "$js_src")"
    cp -L "$yt_dlp_src" "$tools_dir/yt-dlp"
    cp -L "$ffmpeg_src" "$tools_dir/ffmpeg"
    cp -L "$ffprobe_src" "$tools_dir/ffprobe"
    cp -L "$js_src" "$tools_dir/$js_name"
    chmod 755 "$tools_dir/yt-dlp" "$tools_dir/ffmpeg" "$tools_dir/ffprobe" "$tools_dir/$js_name"
    xattr -cr "$tools_dir" 2>/dev/null || true

    # Ad-hoc sign bundled executables (keep yt-dlp's embedded PyInstaller payload intact if already signed)
    for bin in "$tools_dir/ffmpeg" "$tools_dir/ffprobe" "$tools_dir/$js_name"; do
        codesign --force --sign - "$bin"
    done
    if ! codesign --verify --strict "$tools_dir/yt-dlp" 2>/dev/null; then
        codesign --force --sign - "$tools_dir/yt-dlp"
    fi
fi

codesign --force --sign - "$bundle"
codesign --verify --deep --strict "$bundle"

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
