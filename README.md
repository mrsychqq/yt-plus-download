# YT+

A native desktop app for **macOS 14+ (Apple Silicon)** and **64-bit Windows 10/11** that downloads individual videos and audio from **YouTube**, **TikTok**, and **Pinterest** — plus original-quality photos and GIFs from Pinterest.

Supported video formats: **MP4, MKV, WebM, MOV, AVI** (144p up to 8K / Best available).  
Supported audio formats: **MP3, M4A, Opus, WAV, FLAC**.

## Download

Grab the latest installer from the [Releases](https://github.com/mrsychqq/yt-plus-download/releases) page:

- **macOS (Apple Silicon):** `YTPlus-1.3.0-macOS-arm64.dmg`
- **Windows (x64):** `YTPlus-1.3.0-Windows-x64-Setup.exe` (bundles `yt-dlp`, `FFmpeg`, and `Node.js` — no extra setup needed)

## Installation & Usage

1. **Install:**
   - On **macOS**, open the DMG and drag **YT+** into **Applications**.
   - On **Windows**, run `YTPlus-1.3.0-Windows-x64-Setup.exe`. The installer lets you choose the installation directory, optionally creates Start Menu and Desktop shortcuts, and bundles all required tools.
2. **Theme & Language:** Use the buttons in the top-right corner of the header to switch between **System / Light / Dark** themes and **EN / RU** interface languages. Your selection is saved automatically.
3. **Paste a Link:** Pick the **YouTube**, **TikTok**, or **Pinterest** tab and paste a video or pin link (the platform switches automatically when you paste a link). Supports standard YouTube URLs, Shorts, and `youtu.be`; TikTok links (`tiktok.com`, `vm.tiktok.com`, `vt.tiktok.com`); and Pinterest video or photo pins (`pinterest.*`, `pin.it`). When a Pinterest pin contains an image, the original (`JPG`, `PNG`, `WebP`, or `GIF`) is downloaded automatically.
4. **Choose Format & Folder:** Select a format, resolution cap (for video), and output folder. Defaults to MP4, up to 1080p, and your Downloads folder.
5. **Download:** Click **Download**. Once finished, click **Show in Finder** (macOS) or **Show in Folder** (Windows) to reveal the file.

### Features

- **Resolution limits:** 144p, 240p, 360p, 480p, 720p, 1080p, 1440p, 2160p (4K), 4320p (8K), and **Best available**, handled properly for both horizontal and vertical videos (Shorts, TikTok, Pinterest).
- **Auto-resume on connection loss:** Downloads up to four concurrent fragments for DASH and HLS streams. If your internet connection drops, YT+ keeps partial files, waits for connectivity to return, and automatically resumes from where it left off.
- **Lossless audio preference:** For M4A and Opus, prefers matching source audio streams to avoid unnecessary re-encoding whenever possible.

## macOS Dependencies

On macOS, YT+ uses locally installed `yt-dlp`, `FFmpeg` (with `FFprobe`), and `Deno` or `Node.js`. Install them with Homebrew:

```sh
brew install yt-dlp ffmpeg deno
```

YT+ checks your system `PATH`, Homebrew directories, `~/.local/bin`, `~/.deno/bin`, and `~/.local/node/bin`. See the [yt-dlp installation guide](https://github.com/yt-dlp/yt-dlp/wiki/Installation) and [JavaScript runtime setup](https://github.com/yt-dlp/yt-dlp/wiki/EJS).

## Building from Source

### macOS

Requires Xcode Command Line Tools:

```sh
bash scripts/build.sh
open "dist/YT+.app"
```

Run `bash scripts/package.sh` to also build a DMG in `dist/`.

### Windows (cross-built on macOS)

Requires .NET 10 SDK, NSIS (`makensis`), and 7-Zip (`7zz`):

```sh
bash scripts/package-windows.sh
```

The script verifies SHA-256 checksums for bundled tools and packages them together with the Windows x64 application, .NET runtime, and third-party license files. Shared formats, resolutions, and `yt-dlp` options live in `Resources/DownloadProfiles.json`.

## License

[MIT](LICENSE)
