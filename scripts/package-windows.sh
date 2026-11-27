#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")/.."
project_dir="$PWD"
vendor="$project_dir/.build/windows/vendor"
mkdir -p "$vendor" dist
staging="$(mktemp -d "$project_dir/.build/windows/package.XXXXXX")"
trap 'rm -rf "$staging"' EXIT

dotnet="${YTPLUS_DOTNET:-}"
if [[ -z "$dotnet" ]]; then
    if command -v dotnet >/dev/null 2>&1; then
        dotnet="dotnet"
    elif [[ -x "$project_dir/.build/windows/dotnet-sdk/dotnet" ]]; then
        dotnet="$project_dir/.build/windows/dotnet-sdk/dotnet"
        export DOTNET_ROOT="$project_dir/.build/windows/dotnet-sdk"
    else
        echo "Error: dotnet SDK not found. Set YTPLUS_DOTNET or install .NET 10 SDK." >&2
        exit 1
    fi
fi

makensis="${YTPLUS_MAKENSIS:-}"
if [[ -z "$makensis" ]]; then
    if command -v makensis >/dev/null 2>&1; then
        makensis="makensis"
    elif [[ -x "$vendor/makensis/3.13/bin/makensis" ]]; then
        makensis="$vendor/makensis/3.13/bin/makensis"
        export NSISDIR="$vendor/makensis/3.13/share/nsis"
    else
        echo "Error: makensis not found. Set YTPLUS_MAKENSIS or install NSIS." >&2
        exit 1
    fi
fi

seven_zip="${YTPLUS_7ZIP:-}"
if [[ -z "$seven_zip" ]]; then
    if command -v 7zz >/dev/null 2>&1; then
        seven_zip="7zz"
    elif [[ -x "$vendor/7zip/7zz" ]]; then
        seven_zip="$vendor/7zip/7zz"
    else
        echo "Error: 7zz not found. Set YTPLUS_7ZIP or install 7-Zip." >&2
        exit 1
    fi
fi

export NUGET_PACKAGES="${NUGET_PACKAGES:-$project_dir/.build/windows/nuget}"
source_options=()
if [[ -n "${YTPLUS_NUGET_SOURCE:-}" ]]; then
    source_options=(--source "$YTPLUS_NUGET_SOURCE")
fi

verify() {
    printf '%s  %s\n' "$2" "$1" | shasum -a 256 -c - >/dev/null
}

fetch() {
    local filename="$1" url="$2" checksum="${3:-}"
    local target="$vendor/$filename" temporary="$staging/$filename"
    if [[ -s "$target" ]] && { [[ -z "$checksum" ]] || verify "$target" "$checksum"; }; then
        return
    fi
    curl --retry 2 --connect-timeout 15 -fsSL "$url" -o "$temporary"
    if [[ -n "$checksum" ]]; then verify "$temporary" "$checksum"; fi
    mv -f "$temporary" "$target"
}

yt_version=2026.10.26
node_version=24.22.1
ffmpeg_version=9.0.2
ffmpeg_name="ffmpeg-$ffmpeg_version-essentials_build"
fetch yt-dlp.exe "https://github.com/yt-dlp/yt-dlp/releases/download/$yt_version/yt-dlp.exe" \
    f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d
fetch node.exe "https://nodejs.org/dist/v$node_version/win-x64/node.exe" \
    d702daa815eabbff763c7d3808e235cf031cf2ca70211b56847bc72f51e240e5
fetch ffmpeg.7z "https://www.gyan.dev/ffmpeg/builds/packages/$ffmpeg_name.7z" \
    4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077
fetch node-license.txt "https://raw.githubusercontent.com/nodejs/node/v$node_version/LICENSE"
fetch yt-dlp-license.txt "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$yt_version/LICENSE"
fetch yt-dlp-third-party.txt "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$yt_version/THIRD_PARTY_LICENSES.txt"

app="$staging/app"
"$dotnet" publish windows/YTPlus.csproj -c Release -r win-x64 --self-contained true \
    ${source_options[@]+"${source_options[@]}"} -o "$app"
tools="$app/tools"
notices="$app/licenses"
mkdir -p "$tools" "$notices"
"$seven_zip" x -y "-o$staging/ffmpeg" "$vendor/ffmpeg.7z" >/dev/null
ffmpeg="$staging/ffmpeg/$ffmpeg_name"
cp "$ffmpeg/bin/ffmpeg.exe" "$ffmpeg/bin/ffprobe.exe" "$vendor/yt-dlp.exe" "$vendor/node.exe" "$tools/"
cp "$ffmpeg/LICENSE" "$notices/FFmpeg-LICENSE.txt"
cp "$ffmpeg/README.txt" "$notices/FFmpeg-README.txt"
cp "$vendor/node-license.txt" "$notices/Node-LICENSE.txt"
cp "$vendor/yt-dlp-license.txt" "$notices/yt-dlp-LICENSE.txt"
cp "$vendor/yt-dlp-third-party.txt" "$notices/yt-dlp-THIRD-PARTY.txt"
cp windows/ThirdPartySources.txt "$notices/ThirdPartySources.txt"

version="$("$dotnet" msbuild windows/YTPlus.csproj -getProperty:Version -nologo)"
filename="YTPlus-$version-win-x64-Setup.exe"
"$makensis" -WX "-DAPP_VERSION=$version" "-DAPP_SOURCE=$app" \
    "-DSETUP_OUTPUT=$staging/$filename" windows/Setup.nsi
mv -f "$staging/$filename" "$project_dir/dist/$filename"
printf '%s\n' "$project_dir/dist/$filename"
