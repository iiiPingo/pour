using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pour
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, "Pour.SingleInstance.v1", out bool created))
            {
                bool exit = args.Contains("--exit");
                if (!created)
                {
                    // --tray (Start with Windows) must never pop the panel open on a Pour that's already running
                    if (exit || !args.Contains("--tray"))
                        Native.PostMessage((IntPtr)0xffff, exit ? Native.WM_EXITPOUR : Native.WM_SHOWPOUR, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (exit) return;   // asked to close, but Pour wasn't running
                try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
                catch { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (o, e) => Log.Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log.Crash(e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (o, e) => { Log.Crash(e.Exception); e.SetObserved(); };
                // the downloader marshals yt-dlp output back to the UI thread through this context,
                // so it must exist before anything is created
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Application.Run(new App(args.Contains("--tray")));
                GC.KeepAlive(mutex);
            }
        }
    }

    static class Log
    {
        public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pour", "crash.log");
        public static void Crash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.AppendAllText(Path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + "\r\n\r\n");
            }
            catch { }
        }
    }

    class MsgWindow : NativeWindow, IDisposable
    {
        readonly App app;
        public bool HotkeyOk;
        public MsgWindow(App a)
        {
            app = a;
            CreateHandle(new CreateParams { Caption = "PourMsgWindow" });
            Native.AddClipboardFormatListener(Handle);
            HotkeyOk = Native.RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x4000, (int)Keys.D); // Ctrl+Alt+D, no repeat
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x031D) app.OnClipboard();
            else if (m.Msg == 0x0312) app.OnHotkey();
            else if (m.Msg == Native.WM_SHOWPOUR) app.ShowPanel();
            else if (m.Msg == Native.WM_EXITPOUR) app.ExitSoon();
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(Handle);
            Native.UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }

    class App : ApplicationContext
    {
        public readonly Settings S;
        public readonly Downloader Dl;
        public readonly Icon AppIcon;
        public readonly PourPanel Panel;
        public string Status = "";
        public bool StatusIsError;
        public string UpdateNote = "";

        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly MsgWindow msg;
        readonly System.Windows.Forms.Timer tick;
        Action balloonAction;
        DateTime balloonShown = DateTime.MinValue;
        string lastClip;
        DateTime ignoreClipUntil = DateTime.MinValue;
        DateTime flashUntil = DateTime.MinValue;
        bool flashError;
        string lastIconKey;

        // test harness only: the panel with no tray, hotkey, clipboard or setup
        internal App(Settings settings, Downloader unused)
        {
            S = settings;
            AppIcon = SystemIcons.Application;
            Dl = new Downloader(S);
            Panel = new PourPanel(this);
        }

        public App(bool startHidden)
        {
            S = Settings.Load();
            if (S.FirstRun) { Settings.StartWithWindows = true; S.Save(); }
            else Settings.RepairRunPath();

            try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { AppIcon = SystemIcons.Application; }

            Dl = new Downloader(S);
            Panel = new PourPanel(this);
            menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Theme.Ui(9.5f), ShowImageMargin = false };
            menu.Opening += (o, e) => { BuildMenu(); e.Cancel = false; };
            menu.Closed += (o, e) => ReleasePanel();

            tray = new NotifyIcon { Text = "Pour", Visible = true, ContextMenuStrip = menu };
            tray.Icon = TrayArt.Render(-1, false, false);
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
            tray.BalloonTipClicked += (o, e) => { var a = balloonAction; balloonAction = null; a?.Invoke(); };
            // replacing a notification closes the old one: that must not wipe the new one's click
            tray.BalloonTipClosed += (o, e) => { if ((DateTime.Now - balloonShown).TotalMilliseconds > 1000) balloonAction = null; };

            msg = new MsgWindow(this);

            Dl.Changed += () =>
            {
                if (Dl.SettleOpenGroups()) OpenFolder();
                if (Panel.Visible) { Panel.Relayout(); Panel.Invalidate(); }
                UpdateTray();
            };
            Dl.Finished += OnFinished;

            tick = new System.Windows.Forms.Timer { Interval = 400 };
            tick.Tick += (o, e) => UpdateTray();
            tick.Start();

            lastClip = Fmt.OnlyUrls(ReadClipboard()).FirstOrDefault();

            _ = SetupAsync();
            if (!startHidden) Panel.ShowNearTray();
        }

        // ------------------------------------------------------------ setup
        async Task SetupAsync()
        {
            StatusIsError = false;
            try
            {
                if (!Tools.Ready || !File.Exists(Tools.YtDlp)) SetStatus("Setting up…");
                await Tools.EnsureAsync(SetStatus);
                if (S.AutoUpdate && (DateTime.Now - S.LastUpdate).TotalHours > 20)
                {
                    SetStatus("Checking for yt-dlp updates…");
                    await Tools.UpdateYtDlpAsync();
                    S.LastUpdate = DateTime.Now; S.Save();
                }
                SetStatus("");
                Dl.ToolsReady = true;
                Dl.Pump();
            }
            catch (Exception ex)
            {
                StatusIsError = true;
                SetStatus("Setup failed (" + Short(ex.Message) + "). Click to retry.");
            }
        }

        public void RetrySetup() { if (StatusIsError) _ = SetupAsync(); }

        static string Short(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;

        void SetStatus(string s)
        {
            Status = s;
            if (Panel.Visible) Panel.Invalidate();
        }

        // ------------------------------------------------------------ tray
        void UpdateTray()
        {
            var running = Dl.Jobs.Where(j => j.State == JobState.Running).ToList();
            bool flashing = DateTime.Now < flashUntil;
            double prog = running.Count > 0 ? running.Average(j => j.Phase == "Finishing" ? 1 : Math.Max(0, j.Progress)) : (Dl.Jobs.Any(j => j.State == JobState.Queued) ? 0 : -1);
            string key = flashing ? (flashError ? "err" : "done") : prog < 0 ? "idle" : ((int)(prog * 40)).ToString();
            if (key != lastIconKey)
            {
                lastIconKey = key;
                var old = tray.Icon;
                tray.Icon = TrayArt.Render(flashing ? -1 : prog, flashing && !flashError, flashing && flashError);
                old?.Dispose();
            }
            int queued = Dl.Jobs.Count(j => j.State == JobState.Queued);
            string tip = running.Count + queued == 0 ? "Pour" : "Pour · " + running.Count + " pouring" + (prog >= 0 ? " · " + (int)(prog * 100) + "%" : "") + (queued > 0 ? " · " + queued + " waiting" : "");
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tray.Text != tip) tray.Text = tip;
        }

        void TogglePanel()
        {
            if (Panel.Visible) { Panel.Hide(); return; }
            if ((DateTime.Now - Panel.LastHidden).TotalMilliseconds < 300) return; // the click that just closed it
            Panel.ShowNearTray();
        }

        public void ShowPanel() { if (!Panel.Visible) Panel.ShowNearTray(); else Panel.Activate(); }

        void Balloon(string title, string text, ToolTipIcon icon, Action click)
        {
            balloonAction = click;
            balloonShown = DateTime.Now;
            try { tray.ShowBalloonTip(5000, title, string.IsNullOrEmpty(text) ? "Pour" : text, icon); } catch (Exception ex) { Log.Crash(ex); }
        }

        void OnFinished(Job j)
        {
            if (!Dl.Jobs.Any(x => x.State == JobState.Running)) { flashUntil = DateTime.Now.AddSeconds(4); flashError = j.State == JobState.Failed; }
            UpdateTray();
            if (j.State == JobState.Done && j.OpenWhenDone) { j.OpenWhenDone = false; OpenResult(j); return; }
            if (j.State == JobState.Done && j.OpenGroup > 0) return;    // the folder opens when the whole batch is done
            if (Panel.Visible) return;
            if (j.State == JobState.Done) Balloon("Poured", j.DisplayTitle, ToolTipIcon.None, () => ShowInFolder(j));
            else if (j.State == JobState.Failed) Balloon("Couldn't pour that", j.Error ?? j.DisplayTitle, ToolTipIcon.Warning, ShowPanel);
        }

        // ------------------------------------------------------------ clipboard + hotkey
        public static string ReadClipboard()
        {
            for (int i = 0; i < 4; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
                catch { Thread.Sleep(30); }
            }
            return "";
        }

        public void OnClipboard()
        {
            if (DateTime.Now < ignoreClipUntil) return;
            if (!Panel.Visible && !S.WatchClipboard) return;    // watching is off: don't even read the clipboard
            var urls = Fmt.OnlyUrls(ReadClipboard());
            if (urls.Count == 0) return;
            string first = urls[0];
            if (first == lastClip) return;
            lastClip = first;
            if (Panel.Visible) { Panel.SetUrl(string.Join(" ", urls)); return; }
            if (urls.All(Dl.HasActive)) return;
            string what = urls.Count == 1 ? "Click to pour it" : "Click to pour all " + urls.Count;
            Balloon("Link copied", what + " as " + S.FormatLabel(S.Mp3, S.Quality) + ".", ToolTipIcon.None, () =>
            {
                foreach (var u in urls) Dl.Add(u);
            });
        }

        public void OnHotkey()
        {
            var urls = Fmt.OnlyUrls(ReadClipboard());
            if (urls.Count == 0) { ShowPanel(); return; }
            int added = urls.Count(u => Dl.Add(u) != null);
            if (!Panel.Visible)
                Balloon(added > 0 ? "Pouring" : "Already pouring", (added == 1 ? Fmt.ShortUrl(urls[0]) : added + " links") + " · " + S.FormatLabel(S.Mp3, S.Quality), ToolTipIcon.None, ShowPanel);
        }

        // ------------------------------------------------------------ actions
        public void OpenFolder()
        {
            try { Directory.CreateDirectory(S.Folder); Process.Start("explorer.exe", "\"" + S.Folder + "\""); } catch { }
        }

        public void OpenResult(Job j)
        {
            try
            {
                if (j.OpensFolder)
                {
                    string dir = j.ResultFolder;
                    if (dir != null && Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"")?.Dispose();
                    else OpenFolder();
                    return;
                }
                if (j.FilePath == null || !File.Exists(j.FilePath)) { ShowInFolder(j); return; }
                Process.Start(new ProcessStartInfo(j.FilePath) { UseShellExecute = true });
            }
            catch { ShowInFolder(j); }
        }

        public void ShowInFolder(Job j)
        {
            try
            {
                if (j.FilePath != null && File.Exists(j.FilePath)) Process.Start("explorer.exe", "/select,\"" + j.FilePath + "\"");
                else OpenFolder();
            }
            catch { }
        }

        public void ChangeFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Where should Pour save downloads?", SelectedPath = S.Folder, ShowNewFolderButton = true })
            {
                Panel.SuppressHide = true;
                var result = Panel.Visible ? d.ShowDialog(Panel) : d.ShowDialog();
                if (result == DialogResult.OK) { S.Folder = d.SelectedPath; S.Save(); }
                Panel.SuppressHide = false;
                if (Panel.Visible) { Panel.Activate(); Panel.Invalidate(); }
            }
        }

        void Say(string text)
        {
            UpdateNote = text;
            if (Panel.Visible) Panel.Invalidate();
            else Balloon("Pour", text, ToolTipIcon.None, null);
        }

        public async void UpdateNow()
        {
            // also covers first-time setup and an update that's already running
            if (!Dl.ToolsReady || !File.Exists(Tools.YtDlp)) { Say("Busy, try again in a moment"); return; }
            if (Dl.Jobs.Any(j => j.State == JobState.Running)) { Say("Wait for downloads to finish"); return; }
            bool wasReady = Dl.ToolsReady;
            Dl.ToolsReady = false;
            UpdateNote = "Updating…";
            SetStatus("Updating yt-dlp…");
            string r = await Tools.UpdateYtDlpAsync();
            S.LastUpdate = DateTime.Now; S.Save();
            SetStatus("");
            Dl.ToolsReady = wasReady || Tools.Ready;
            Dl.Pump();
            Say(r);
        }

        // clean exit on request: after this message is handled, not inside it
        public void ExitSoon()
        {
            var t = new System.Windows.Forms.Timer { Interval = 10 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); try { Exit(); } catch (Exception ex) { Log.Crash(ex); ExitThread(); } };
            t.Start();
        }

        void Exit()
        {
            Dl.KillAll();
            tray.Visible = false;
            msg.Dispose();
            ExitThread();
        }

        // ------------------------------------------------------------ menus
        void ReleasePanel()
        {
            if (!Panel.IsHandleCreated) { Panel.SuppressHide = false; return; }
            Panel.BeginInvoke((Action)(() =>
            {
                Panel.SuppressHide = false;
                if (Panel.Visible && Form.ActiveForm != Panel) { Panel.Hide(); Panel.LastHidden = DateTime.Now; }
            }));
        }

        readonly Font menuBold = Theme.UiBold(9.5f);

        ToolStripMenuItem Item(string text, Action click, bool check = false, bool isChecked = false)
        {
            var it = new ToolStripMenuItem(text) { ForeColor = Theme.Text, CheckOnClick = false, Checked = check && isChecked };
            if (click != null) it.Click += (o, e) => click();
            return it;
        }

        void BuildMenu()
        {
            foreach (ToolStripItem old in menu.Items.Cast<ToolStripItem>().ToList()) old.Dispose();
            menu.Items.Clear();
            var open = Item("Open Pour", ShowPanel);
            open.Font = menuBold;
            menu.Items.Add(open);
            menu.Items.Add(Item("Open download folder", OpenFolder));
            menu.Items.Add(Item("Settings", () => { ShowPanel(); Panel.ShowSettings(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Exit", Exit));
        }

        public void ShowRowMenu(Form owner, Job j, Point screen)
        {
            var m = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = menu.Font, ShowImageMargin = false };
            if (j.State == JobState.Done)
            {
                m.Items.Add(Item("Open", () => OpenResult(j)));
                m.Items.Add(Item("Show in folder", () => ShowInFolder(j)));
            }
            m.Items.Add(Item("Copy link", () => { ignoreClipUntil = DateTime.Now.AddSeconds(1); try { Clipboard.SetText(j.Url); } catch { } }));
            if (j.Active) m.Items.Add(Item("Cancel", () => Dl.Cancel(j)));
            else
            {
                m.Items.Add(Item(j.State == JobState.Done ? "Download again" : "Retry", () => Dl.Retry(j)));
                m.Items.Add(Item("Remove from list", () => Dl.Remove(j)));
            }
            Panel.SuppressHide = true;
            m.Closed += (o, e) => { ReleasePanel(); Panel.BeginInvoke((Action)m.Dispose); };
            m.Show(screen);
        }
    }
}
