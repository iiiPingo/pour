<p align="center"><img src="docs/logo.png" width="96" alt="Pour logo"></p>
<h1 align="center">Pour</h1>
<p align="center">A video and audio downloader that lives in the Windows tray. Powered by yt-dlp and ffmpeg.</p>
<p align="center"><a href="../../releases/latest"><b>Download the latest release</b></a></p>

## New in 1.4

- New GIF option next to MP4 and MP3. Pick From and To times, up to 60 seconds, and Pour saves just that part as a GIF.
- Works with Pour & open, so the GIF can open as soon as it is ready.

## New in 1.3

- New look: Lexend, the same font as Minibar, with sharper text.
- New Settings box (gear icon) with clear switches, replacing the old menu.
- New "Pour & open" button (or Ctrl+Enter): the video or song opens by itself when it finishes. A playlist opens its folder. Several links open the download folder once, after the last one.
- Fixed: notification clicks that sometimes did nothing.
- Fixed: Cancel could freeze the window for a few seconds.
- Fixed: Retry could open the previous file or show old playlist numbers.
- Fixed: clicks left over from a closed menu could press a button.
- Fixed: links copied with a comma or dot at the end failed.
- Fixed: Start with Windows stopped working after moving Pour to another folder.
- Fixed: with clipboard watching off, Pour no longer reads the clipboard while closed.

## Features
- **MP4 or MP3**, in Best / 2K / 1080p / 720p, or a **GIF** of any part up to 60 seconds.
- **Copy a link anywhere** and click the notification to download it, or press **Ctrl+Alt+D**.
- Paste several links at once, or drag them from the browser onto the panel.
- **Queue** with 1 to 4 downloads at once, cancel / retry / open / show in folder.
- Whole playlists go into their own numbered folder.
- Embeds cover art and info, sets up yt-dlp + ffmpeg by itself on first run and keeps yt-dlp updated daily.
- Tray ring fills while downloading, flashes green when done.

## Install
1. Download `Pour-1.4.0.zip` from [Releases](../../releases/latest) and unzip it into its own folder.
2. Run `Pour.exe`. If SmartScreen warns, click **More info > Run anyway** (the exe is unsigned).
3. The first launch downloads yt-dlp and ffmpeg (about 150 MB, one time) into `%LOCALAPPDATA%\Pour\bin`.

Files save to `Downloads\Pour` by default (change it from the gear menu). DRM-protected services can't be downloaded.

## Build
```
cd src
dotnet build -c Release
```
On Linux it builds against Mono's .NET Framework 4.8 reference assemblies (`mono-devel`).

## Credits
[yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://ffmpeg.org) do the heavy lifting; they are downloaded at runtime, not bundled.

## License
MIT
