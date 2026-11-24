using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Pour
{
    class PourPanel : Form
    {
        class Hit { public Rectangle R; public string Key; public Action Click; public Action<Point> Right; }

        // Segoe Fluent Icons
        const string GSettings = "", GFolder = "", GPaste = "", GCheck = "", GCancel = "",
            GPlay = "", GRetry = "", GBack = "", GDownload = "";

        readonly App app;
        readonly float s;
        readonly TextBox url, clipFrom, clipTo;
        bool clipError;
        readonly Font fTitle, fBody, fBold, fSmall, fLabel, fIcon, fIconSmall;
        readonly List<Hit> hits = new List<Hit>();
        readonly Timer anim;
        string hover, pressed;
        // smooth scrolling in pixels: the wheel moves scrollGoal, scrollAnim eases scrollPx toward it (~60 fps)
        float scrollPx, scrollGoal;
        readonly Timer scrollAnim = new Timer { Interval = 15 };
        bool urlError;
        bool settingsOpen, startWithWindows;
        public bool SuppressHide;
        public DateTime LastHidden = DateTime.MinValue;

        const int MaxRows = 5;
        int S(float v) => (int)Math.Round(v * s);
        int W => S(412);
        int Pad => S(16);
        int ListTop => S(242);
        int RowH => S(62);
        int FooterH => S(36);
        int SettingsH => S(530);
        int VisibleRows => Math.Max(1, Math.Min(MaxRows, app.Dl.Jobs.Count));
        int ListH => app.Dl.Jobs.Count == 0 ? S(92) : VisibleRows * RowH;

        Rectangle UrlField => new Rectangle(Pad, S(58), W - Pad * 2, S(42));
        Rectangle PourButton => new Rectangle(Pad, S(154), (W - Pad * 2 - S(8)) / 2, S(40));
        Rectangle OpenButton { get { var p = PourButton; return new Rectangle(p.Right + S(8), p.Y, W - Pad - p.Right - S(8), p.Height); } }

        public PourPanel(App a)
        {
            app = a;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            Text = "Pour";
            Icon = app.AppIcon;
            KeyPreview = true;
            AllowDrop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            using (var g = CreateGraphics()) s = g.DpiX / 96f;

            fTitle = Theme.UiBold(13f);
            fBody = Theme.Ui(10f);
            fBold = Theme.UiBold(10f);
            fSmall = Theme.Ui(8.75f);
            fLabel = Theme.UiBold(8.25f);
            fIcon = new Font(Theme.IconFont, 11f);
            fIconSmall = new Font(Theme.IconFont, 9.5f);

            url = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = fBody };
            url.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(e.Control); }
                else if (e.KeyCode == Keys.A && e.Control) { e.SuppressKeyPress = true; url.SelectAll(); }
            };
            url.TextChanged += (o, e) => { if (urlError) { urlError = false; Invalidate(); } };
            url.GotFocus += (o, e) => Invalidate();
            url.LostFocus += (o, e) => Invalidate();
            url.HandleCreated += (o, e) => Native.SendMessage(url.Handle, 0x1501, (IntPtr)1, "Paste a link, or a few");
            Controls.Add(url);

            clipFrom = ClipBox(app.S.GifFrom, t => app.S.GifFrom = t);
            clipTo = ClipBox(app.S.GifTo, t => app.S.GifTo = t);

            DragEnter += (o, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += (o, e) =>
            {
                string t = (e.Data.GetData(DataFormats.UnicodeText) ?? e.Data.GetData(DataFormats.Text)) as string;
                foreach (var u in Fmt.AnyUrls(t)) app.Dl.Add(u);
            };

            scrollAnim.Tick += (o, e) => ScrollStep();

            anim = new Timer { Interval = 50 };
            anim.Tick += (o, e) => { if (Visible && !settingsOpen && app.Dl.Jobs.Any(j => j.State == JobState.Running && (j.Progress < 0 || j.Phase == "Finishing"))) Invalidate(); };
            anim.Start();

            Relayout();
        }

        TextBox ClipBox(string value, Action<string> store)
        {
            var b = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fBody, Text = value, MaxLength = 8, Visible = false, TextAlign = HorizontalAlignment.Center };
            b.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(e.Control); } };
            b.TextChanged += (o, e) =>
            {
                string t = b.Text.Trim();
                if (Fmt.Seconds(t) >= 0) store(t);
                if (clipError) { clipError = false; Invalidate(); }
            };
            b.GotFocus += (o, e) => b.SelectAll();
            b.LostFocus += (o, e) => app.S.Save();
            Controls.Add(b);
            return b;
        }

        Rectangle ClipField(int i)
        {
            int x = Pad + S(158), w = (W - Pad - x - S(6)) / 2;
            return new Rectangle(x + i * (w + S(6)), S(108), w, S(38));
        }

        void SyncClip()
        {
            bool show = !settingsOpen && app.S.Gif;
            clipFrom.Visible = clipTo.Visible = show;
            if (!show) return;
            for (int i = 0; i < 2; i++)
            {
                var f = ClipField(i);
                var box = i == 0 ? clipFrom : clipTo;
                // Lexend sits low in its line, so the box moves up a little to match the label beside it
                box.SetBounds(f.X + S(42), f.Y + (f.Height - box.PreferredHeight) / 2 - S(2), f.Width - S(42) - S(8), box.PreferredHeight);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);         // rounded corners (Win11)
                int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);           // dark mode
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);                       // border color
            }
            catch { }
        }

        public void Relayout()
        {
            int h = settingsOpen ? SettingsH : ListTop + ListH + FooterH;
            if (Width != W || Height != h)
            {
                if (Visible) SetBounds(Left, Bottom - h, W, h);     // grows upward, stays above the tray
                else Size = new Size(W, h);
            }
            var uf = UrlField;
            url.SetBounds(uf.X + S(14), uf.Y + (uf.Height - url.PreferredHeight) / 2, uf.Width - S(14) - S(40), url.PreferredHeight);
            SyncClip();
        }

        public void ShowNearTray()
        {
            Relayout();
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
            hover = pressed = null;
            if (string.IsNullOrWhiteSpace(url.Text))
            {
                var clip = Fmt.OnlyUrls(App.ReadClipboard());
                clip = clip.Where(u => !app.Dl.HasActive(u) && !app.Dl.Jobs.Any(j => j.Url == u)).ToList();
                if (clip.Count > 0) url.Text = string.Join(" ", clip);
            }
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            url.Focus();
            url.SelectAll();
            Invalidate();
        }

        public void SetUrl(string text)
        {
            if (settingsOpen) ShowMain();
            url.Text = text;
            url.Focus();
            url.SelectAll();
        }

        public void ShowSettings()
        {
            settingsOpen = true;
            startWithWindows = Settings.StartWithWindows;
            app.UpdateNote = "";
            url.Visible = false;
            hover = pressed = null;
            Relayout();
            Invalidate();
        }

        void ShowMain()
        {
            settingsOpen = false;
            url.Visible = true;
            hover = pressed = null;
            Relayout();
            if (Visible) url.Focus();
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && settingsOpen) { settingsOpen = false; url.Visible = true; SyncClip(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!SuppressHide) { Hide(); LastHidden = DateTime.Now; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (settingsOpen) ShowMain(); else Hide();
            }
            base.OnKeyDown(e);
        }

        void Submit(bool openWhenDone)
        {
            if (app.S.Gif && !Fmt.GifRange(clipFrom.Text, clipTo.Text, out _, out _)) { clipError = true; Invalidate(); return; }
            var urls = Fmt.AnyUrls(url.Text);
            if (urls.Count == 0) urls = Fmt.OnlyUrls(App.ReadClipboard());
            if (urls.Count == 0) { urlError = true; Invalidate(); url.Focus(); return; }
            if (openWhenDone) app.Dl.AddAndOpen(urls);
            else foreach (var u in urls) app.Dl.Add(u);
            url.Clear();
            ScrollTo(0);
        }

        void Paste()
        {
            var urls = Fmt.AnyUrls(App.ReadClipboard());
            if (urls.Count > 0) SetUrl(string.Join(" ", urls));
        }

        // ------------------------------------------------------------ painting helpers
        void AddHit(Rectangle r, string key, Action click, Action<Point> right = null) => hits.Add(new Hit { R = r, Key = key, Click = click, Right = right });

        Hit FindHit(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        const TextFormatFlags LPath = (L & ~TextFormatFlags.EndEllipsis) | TextFormatFlags.PathEllipsis;
        const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags Measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        // Arabic or Hebrew titles read right to left, so their ellipsis lands on the correct side
        static TextFormatFlags Direction(string t)
        {
            foreach (char c in t)
            {
                if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF)) return TextFormatFlags.RightToLeft;
                if (char.IsLetter(c)) return 0;
            }
            return 0;
        }

        void IconButton(Graphics g, string glyph, Rectangle r, string key, Action click, Font f = null, Color? color = null)
        {
            if (hover == key) Theme.Fill(g, r, Theme.CardHover, S(7));
            TextRenderer.DrawText(g, glyph, f ?? fIcon, r, color ?? (hover == key ? Theme.Text : Theme.Sub), C);
            AddHit(r, key, click);
        }

        void Button(Graphics g, Rectangle r, string text, string glyph, string key, Action click, bool primary)
        {
            bool hot = hover == key, down = hot && pressed == key;
            Color bg = primary ? (down ? Theme.Accent : hot ? Theme.AccentHover : Theme.Accent) : (down ? Theme.Selected : hot ? Theme.CardHover : Theme.Card);
            Color fg = primary ? Theme.AccentText : Theme.Text;
            Theme.Fill(g, r, bg, S(9));
            int tw = TextRenderer.MeasureText(g, text, fBold, Size.Empty, Measure).Width;
            int iw = glyph == null ? 0 : S(16) + S(8);
            int x = r.X + (r.Width - tw - iw) / 2;
            if (glyph != null) TextRenderer.DrawText(g, glyph, fIconSmall, new Rectangle(x, r.Y, S(16), r.Height), fg, C);
            TextRenderer.DrawText(g, text, fBold, new Rectangle(x + iw, r.Y, tw + S(2), r.Height), fg, L);
            AddHit(r, key, click);
        }

        void Segmented(Graphics g, Rectangle r, string[] labels, int selected, string key, Action<int> pick)
        {
            Theme.Fill(g, r, Theme.Card, S(9));
            int n = labels.Length;
            for (int i = 0; i < n; i++)
            {
                int x0 = r.X + r.Width * i / n, x1 = r.X + r.Width * (i + 1) / n;
                var cell = new Rectangle(x0, r.Y, x1 - x0, r.Height);
                var inner = Rectangle.Inflate(cell, -S(3), -S(3));
                string k = key + i;
                if (i == selected) Theme.Fill(g, inner, Theme.Selected, S(6));
                else if (hover == k) Theme.Fill(g, inner, Theme.CardHover, S(6));
                TextRenderer.DrawText(g, labels[i], i == selected ? fBold : fBody, cell, i == selected ? Theme.Text : Theme.Sub, C);
                int idx = i;
                AddHit(cell, k, () => pick(idx));
            }
        }

        void Switch(Graphics g, Rectangle pill, bool on, bool hot)
        {
            if (on) Theme.Fill(g, pill, hot ? Theme.AccentHover : Theme.Accent, pill.Height / 2f);
            else Theme.Stroke(g, pill, hot ? Theme.Text : Theme.Sub, pill.Height / 2f, Math.Max(1, s));
            float d = on ? S(14) : S(11);
            float kx = on ? pill.Right - S(3) - d : pill.X + S(4);
            using (var b = new SolidBrush(on ? Theme.AccentText : hot ? Theme.Text : Theme.Sub))
                g.FillEllipse(b, kx, pill.Y + (pill.Height - d) / 2f, d, d);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            hits.Clear();
            if (settingsOpen) PaintSettings(g); else PaintMain(g);
        }

        // ------------------------------------------------------------ main page
        void PaintMain(Graphics g)
        {
            // header
            Theme.DrawLogo(g, new RectangleF(Pad, S(14), S(28), S(28)));
            TextRenderer.DrawText(g, "Pour", fTitle, new Rectangle(Pad + S(38), S(12), S(120), S(32)), Theme.Text, L);
            IconButton(g, GSettings, new Rectangle(W - Pad - S(32), S(12), S(32), S(32)), "settings", ShowSettings);
            IconButton(g, GFolder, new Rectangle(W - Pad - S(68), S(12), S(32), S(32)), "folder", app.OpenFolder);

            // link field
            var uf = UrlField;
            Theme.Fill(g, uf, Theme.Field, S(9));
            if (urlError) Theme.Stroke(g, uf, Theme.Red, S(9), Math.Max(1, s));
            else if (url.Focused) Theme.Stroke(g, uf, Theme.Accent, S(9), Math.Max(1, s));
            IconButton(g, GPaste, new Rectangle(uf.Right - S(36), uf.Y + S(5), S(32), S(32)), "paste", Paste, fIconSmall);

            // format + quality
            var st = app.S;
            Segmented(g, new Rectangle(Pad, S(108), S(150), S(38)), new[] { "MP4", "MP3", "GIF" }, st.Gif ? 2 : st.Mp3 ? 1 : 0, "fmt", i =>
            {
                st.Mp3 = i == 1; st.Gif = i == 2; clipError = false;
                st.Save(); SyncClip(); Invalidate();
            });
            var qr = new Rectangle(Pad + S(158), S(108), W - Pad * 2 - S(158), S(38));
            if (st.Gif)
            {
                for (int i = 0; i < 2; i++)
                {
                    var f = ClipField(i);
                    Theme.Fill(g, f, Theme.Card, S(9));
                    if (clipError) Theme.Stroke(g, f, Theme.Red, S(9), Math.Max(1, s));
                    else if ((i == 0 ? clipFrom : clipTo).Focused) Theme.Stroke(g, f, Theme.Accent, S(9), Math.Max(1, s));
                    TextRenderer.DrawText(g, i == 0 ? "From" : "To", fSmall, new Rectangle(f.X + S(10), f.Y, S(34), f.Height), Theme.Sub, L);
                }
            }
            else if (st.Mp3)
            {
                Theme.Fill(g, qr, Theme.Card, S(9));
                TextRenderer.DrawText(g, st.EmbedExtras ? "Best audio, with cover" : "Best audio quality", fBody, qr, Theme.Sub, C);
            }
            else
            {
                var qv = new[] { "best", "1440", "1080", "720" };
                Segmented(g, qr, new[] { "Best", "2K", "1080p", "720p" }, Array.IndexOf(qv, st.Quality), "q", i => { st.Quality = qv[i]; st.Save(); Invalidate(); });
            }

            // the two pour buttons
            Button(g, PourButton, "Pour", GDownload, "pour", () => Submit(false), true);
            Button(g, OpenButton, "Pour & open", GPlay, "pouropen", () => Submit(true), false);

            // playlist switch + clear
            var tr = new Rectangle(Pad, S(202), S(170), S(28));
            bool plHot = hover == "pl" && !st.Gif, plOn = st.Playlist && !st.Gif;
            Switch(g, new Rectangle(Pad, tr.Y + (tr.Height - S(20)) / 2, S(36), S(20)), plOn, plHot);
            TextRenderer.DrawText(g, "Whole playlist", fBody, new Rectangle(Pad + S(46), tr.Y, S(124), tr.Height), st.Gif ? Theme.Dim : plHot || plOn ? Theme.Text : Theme.Sub, L);
            if (!st.Gif) AddHit(tr, "pl", () => { st.Playlist = !st.Playlist; st.Save(); Invalidate(); });

            if (app.Dl.Jobs.Any(j => !j.Active))
            {
                var cr = new Rectangle(W - Pad - S(110), tr.Y, S(110), tr.Height);
                TextRenderer.DrawText(g, "Clear finished", fSmall, cr, hover == "clear" ? Theme.Text : Theme.Sub, R);
                AddHit(cr, "clear", () => { app.Dl.ClearFinished(); ScrollTo(0); });
            }

            using (var p = new Pen(Theme.Border, Math.Max(1, s))) g.DrawLine(p, Pad, S(237), W - Pad, S(237));

            // list
            var jobs = app.Dl.Jobs;
            if (jobs.Count == 0)
            {
                var er = new Rectangle(Pad, ListTop, W - Pad * 2, ListH);
                TextRenderer.DrawText(g, "Nothing pouring yet", fBold, new Rectangle(er.X, er.Y + S(20), er.Width, S(24)), Theme.Sub, C);
                TextRenderer.DrawText(g, "Copy a link, drop one here, or press Ctrl+Alt+D", fSmall, new Rectangle(er.X, er.Y + S(46), er.Width, S(20)), Theme.Dim, C);
            }
            else
            {
                int maxPx = MaxScrollPx;
                scrollPx = Math.Max(0, Math.Min(scrollPx, maxPx));
                scrollGoal = Math.Max(0, Math.Min(scrollGoal, maxPx));
                int off = (int)Math.Round(scrollPx);
                var listRect = new Rectangle(0, ListTop, W, ListH);
                var state = g.Save();
                g.SetClip(listRect);
                for (int i = Math.Max(0, off / RowH); i < jobs.Count; i++)
                {
                    int y = ListTop + i * RowH - off;
                    if (y > listRect.Bottom) break;
                    var r = new Rectangle(Pad - S(6), y + S(2), W - Pad * 2 + S(12), RowH - S(4));
                    int before = hits.Count;
                    DrawRow(g, jobs[i], r);
                    // a row half out of view only answers clicks on its visible part
                    for (int k = before; k < hits.Count; k++) hits[k].R = Rectangle.Intersect(hits[k].R, listRect);
                }
                g.Restore(state);
                if (jobs.Count > MaxRows)
                {
                    int trackH = VisibleRows * RowH - S(8);
                    int thumbH = Math.Max(S(20), trackH * MaxRows / jobs.Count);
                    int thumbY = ListTop + S(4) + (int)((long)(trackH - thumbH) * off / Math.Max(1, maxPx));
                    Theme.Fill(g, new Rectangle(W - S(5), thumbY, S(3), thumbH), Theme.Track, S(2));
                }
            }

            // footer
            var fr = new Rectangle(Pad, Height - FooterH, W - Pad * 2, FooterH - S(6));
            string status = app.Status;
            Color sc = app.StatusIsError ? Theme.Red : Theme.Dim;
            if (clipError) { status = "Use times like 1:20. A GIF can be up to " + Fmt.MaxGifSeconds + " seconds."; sc = Theme.Red; }
            else if (string.IsNullOrEmpty(status))
            {
                int running = jobs.Count(j => j.State == JobState.Running), queued = jobs.Count(j => j.State == JobState.Queued);
                status = running + queued > 0 ? running + " pouring" + (queued > 0 ? " · " + queued + " waiting" : "") : "Saving to " + ShortFolder(st.Folder);
            }
            TextRenderer.DrawText(g, status, fSmall, fr, hover == "status" && app.StatusIsError ? Theme.Text : sc, LPath);
            if (app.StatusIsError) AddHit(fr, "status", app.RetrySetup);
        }

        static string ShortFolder(string f)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (f.StartsWith(home, StringComparison.OrdinalIgnoreCase)) f = "~" + f.Substring(home.Length);
            return f;
        }

        void DrawRow(Graphics g, Job j, Rectangle r)
        {
            string rowKey = "row:" + j.Id, actPrefix = "act:" + j.Id + ":";
            Action<Point> rowMenu = pt => app.ShowRowMenu(this, j, PointToScreen(pt));
            bool hov = hover == rowKey || (hover != null && hover.StartsWith(actPrefix));
            if (hov) Theme.Fill(g, r, Theme.Card, S(9));
            AddHit(r, rowKey, () => { if (j.State == JobState.Done) app.OpenResult(j); }, rowMenu);

            var inner = Rectangle.Inflate(r, -S(8), 0);
            string fmt = j.Gif ? "GIF · " + Fmt.Clock(j.ClipFrom) + " to " + Fmt.Clock(j.ClipTo) : j.Mp3 ? "MP3" : "MP4 · " + Settings.QualityLabel(j.Quality);

            string right; Color rc; Color bar;
            switch (j.State)
            {
                case JobState.Running:
                    right = j.Phase == "Finishing" ? "Finishing" : j.Progress >= 0 ? (int)(j.Progress * 100) + "%" : "…";
                    rc = Theme.Accent; bar = Theme.Accent; break;
                case JobState.Done: right = "Done"; rc = Theme.Green; bar = Theme.Green; break;
                case JobState.Failed: right = "Failed"; rc = Theme.Red; bar = Theme.Red; break;
                case JobState.Canceled: right = "Canceled"; rc = Theme.Dim; bar = Theme.Dim; break;
                default: right = "Queued"; rc = Theme.Sub; bar = Theme.Track; break;
            }

            // right side: status, or action buttons on hover
            int rightW;
            var top = new Rectangle(inner.X, r.Y + S(6), inner.Width, S(22));
            if (hov)
            {
                var acts = new List<Tuple<string, string, Action>>();
                if (j.Active) acts.Add(Tuple.Create(GCancel, "cancel", (Action)(() => app.Dl.Cancel(j))));
                if (j.State == JobState.Done)
                {
                    acts.Add(Tuple.Create(j.OpensFolder ? GFolder : GPlay, "open", (Action)(() => app.OpenResult(j))));
                    if (!j.OpensFolder) acts.Add(Tuple.Create(GFolder, "show", (Action)(() => app.ShowInFolder(j))));
                }
                if (j.State == JobState.Failed || j.State == JobState.Canceled) acts.Add(Tuple.Create(GRetry, "retry", (Action)(() => app.Dl.Retry(j))));
                if (!j.Active) acts.Add(Tuple.Create(GCancel, "remove", (Action)(() => app.Dl.Remove(j))));
                int bw = S(26), x = inner.Right - bw * acts.Count;
                foreach (var a in acts)
                {
                    IconButton(g, a.Item1, new Rectangle(x, top.Y - S(2), bw, bw), actPrefix + a.Item2, a.Item3, fIconSmall);
                    hits[hits.Count - 1].Right = rowMenu;
                    x += bw;
                }
                rightW = bw * acts.Count + S(6);
            }
            else
            {
                rightW = TextRenderer.MeasureText(g, right, fBold, Size.Empty, Measure).Width + S(10);
                TextRenderer.DrawText(g, right, fBold, top, rc, R);
            }

            string title = j.DisplayTitle;
            TextRenderer.DrawText(g, title, fBody, new Rectangle(top.X, top.Y, top.Width - rightW, top.Height), Theme.Text, L | Direction(title));

            // bar
            var br = new Rectangle(inner.X, r.Y + S(32), inner.Width, Math.Max(3, S(4)));
            Theme.Fill(g, br, Theme.Track, br.Height / 2f);
            if (j.State == JobState.Running && (j.Progress < 0 || j.Phase == "Finishing"))
            {
                // indeterminate shimmer (masked: TickCount goes negative after 24 days of uptime)
                int seg = br.Width / 4;
                int pos = (int)(((Environment.TickCount & int.MaxValue) / 4) % (br.Width + seg)) - seg;
                var sr = Rectangle.Intersect(br, new Rectangle(br.X + pos, br.Y, seg, br.Height));
                Theme.Fill(g, sr, bar, br.Height / 2f);
            }
            else
            {
                double p = j.State == JobState.Running ? Math.Max(0, j.Progress) : j.State == JobState.Queued ? 0 : 1;
                Theme.Fill(g, new RectangleF(br.X, br.Y, (float)(br.Width * p), br.Height), bar, br.Height / 2f);
            }

            // sub line
            var parts = new List<string>();
            Color subc = Theme.Sub;
            switch (j.State)
            {
                case JobState.Queued:
                    parts.Add(fmt); parts.Add(app.Dl.ToolsReady ? "waiting" : "waiting for setup");
                    if (j.OpenWhenDone || j.OpenGroup > 0) parts.Add("opens when done");
                    break;
                case JobState.Running:
                    if (j.Count > 1 && j.Index > 0) parts.Add(j.Index + " of " + j.Count);
                    if (j.Phase.Length > 0 && j.Phase != "Finishing") parts.Add(j.Phase);
                    else if (j.Phase == "Finishing") parts.Add(j.Gif ? "making GIF" : j.Mp3 ? "converting to MP3" : "merging");
                    if (!double.IsNaN(j.Total) && j.Total > 0) parts.Add(Fmt.Size(j.Total));
                    var sp = Fmt.Speed(j.Speed); if (sp.Length > 0) parts.Add(sp);
                    var et = Fmt.Eta(j.Eta); if (et.Length > 0) parts.Add(et);
                    if (j.OpenWhenDone || j.OpenGroup > 0) parts.Add("opens when done");
                    break;
                case JobState.Done:
                    parts.Add(fmt);
                    if (j.FilesDone > 1) parts.Add(j.FilesDone + " files");
                    else try { if (j.FilePath != null && System.IO.File.Exists(j.FilePath)) parts.Add(Fmt.Size(new System.IO.FileInfo(j.FilePath).Length)); } catch { }
                    if (j.Playlist && !string.IsNullOrEmpty(j.Error)) parts.Add("some skipped");
                    parts.Add(j.OpensFolder ? "click for folder" : "click to open");
                    break;
                case JobState.Failed: parts.Add(j.Error ?? "Something went wrong"); subc = Color.FromArgb(220, 140, 140); break;
                case JobState.Canceled: parts.Add(fmt); parts.Add("canceled"); break;
            }
            TextRenderer.DrawText(g, string.Join("  ·  ", parts), fSmall, new Rectangle(inner.X, r.Y + S(39), inner.Width, S(17)), subc, L);
        }

        // ------------------------------------------------------------ settings page
        void Section(Graphics g, string text, int y) =>
            TextRenderer.DrawText(g, text, fLabel, new Rectangle(Pad + S(2), y, W - Pad * 2, S(18)), Theme.Dim, L);

        Rectangle Card(Graphics g, int y, int rows, int rowH)
        {
            var c = new Rectangle(Pad, y, W - Pad * 2, rows * rowH);
            Theme.Fill(g, c, Theme.Card, S(10));
            using (var p = new Pen(Theme.Border, Math.Max(1, s)))
                for (int i = 1; i < rows; i++) g.DrawLine(p, c.X + S(14), y + i * rowH, c.Right - S(14), y + i * rowH);
            return c;
        }

        void RowText(Graphics g, Rectangle r, string title, string sub, int rightSpace)
        {
            TextRenderer.DrawText(g, title, fBody, new Rectangle(r.X + S(14), r.Y + S(5), r.Width - S(14) - rightSpace, S(20)), Theme.Text, L);
            TextRenderer.DrawText(g, sub, fSmall, new Rectangle(r.X + S(14), r.Y + S(25), r.Width - S(14) - rightSpace, S(17)), Theme.Sub, LPath);
        }

        void ToggleRow(Graphics g, Rectangle r, string title, string sub, string key, bool on, Action flip)
        {
            bool hot = hover == key;
            if (hot) Theme.Fill(g, Rectangle.Inflate(r, -S(3), -S(3)), Theme.CardHover, S(7));
            RowText(g, r, title, sub, S(64));
            Switch(g, new Rectangle(r.Right - S(14) - S(36), r.Y + (r.Height - S(20)) / 2, S(36), S(20)), on, hot);
            AddHit(r, key, () => { flip(); Invalidate(); });
        }

        void SmallButton(Graphics g, Rectangle r, string text, string key, Action click)
        {
            bool hot = hover == key;
            Theme.Fill(g, r, hot ? Theme.Selected : Theme.Field, S(7));
            TextRenderer.DrawText(g, text, fSmall, r, Theme.Text, C);
            AddHit(r, key, click);
        }

        void PaintSettings(Graphics g)
        {
            var st = app.S;
            const int RH = 48;
            int rh = S(RH);

            IconButton(g, GBack, new Rectangle(Pad - S(4), S(12), S(32), S(32)), "back", ShowMain, fIconSmall);
            TextRenderer.DrawText(g, "Settings", fTitle, new Rectangle(Pad + S(36), S(12), S(200), S(32)), Theme.Text, L);

            // saving
            int y = S(52);
            Section(g, "SAVE TO", y);
            var c = Card(g, y + S(20), 1, rh);
            RowText(g, c, "Download folder", ShortFolder(st.Folder), S(92));
            SmallButton(g, new Rectangle(c.Right - S(84), c.Y + S(10), S(72), S(28)), "Change", "changefolder", app.ChangeFolder);

            // downloads
            y = c.Bottom + S(10);
            Section(g, "DOWNLOADS", y);
            c = Card(g, y + S(20), 3, rh);
            ToggleRow(g, new Rectangle(c.X, c.Y, c.Width, rh), "Watch clipboard", "Offer to pour links you copy", "clip", st.WatchClipboard,
                () => { st.WatchClipboard = !st.WatchClipboard; st.Save(); });
            ToggleRow(g, new Rectangle(c.X, c.Y + rh, c.Width, rh), "Cover art and info", "Saved inside each file", "extras", st.EmbedExtras,
                () => { st.EmbedExtras = !st.EmbedExtras; st.Save(); });
            var par = new Rectangle(c.X, c.Y + rh * 2, c.Width, rh);
            RowText(g, par, "Downloads at once", "How many run together", S(150));
            Segmented(g, new Rectangle(par.Right - S(142), par.Y + S(8), S(130), S(32)), new[] { "1", "2", "3", "4" }, st.Parallel - 1, "par",
                i => { st.Parallel = i + 1; st.Save(); app.Dl.Pump(); Invalidate(); });

            // app
            y = c.Bottom + S(10);
            Section(g, "APP", y);
            c = Card(g, y + S(20), 3, rh);
            ToggleRow(g, new Rectangle(c.X, c.Y, c.Width, rh), "Start with Windows", "Waits quietly in the tray", "startup", startWithWindows,
                () => { Settings.StartWithWindows = !startWithWindows; startWithWindows = Settings.StartWithWindows; });
            ToggleRow(g, new Rectangle(c.X, c.Y + rh, c.Width, rh), "Keep yt-dlp updated", "Checks once a day", "autoupdate", st.AutoUpdate,
                () => { st.AutoUpdate = !st.AutoUpdate; st.Save(); });
            var upd = new Rectangle(c.X, c.Y + rh * 2, c.Width, rh);
            RowText(g, upd, "Update yt-dlp", app.UpdateNote.Length > 0 ? app.UpdateNote : "If a site stops working", S(112));
            SmallButton(g, new Rectangle(upd.Right - S(104), upd.Y + S(10), S(92), S(28)), "Update now", "updatenow", app.UpdateNow);

            // footer
            var v = Application.ProductVersion;
            var fr = new Rectangle(Pad + S(2), Height - S(40), S(160), S(30));
            TextRenderer.DrawText(g, "Pour " + v, fSmall, fr, Theme.Dim, L);
            var ex = new Rectangle(W - Pad - S(84), Height - S(40), S(84), S(30));
            TextRenderer.DrawText(g, "Exit Pour", fSmall, ex, hover == "exit" ? Theme.Red : Theme.Sub, R);
            AddHit(ex, "exit", app.ExitSoon);
        }

        // ------------------------------------------------------------ mouse
        void UpdateHover(Point p)
        {
            var h = FindHit(p);
            string k = h?.Key;
            if (k != hover) { hover = k; Invalidate(); }
            Cursor = h != null && !(h.Key.StartsWith("row:") && app.Dl.Jobs.FirstOrDefault(j => "row:" + j.Id == h.Key)?.State != JobState.Done) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = FindHit(e.Location)?.Key;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = FindHit(e.Location);
            string was = pressed;
            pressed = null;
            Invalidate();
            // only a press and release on the same thing counts (not a release left over from a menu or a drag)
            if (h == null || h.Key != was) return;
            if (e.Button == MouseButtons.Left) h.Click?.Invoke();
            else if (e.Button == MouseButtons.Right) h.Right?.Invoke(e.Location);
        }

        int MaxScrollPx => Math.Max(0, (app.Dl.Jobs.Count - MaxRows) * RowH);

        void ScrollTo(int px) { scrollAnim.Stop(); scrollGoal = scrollPx = px; Invalidate(); }

        void ScrollStep()
        {
            float d = scrollGoal - scrollPx;
            bool done = Math.Abs(d) < 0.6f;
            if (done) { scrollPx = scrollGoal; scrollAnim.Stop(); }
            else scrollPx += d * 0.3f;
            Invalidate();
            Update();
            if (done) UpdateHover(PointToClient(Cursor.Position));   // rows moved under a still mouse
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (settingsOpen) return;
            // the real delta: a touchpad scrolls by the pixel, a wheel notch by one row
            scrollGoal = Math.Max(0, Math.Min(MaxScrollPx, scrollGoal - e.Delta * RowH / 120f));
            if (!scrollAnim.Enabled) scrollAnim.Start();
        }

        // test harness: draw the current state to a picture
        internal void RenderTo(Bitmap b, bool settings)
        {
            if (settings) ShowSettings(); else if (settingsOpen) ShowMain();
            DrawToBitmap(b, new Rectangle(0, 0, Width, Height));
            // a hidden form skips its child boxes
            if (!settings && app.S.Gif) foreach (var box in new[] { clipFrom, clipTo }) box.DrawToBitmap(b, box.Bounds);
        }
    }
}
