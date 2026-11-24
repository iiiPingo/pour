# Pour

Video and audio downloader that lives in the Windows tray. Powered by yt-dlp and ffmpeg, which are downloaded at runtime (about 150 MB, first run) into `%LOCALAPPDATA%\Pour\bin` and not bundled. C# WinForms, .NET Framework 4.8, no packages.

## Build and test
- `cd src && dotnet build -c Release`. On Linux it builds against Mono's reference assemblies (`mono-devel`).
- Version lives in `src/Pour.csproj` (`<Version>`). Bump it every release; Minibar compares it with the GitHub tag.
- Release asset: `Pour-x.y.z.zip` containing `Pour/Pour.exe`, `Pour.exe.config`, `README.txt`.
- Tests: build `tests/PourTests.csproj` with `dotnet build -c Release`, then run `PourTests.exe <output folder>` (mono on Linux). It writes `results.txt` and pictures of the panel, and never reads or saves the real settings.
- Lexend (the same files as Minibar, SIL Open Font License, `src/fonts/OFL.txt`) is built into `Pour.exe` and loaded from it in `Core.cs`. Nothing is installed on the PC.
- Downloads save to `Downloads\Pour` by default. DRM-protected services can't be downloaded.

## Gotchas
- `Pour.exe --tray` starts hidden in the tray (this is what the Start with Windows entry uses). Keep that argument working.
- `Pour.exe --exit` closes it cleanly (message `Pour.Exit.v1`) and stops any running yt-dlp. Minibar's Stop and updates rely on it.
- The queue scrolls by pixels with smooth scrolling. Keep painting cheap.
- yt-dlp breaks when sites change; it is updated daily by Pour itself. Don't pin versions.
- Settings and queue live in `%APPDATA%\Pour` and `%LOCALAPPDATA%\Pour`. Never touch them in code or commits.
- GIF (1.4): From and To of at most 60 seconds (`MaxGifSeconds`). yt-dlp downloads only that section into its own temp folder (`Pour-gif-<id>`), ffmpeg turns it into the GIF (`GifFilter`), and the result goes to the download folder without overwriting (`Name.gif`, then `Name (2).gif`). A GIF is never combined with MP3, playlists or embedded extras.
- Pour & open (Ctrl+Enter) opens the file when it finishes. A playlist opens its folder, and several links open the download folder once, after the last one.

## Releasing
When a change is ready to ship, leave it fully done, so the release can go out without rebuilding or repackaging:
1. Bump the version (see Build and test), add the "what's new" to the README and to `README.txt` in the zip (a "NEW IN x.y.z" section at the top, plain words), and run the tests.
2. Build the exe from the final source, last, after the final change. The exe you leave is the one that ships. Copy it to `C:\Claude Projects\Pour\Pour.exe` so it can be tried.
3. Build the full release zip `Pour-x.y.z.zip`: one top folder `Pour/` with `Pour.exe`, `Pour.exe.config` and `README.txt`. Save it to `C:\Claude Projects\Releases\Pour-x.y.z\`. A zip with only the exe is not a release zip.
4. Push the branch. Push to `main` when the owner says push or release. Never force-push. Tags are plain versions (`vX.Y.Z`) and are created on GitHub when the owner publishes.
5. Do not create the GitHub release or tag yourself. The owner attaches the zip and clicks Publish. The "My project apps" chat prepares the release page and checks it afterwards; it does not rebuild.
6. Finish with one message: what changed in short plain bullets (these become the release notes, no dashes), the version, the exe's SHA256, the zip path, the tests run, and what only real Windows can check.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Pour (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so. "Push", "release" and "update GitHub" count as that go-ahead: then push to `main`.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (tray, downloads, notifications).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
