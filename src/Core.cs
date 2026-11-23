using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pour
{
    // ---------------------------------------------------------------- theme
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 32);
        public static readonly Color Card = Color.FromArgb(45, 45, 45);
        public static readonly Color CardSoft = Color.FromArgb(39, 39, 39);
        public static readonly Color Field = Color.FromArgb(52, 52, 52);
        public static readonly Color CardHover = Color.FromArgb(58, 58, 58);
        public static readonly Color Selected = Color.FromArgb(72, 72, 72);
        public static readonly Color Border = Color.FromArgb(62, 62, 62);
        public static readonly Color Text = Color.FromArgb(242, 242, 242);
        public static readonly Color Sub = Color.FromArgb(170, 170, 170);
        public static readonly Color Dim = Color.FromArgb(120, 120, 120);
        public static readonly Color Accent = Color.FromArgb(208, 100, 74);
        public static readonly Color AccentHover = Color.FromArgb(226, 122, 95);
        public static readonly Color AccentText = Color.FromArgb(255, 248, 244);
        public static readonly Color LogoA = Color.FromArgb(92, 26, 27);
        public static readonly Color LogoB = Color.FromArgb(196, 96, 62);
        public static readonly Color Green = Color.FromArgb(76, 195, 138);
        public static readonly Color Red = Color.FromArgb(240, 96, 96);
        public static readonly Color Track = Color.FromArgb(68, 68, 68);

        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

        // Lexend (built into the exe, same as Minibar): Medium for normal text, Bold for names, headings and buttons
        public static Font Ui(float size) => AppFonts.Make(AppFonts.Medium, size, false) ?? new Font(UiFamily, size);
        public static Font UiBold(float size) => AppFonts.Make(AppFonts.Bold, size, true) ?? new Font(UiFamily, size, FontStyle.Bold);

        static bool FontExists(string name)
        {
            try { using (var f = new Font(name, 10)) return f.Name == name; } catch { return false; }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, Color c, float rad)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, Color c, float rad, float w = 1)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        // app logo: red-brown squircle with a pour-over dripper
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            using (var path = Round(r, r.Width * 0.24f))
            using (var b = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom), LogoA, LogoB))
                g.FillPath(b, path);
            DrawDripper(g, new RectangleF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.22f, r.Width * 0.52f, r.Height * 0.57f), Color.White, r.Width >= 40 ? (Color?)LogoA : null);
        }

        // dripper glyph inside a box: rim, cone, falling drop, cup line
        public static void DrawDripper(Graphics g, RectangleF box, Color c, Color? rib = null)
        {
            Func<float, float, PointF> P = (u, v) => new PointF(box.X + u * box.Width, box.Y + v * box.Height);
            using (var b = new SolidBrush(c))
            {
                FillRound(g, b, RectangleF.FromLTRB(box.X, box.Y, box.Right, box.Y + box.Height * 0.123f), box.Height * 0.05f);
                g.FillPolygon(b, new[] { P(0.096f, 0.10f), P(0.904f, 0.10f), P(0.587f, 0.526f), P(0.413f, 0.526f) });
                g.FillPolygon(b, Drop(box.X + box.Width * 0.5f, box.Y + box.Height * 0.585f, box.Height * 0.22f));
                FillRound(g, b, RectangleF.FromLTRB(P(0.115f, 0).X, P(0, 0.877f).Y, P(0.885f, 0).X, box.Bottom), box.Height * 0.06f);
            }
            if (rib.HasValue)
                using (var pen = new Pen(rib.Value, Math.Max(1f, box.Width * 0.03f)))
                    for (int k = -1; k <= 1; k++) g.DrawLine(pen, P(0.5f + k * 0.144f, 0.175f), P(0.5f + k * 0.048f, 0.44f));
        }

        static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            using (var p = Round(r, Math.Max(0.5f, rad))) g.FillPath(b, p);
        }

        // teardrop, point up, top at (cx, top), total height h
        static PointF[] Drop(float cx, float top, float h)
        {
            float w = h * 0.72f;
            var pts = new PointF[64];
            for (int i = 0; i < pts.Length; i++)
            {
                double t = 2 * Math.PI * i / pts.Length;
                double x = Math.Sin(t) * Math.Sin(t / 2), y = -Math.Cos(t);
                pts[i] = new PointF(cx + (float)(x * w * 0.62), top + (float)((y + 1) / 2 * h));
            }
            return pts;
        }
    }

    // ---------------------------------------------------------------- Lexend, built into the exe
    // One PrivateFontCollection per file (GDI+ Font objects) plus AddFontMemResourceEx (so TextRenderer and the
    // link box find the same faces). The memory stays allocated for the life of the process. Lexend-Medium.ttf is
    // family "Lexend Medium" (Regular style); Lexend-Bold.ttf is family "Lexend" with only a Bold face.
    static class AppFonts
    {
        public static FontFamily Medium, Bold;
        static readonly System.Drawing.Text.PrivateFontCollection[] pfc =
            { new System.Drawing.Text.PrivateFontCollection(), new System.Drawing.Text.PrivateFontCollection() };

        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pb, uint cb, IntPtr pdv, ref uint pcFonts);

        static AppFonts()
        {
            try
            {
                var names = new[] { "fonts.Lexend-Medium.ttf", "fonts.Lexend-Bold.ttf" };
                for (int i = 0; i < names.Length; i++)
                {
                    byte[] data;
                    using (var st = typeof(AppFonts).Assembly.GetManifestResourceStream(names[i]))
                    {
                        if (st == null) return;
                        data = new byte[st.Length];
                        int read = 0;
                        while (read < data.Length) { int n = st.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                    }
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length);   // never freed: GDI and GDI+ read from it
                    Marshal.Copy(data, 0, mem, data.Length);
                    pfc[i].AddMemoryFont(mem, data.Length);
                    uint count = 0;
                    if (AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref count) == IntPtr.Zero) return;
                }
                if (pfc[0].Families.Length == 0 || pfc[1].Families.Length == 0) return;
                Medium = pfc[0].Families[0];
                Bold = pfc[1].Families[0];
            }
            catch { Medium = Bold = null; }
        }

        public static Font Make(FontFamily f, float size, bool bold)
        {
            if (f == null) return null;
            try
            {
                var want = bold ? FontStyle.Bold : FontStyle.Regular;
                var other = bold ? FontStyle.Regular : FontStyle.Bold;
                var style = f.IsStyleAvailable(want) ? want : f.IsStyleAvailable(other) ? other : (FontStyle?)null;
                return style == null ? null : new Font(f, size, style.Value);
            }
            catch { return null; }
        }
    }

    // ---------------------------------------------------------------- native
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string msg);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public static readonly int WM_SHOWPOUR = RegisterWindowMessage("Pour.ShowPanel.v1");
        public static readonly int WM_EXITPOUR = RegisterWindowMessage("Pour.Exit.v1");   // "Pour.exe --exit" (Minibar's Stop / updates)
    }

    // ---------------------------------------------------------------- settings
    class Settings
    {
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Pour");
        public bool Mp3 = false;
        public string Quality = "1080";   // best | 1440 | 1080 | 720
        public bool Playlist = false;
        public bool WatchClipboard = true;
        public bool EmbedExtras = true;
        public bool AutoUpdate = true;
        public int Parallel = 2;
        public DateTime LastUpdate = DateTime.MinValue;
        public bool FirstRun;

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pour");
        static string FilePath => Path.Combine(Dir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "folder": if (v.Length > 0) s.Folder = v; break;
                        case "mp3": s.Mp3 = v == "1"; break;
                        case "quality": if (new[] { "best", "1440", "1080", "720" }.Contains(v)) s.Quality = v; break;
                        case "playlist": s.Playlist = v == "1"; break;
                        case "clipboard": s.WatchClipboard = v == "1"; break;
                        case "extras": s.EmbedExtras = v == "1"; break;
                        case "autoupdate": s.AutoUpdate = v == "1"; break;
                        case "parallel": if (int.TryParse(v, out int p) && p >= 1 && p <= 4) s.Parallel = p; break;
                        case "lastupdate": if (long.TryParse(v, out long t)) s.LastUpdate = new DateTime(t); break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, string.Join("\r\n", new[]
                {
                    "folder=" + Folder, "mp3=" + B(Mp3), "quality=" + Quality, "playlist=" + B(Playlist),
                    "clipboard=" + B(WatchClipboard), "extras=" + B(EmbedExtras), "autoupdate=" + B(AutoUpdate),
                    "parallel=" + Parallel, "lastupdate=" + LastUpdate.Ticks
                }), Encoding.UTF8);
            }
            catch { }
        }
        static string B(bool b) => b ? "1" : "0";

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool StartWithWindows
        {
            get { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue("Pour") != null; } catch { return false; } }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("Pour", "\"" + Application.ExecutablePath + "\" --tray");
                        else k.DeleteValue("Pour", false);
                    }
                }
                catch { }
            }
        }

        // the app was moved or renamed: point Start with Windows at this copy, only if the old path is gone
        public static void RepairRunPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (!(k?.GetValue("Pour") is string v) || v.Length == 0) return;
                    string old = v[0] == '"' ? v.Substring(1, v.IndexOf('"', 1) - 1) : v.Split(' ')[0];
                    if (!File.Exists(old)) k.SetValue("Pour", "\"" + Application.ExecutablePath + "\" --tray");
                }
            }
            catch { }
        }

        public string FormatLabel(bool mp3, string quality) => mp3 ? "MP3" : "MP4 · " + QualityLabel(quality);
        public static string QualityLabel(string q) => q == "best" ? "Best" : q == "1440" ? "2K" : q + "p";
    }

    // ---------------------------------------------------------------- tools (yt-dlp + ffmpeg)
    static class Tools
    {
        public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pour", "bin");
        public static string YtDlp => Path.Combine(Dir, "yt-dlp.exe");
        public static string FfmpegDir = Dir;
        static string BundledFfmpeg => Path.Combine(Dir, "ffmpeg.exe");

        const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        static string FindFfmpegOnPath()
        {
            try
            {
                foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    var dir = d.Trim().Trim('"');
                    if (File.Exists(Path.Combine(dir, "ffmpeg.exe")) && File.Exists(Path.Combine(dir, "ffprobe.exe"))) return dir;
                }
            }
            catch { }
            return null;
        }

        public static bool Ready => File.Exists(YtDlp) && File.Exists(Path.Combine(FfmpegDir, "ffmpeg.exe"));

        public static async Task EnsureAsync(Action<string> status)
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(YtDlp))
            {
                await Download(YtDlpUrl, YtDlp, p => status("Setting up · getting yt-dlp " + p));
            }
            if (!File.Exists(BundledFfmpeg))
            {
                var onPath = FindFfmpegOnPath();
                if (onPath != null) { FfmpegDir = onPath; return; }
                string zip = Path.Combine(Dir, "ffmpeg.zip");
                await Download(FfmpegUrl, zip, p => status("Setting up · getting ffmpeg " + p));
                status("Setting up · unpacking ffmpeg");
                await Task.Run(() => Unzip(zip));
                if (!File.Exists(BundledFfmpeg)) throw new Exception("couldn't unpack ffmpeg");
            }
            FfmpegDir = Dir;
        }

        static void Run(string exe, string args, int timeoutMs)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir }))
                if (!p.WaitForExit(timeoutMs)) try { p.Kill(); } catch { }
        }

        static void Unzip(string zip)
        {
            string tmp = Path.Combine(Dir, "ffmpeg_unpack");
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (File.Exists(tar)) Run(tar, "-xf \"" + zip + "\" -C \"" + tmp + "\"", 300000);
                if (!Directory.EnumerateFiles(tmp, "ffmpeg.exe", SearchOption.AllDirectories).Any())
                    Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" + zip + "' -DestinationPath '" + tmp + "' -Force\"", 300000);
                foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    var src = Directory.EnumerateFiles(tmp, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (src == null) continue;
                    string dst = Path.Combine(Dir, name);
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
                try { File.Delete(zip); } catch { }
            }
        }

        static async Task Download(string url, string dest, Action<string> progress)
        {
            string tmp = dest + ".part";
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Pour/1.0";
                wc.DownloadProgressChanged += (s, e) =>
                {
                    if (e.TotalBytesToReceive > 0) progress(e.ProgressPercentage + "%");
                    else progress(Fmt.Size(e.BytesReceived));
                };
                await wc.DownloadFileTaskAsync(new Uri(url), tmp);
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        // runs "yt-dlp -U"; returns a short human message
        public static Task<string> UpdateYtDlpAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(YtDlp, "-U --no-colors")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    };
                    using (var p = Process.Start(psi))
                    {
                        var outTask = p.StandardOutput.ReadToEndAsync();
                        var errTask = p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return "Update timed out"; }
                        string all = outTask.Result + "\n" + errTask.Result;
                        if (all.Contains("is up to date")) return "yt-dlp is up to date";
                        var m = Regex.Match(all, @"Updated yt-dlp to\s+(\S+)");
                        if (m.Success) return "yt-dlp updated to " + m.Groups[1].Value.Trim();
                        return p.ExitCode == 0 ? "yt-dlp checked for updates" : "yt-dlp update failed";
                    }
                }
                catch (Exception ex) { return "Update failed: " + ex.Message; }
            });
        }
    }

    static class Fmt
    {
        public static string Size(double b)
        {
            if (double.IsNaN(b) || b < 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i >= 2 ? b.ToString("0.0", CultureInfo.InvariantCulture) : b.ToString("0", CultureInfo.InvariantCulture)) + " " + u[i];
        }
        public static string Speed(double bps) => double.IsNaN(bps) || bps <= 0 ? "" : Size(bps) + "/s";
        public static string Eta(double sec)
        {
            if (double.IsNaN(sec) || sec < 0 || sec > 360000) return "";
            var t = TimeSpan.FromSeconds(Math.Round(sec));
            return (t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") : t.Minutes.ToString()) + ":" + t.Seconds.ToString("00") + " left";
        }
        public static double Num(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "NA" || s == "None") return double.NaN;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;
        }
        public static string ShortUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                string host = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
                string rest = u.PathAndQuery.Length > 40 ? u.PathAndQuery.Substring(0, 40) + "…" : u.PathAndQuery;
                return host + rest;
            }
            catch { return url; }
        }

        static readonly Regex UrlRx = new Regex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase);

        // a link pasted from a sentence often carries the sentence's punctuation
        static string CleanUrl(string u) => u.TrimEnd('.', ',', ';', '!', '?');

        // strict: the text must be nothing but links (used for the clipboard watcher)
        public static List<string> OnlyUrls(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 8000) return list;
            if (UrlRx.Replace(text, "").Trim().Length > 0) return list;
            foreach (Match m in UrlRx.Matches(text)) { string u = CleanUrl(m.Value); if (!list.Contains(u)) list.Add(u); }
            return list;
        }

        // loose: anything that looks like a link inside the text
        public static List<string> AnyUrls(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (Match m in UrlRx.Matches(text)) { string u = CleanUrl(m.Value); if (!list.Contains(u)) list.Add(u); }
            if (list.Count == 0)
            {
                string t = text.Trim();
                if (!t.Contains(" ") && t.Contains(".") && t.Length > 4) list.Add("https://" + t);
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- jobs
    enum JobState { Queued, Running, Done, Failed, Canceled }

    class Job
    {
        static int nextId = 1;
        public readonly int Id = nextId++;
        public string Url;
        public bool Mp3;
        public string Quality;
        public bool Playlist;
        public string Title;
        public JobState State = JobState.Queued;
        public double Progress;          // 0..1 or -1 when unknown
        public string Phase = "";        // Video / Audio / Finishing
        public double Speed = double.NaN, Eta = double.NaN, Total = double.NaN;
        public string Error;
        public string FilePath;
        public int Index, Count, FilesDone;
        public Process Proc;
        public bool CancelRequested, RemoveAfter;
        public bool OpenWhenDone;        // "Pour & open" on one link
        public int OpenGroup;            // "Pour & open" on several links: the folder opens once when all are finished

        // a playlist result opens its folder; a single video or song opens itself
        public bool OpensFolder => FilesDone > 1 || (Playlist && Count > 1);
        public string ResultFolder
        {
            get { try { return string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath); } catch { return null; } }
        }

        public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : Fmt.ShortUrl(Url);
        public bool Active => State == JobState.Queued || State == JobState.Running;
    }

    class Downloader
    {
        public readonly List<Job> Jobs = new List<Job>();   // newest first
        public event Action Changed;
        public event Action<Job> Finished;
        readonly SynchronizationContext ui;
        readonly Settings s;
        public bool ToolsReady;

        public Downloader(Settings settings)
        {
            s = settings;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        }

        void Post(Action a) => ui.Post(_ => { try { a(); } catch (Exception ex) { Log.Crash(ex); } }, null);
        void Raise() => Changed?.Invoke();

        public bool HasActive(string url) => Jobs.Any(j => j.Active && j.Url == url);

        public Job Add(string url, bool openWhenDone = false, int openGroup = 0)
        {
            var running = Jobs.FirstOrDefault(x => x.Active && x.Url == url);
            if (running != null)
            {
                // already pouring: "Pour & open" still applies to it
                if (openWhenDone || openGroup > 0) { running.OpenWhenDone = openWhenDone; running.OpenGroup = openGroup; Raise(); }
                return null;
            }
            var j = new Job { Url = url, Mp3 = s.Mp3, Quality = s.Quality, Playlist = s.Playlist, OpenWhenDone = openWhenDone, OpenGroup = openGroup };
            Jobs.Insert(0, j);
            Pump();
            return j;
        }

        int lastGroup;

        // one link opens when done; several links open the download folder once, after the last one
        public void AddAndOpen(IList<string> urls)
        {
            if (urls.Count == 1) { Add(urls[0], true); return; }
            int group = ++lastGroup;
            foreach (var u in urls) Add(u, false, group);
        }

        // returns true when a "Pour & open" batch just finished with at least one file
        public bool SettleOpenGroups()
        {
            bool open = false;
            foreach (var g in Jobs.Where(x => x.OpenGroup > 0).GroupBy(x => x.OpenGroup).ToList())
            {
                if (g.Any(x => x.Active)) continue;
                if (g.Any(x => x.State == JobState.Done)) open = true;
                foreach (var x in g) x.OpenGroup = 0;
            }
            return open;
        }

        public void Pump()
        {
            if (ToolsReady)
            {
                int running = Jobs.Count(j => j.State == JobState.Running);
                for (int i = Jobs.Count - 1; i >= 0 && running < s.Parallel; i--)
                {
                    if (Jobs[i].State == JobState.Queued) { Start(Jobs[i]); running++; }
                }
            }
            Raise();
        }

        public void Cancel(Job j)
        {
            if (j.State == JobState.Queued) { j.State = JobState.Canceled; Raise(); return; }
            if (j.State != JobState.Running) return;
            j.CancelRequested = true;
            var proc = j.Proc;
            Task.Run(() => KillTree(proc));     // taskkill can take seconds: never on the UI thread
            Raise();
        }

        public void Retry(Job j)
        {
            if (j.Active) return;
            j.State = JobState.Queued; j.Error = null; j.Progress = 0; j.Phase = ""; j.CancelRequested = false;
            j.Speed = j.Eta = double.NaN;
            Pump();
        }

        public void Remove(Job j)
        {
            if (j.State == JobState.Running) { j.RemoveAfter = true; Cancel(j); return; }
            Jobs.Remove(j);
            Raise();
        }

        public void ClearFinished()
        {
            Jobs.RemoveAll(j => !j.Active);
            Raise();
        }

        public void KillAll()
        {
            foreach (var j in Jobs.Where(j => j.State == JobState.Running)) { j.CancelRequested = true; KillTree(j.Proc); }
        }

        static void KillTree(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return;
                using (var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false }))
                    k?.WaitForExit(3000);
            }
            catch { }
        }

        static string Q(string a)
        {
            // quote one argument for the Windows command line
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static List<string> BuildArgs(Job j, Settings s, string ffmpegDir)
        {
            var a = new List<string>
            {
                "--newline", "--no-colors", "--progress", "--no-simulate", "--encoding", "utf-8",
                "--ffmpeg-location", ffmpegDir,
                "-P", s.Folder,
                "--concurrent-fragments", "8",
                "--retries", "10", "--fragment-retries", "10",
                "--no-mtime",
                "--progress-template", "download:[pour]%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(info.vcodec)s",
                "--print", "before_dl:[title]%(title)s",
                "--print", "before_dl:[item]%(playlist_index|)s|%(n_entries|)s",
                "--print", "after_move:[file]%(filepath)s",
            };

            if (j.Mp3)
            {
                a.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0" });
            }
            else
            {
                string res = j.Quality == "best" ? "res" : "res:" + j.Quality;
                a.AddRange(new[] { "-f", "bv*+ba/b", "-S", res + ",fps,vcodec:h264,acodec:aac", "--merge-output-format", "mp4", "--remux-video", "mp4" });
            }

            if (s.EmbedExtras)
            {
                a.AddRange(new[] { "--embed-metadata", "--embed-thumbnail" });
                if (!j.Mp3) a.Add("--embed-chapters");
            }

            if (j.Playlist)
            {
                a.AddRange(new[] { "--yes-playlist", "-o", "%(playlist_title,playlist_id|Singles)s/%(playlist_index&{:03d} - |)s%(title).120B.%(ext)s" });
            }
            else
            {
                a.AddRange(new[] { "--no-playlist", "-o", "%(title).150B.%(ext)s" });
            }

            a.Add("--");
            a.Add(j.Url);
            return a;
        }

        void Start(Job j)
        {
            j.State = JobState.Running; j.Progress = -1; j.Phase = "Starting"; j.Error = null; j.FilesDone = 0;
            j.FilePath = null; j.Index = j.Count = 0; j.Total = double.NaN;     // a retry must not reuse the last run's file
            try
            {
                Directory.CreateDirectory(s.Folder);
                string args = string.Join(" ", BuildArgs(j, s, Tools.FfmpegDir).Select(Q));
                var psi = new ProcessStartInfo(Tools.YtDlp, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = s.Folder
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (o, e) => { if (e.Data != null) Post(() => OnOut(j, e.Data)); };
                p.ErrorDataReceived += (o, e) => { if (e.Data != null) Post(() => OnErr(j, e.Data)); };
                p.Start();
                j.Proc = p;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                Task.Run(() =>
                {
                    try { p.WaitForExit(); } catch { }
                    int code = -1;
                    try { code = p.ExitCode; } catch { }
                    Post(() => { OnExit(j, code); try { p.Dispose(); } catch { } });
                });
            }
            catch (Exception ex)
            {
                j.State = JobState.Failed; j.Error = ex.Message;
                Finished?.Invoke(j);
            }
        }

        void OnOut(Job j, string line)
        {
            if (line.StartsWith("[pour]"))
            {
                var p = line.Substring(6).Split('|');
                if (p.Length < 7) return;
                double done = Fmt.Num(p[1]), total = Fmt.Num(p[2]);
                if (double.IsNaN(total)) total = Fmt.Num(p[3]);
                if (p[0] == "downloading")
                {
                    j.Phase = j.Mp3 ? "Audio" : (p[6] == "none" ? "Audio" : "Video");
                    j.Progress = total > 0 && !double.IsNaN(done) ? Math.Min(1, done / total) : -1;
                    j.Speed = Fmt.Num(p[4]); j.Eta = Fmt.Num(p[5]); j.Total = total;
                }
                else if (p[0] == "finished")
                {
                    j.Progress = 1; j.Phase = "Finishing"; j.Speed = j.Eta = double.NaN;
                }
                Raise();
            }
            else if (line.StartsWith("[title]")) { j.Title = line.Substring(7).Trim(); Raise(); }
            else if (line.StartsWith("[item]"))
            {
                var p = line.Substring(6).Split('|');
                int.TryParse(p[0], out j.Index);
                if (p.Length > 1) int.TryParse(p[1], out j.Count);
                j.Progress = -1; j.Phase = "Starting";
                Raise();
            }
            else if (line.StartsWith("[file]")) { j.FilePath = line.Substring(6).Trim(); j.FilesDone++; Raise(); }
        }

        static readonly Regex ErrRx = new Regex(@"^ERROR:\s*(\[[^\]]+\]\s*[^:\s]+:\s*)?");
        void OnErr(Job j, string line)
        {
            if (line.StartsWith("ERROR:"))
            {
                string m = ErrRx.Replace(line, "").Trim();
                int c = m.IndexOf(" (caused by", StringComparison.Ordinal);
                if (c > 0) m = m.Substring(0, c);
                if (m.Contains("Unsupported URL")) m = "This site isn't supported";
                j.Error = m; Raise();
            }
        }

        void OnExit(Job j, int code)
        {
            j.Proc = null;
            if (j.CancelRequested) j.State = JobState.Canceled;
            else if (code == 0 || (j.Playlist && j.FilesDone > 0)) { j.State = JobState.Done; j.Progress = 1; }
            else
            {
                j.State = JobState.Failed;
                if (string.IsNullOrEmpty(j.Error)) j.Error = "yt-dlp stopped (code " + code + ")";
            }
            j.Speed = j.Eta = double.NaN;
            if (j.RemoveAfter) Jobs.Remove(j);
            else if (!j.CancelRequested) Finished?.Invoke(j);
            Pump();
        }
    }

    // ---------------------------------------------------------------- tray icon renderer
    static class TrayArt
    {
        // A solid disc so it's easy to spot and click in the tray: terracotta when idle, a dark disc with a thick
        // progress ring while downloading, green when done, red on an error; always a white dripper. Drawn 4x and scaled down.
        public static Icon Render(double progress, bool done, bool error)
        {
            int sz = SystemInformation.SmallIconSize.Width;
            if (sz < 16) sz = 16;
            int big = sz * 4;
            using (var hi = new Bitmap(big, big))
            using (var bmp = new Bitmap(sz, sz))
            {
                using (var g = Graphics.FromImage(hi))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    var disc = new RectangleF(big * 0.02f, big * 0.02f, big * 0.96f, big * 0.96f);
                    bool active = progress >= 0 && !done && !error;
                    Color fill = error ? Theme.Red : done ? Theme.Green : active ? Color.FromArgb(255, 64, 38, 32) : Theme.Accent;
                    using (var b = new SolidBrush(fill)) g.FillEllipse(b, disc);
                    if (active)
                    {
                        float pw = big * 0.16f;
                        var ring = new RectangleF(pw / 2 + big * 0.02f, pw / 2 + big * 0.02f, big * 0.96f - pw, big * 0.96f - pw);
                        using (var tp = new Pen(Color.FromArgb(90, 255, 255, 255), pw)) g.DrawEllipse(tp, ring);
                        float sweep = (float)Math.Max(0.04, Math.Min(1, progress)) * 360f;
                        using (var ap = new Pen(Theme.Accent, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(ap, ring, -90, sweep);
                    }
                    float ins = big * (active ? 0.27f : 0.21f);
                    Theme.DrawDripper(g, new RectangleF(ins, ins * 1.04f, big - 2 * ins, big - 2 * ins), Color.White);
                }
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(hi, new Rectangle(0, 0, sz, sz));
                }
                IntPtr h = bmp.GetHicon();
                using (var tmp = Icon.FromHandle(h))
                {
                    var ico = (Icon)tmp.Clone();
                    try { Native.DestroyIcon(h); } catch { }
                    return ico;
                }
            }
        }
    }

    // ---------------------------------------------------------------- dark menu
    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Back = Color.FromArgb(40, 40, 40), Hi = Color.FromArgb(60, 60, 60);
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Hi;
        public override Color MenuItemSelected => Hi;
        public override Color MenuItemSelectedGradientBegin => Hi;
        public override Color MenuItemSelectedGradientEnd => Hi;
        public override Color MenuItemPressedGradientBegin => Hi;
        public override Color MenuItemPressedGradientEnd => Hi;
        public override Color SeparatorDark => Color.FromArgb(64, 64, 64);
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Back;
        public override Color CheckSelectedBackground => Hi;
        public override Color CheckPressedBackground => Hi;
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Sub;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (var f = new Font(Theme.IconFont, 9f))
                TextRenderer.DrawText(e.Graphics, "", f, e.ImageRectangle, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
