# Pour

Video and audio downloader that lives in the Windows tray. Powered by yt-dlp and ffmpeg, which are downloaded at runtime (about 150 MB, first run) into `%LOCALAPPDATA%\Pour\bin` and not bundled. C# WinForms, .NET Framework 4.8, no packages.

## Build and test
- `cd src && dotnet build -c Release`. On Linux it builds against Mono's reference assemblies (`mono-devel`).
- Version lives in `src/Pour.csproj` (`<Version>`). Bump it every release; Minibar compares it with the GitHub tag.
- Release asset: `Pour-x.y.z.zip` containing `Pour/Pour.exe`, `Pour.exe.config`, `README.txt`.
- Downloads save to `Downloads\Pour` by default. DRM-protected services can't be downloaded.

## Gotchas
- `Pour.exe --tray` starts hidden in the tray (this is what the Start with Windows entry uses). Keep that argument working.
- `Pour.exe --exit` closes it cleanly (message `Pour.Exit.v1`) and stops any running yt-dlp. Minibar's Stop and updates rely on it.
- The queue scrolls by pixels with smooth scrolling. Keep painting cheap.
- yt-dlp breaks when sites change; it is updated daily by Pour itself. Don't pin versions.
- Settings and queue live in `%APPDATA%\Pour` and `%LOCALAPPDATA%\Pour`. Never touch them in code or commits.

## Working rules (all Pingo apps)
- Apps: Echoa, Fizz, Juice, Pour, Pulp, Minibar. Minibar is the hub that finds, installs, starts, stops and updates the others from GitHub releases (iiiPingo/<app>). Don't change how an app is named, found, installed, updated or closed without checking it still works with Minibar.
- Never rely on the exe's file name. Discord and browsers rename downloads to `Pour (2).exe`. Identify an app by its version resource (ProductName), not by `<App>.exe`.
- Every release needs a zip with the app name in its file name, containing `<App>.exe`. Bump the exe's version every release.
- Never touch user data (settings.ini, logs, history, databases).
- This repo is public: never commit secrets, tokens, personal paths, build output (`bin/`, `obj/`), crash logs, settings.ini or nuget.config.
- Work on a branch. Don't push, open a PR or publish a release until the owner says so.
- Add a test for each fix that can be tested off Windows. At the end, list what can only be checked on real Windows (tray, downloads, notifications).
- If unsure a fix is safe, report it instead of changing it.
- Keep README text in plain words; mention what's new each version.
- Explain changes simply, in plain language.
