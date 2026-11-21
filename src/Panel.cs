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

        readonly App app;
        readonly float s;
        readonly TextBox url;
        readonly Font fTitle, fBody, fBold, fSmall, fIcon, fIconSmall;
        readonly List<Hit> hits = new List<Hit>();
        readonly Timer anim;
        string hover;
        // smooth scrolling in pixels: the wheel moves scrollGoal, scrollAnim eases scrollPx toward it (~60 fps)
        float scrollPx, scrollGoal;
        readonly Timer scrollAnim = new Timer { Interval = 15 };
        bool urlError;
        public bool SuppressHide;
        public DateTime LastHidden = DateTime.MinValue;

        const int MaxRows = 5;
        int S(float v) => (int)Math.Round(v * s);
        int W => S(388);
        int Pad => S(14);
        int ListTop => S(180);
        int RowH => S(60);
        int FooterH => S(34);
        int VisibleRows => Math.Max(1, Math.Min(MaxRows, app.Dl.Jobs.Count));
        int ListH => app.Dl.Jobs.Count == 0 ? S(86) : VisibleRows * RowH;

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

            fTitle = new Font("Segoe UI Semibold", 12f);
            fBody = new Font("Segoe UI", 9.75f);
            fBold = new Font("Segoe UI Semibold", 9.75f);
            fSmall = new Font("Segoe UI", 8.25f);
            fIcon = new Font(Theme.IconFont, 11f);
            fIconSmall = new Font(Theme.IconFont, 9.5f);

            url = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fBody };
            url.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Hide(); }
            };
            url.TextChanged += (o, e) => { if (urlError) { urlError = false; Invalidate(); } };
            url.GotFocus += (o, e) => Invalidate();
            url.LostFocus += (o, e) => Invalidate();
            url.HandleCreated += (o, e) => Native.SendMessage(url.Handle, 0x1501, (IntPtr)1, "Paste a link, or a few");
            Controls.Add(url);

            DragEnter += (o, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += (o, e) =>
            {
                string t = (e.Data.GetData(DataFormats.UnicodeText) ?? e.Data.GetData(DataFormats.Text)) as string;
                var urls = Fmt.AnyUrls(t);
                foreach (var u in urls) app.Dl.Add(u);
            };

            scrollAnim.Tick += (o, e) => ScrollStep();

            anim = new Timer { Interval = 50 };
            anim.Tick += (o, e) => { if (Visible && app.Dl.Jobs.Any(j => j.State == JobState.Running && (j.Progress < 0 || j.Phase == "Finishing"))) Invalidate(); };
            anim.Start();

            Relayout();
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
            int h = ListTop + ListH + FooterH;
            if (Width != W || Height != h)
            {
                int bottom = Bottom;
                Size = new Size(W, h);
                if (Visible) Top = bottom - h;
            }
            var uf = UrlField;
            url.SetBounds(uf.X + S(12), uf.Y + (uf.Height - url.PreferredHeight) / 2, uf.Width - S(12) - S(36), url.PreferredHeight);
        }

        Rectangle UrlField => new Rectangle(Pad, S(56), W - Pad * 2 - S(88), S(38));
        Rectangle PourButton => new Rectangle(W - Pad - S(80), S(56), S(80), S(38));

        public void ShowNearTray()
        {
            Relayout();
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
            hover = null;
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
            url.Text = text;
            url.Focus();
            url.SelectAll();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!SuppressHide) { Hide(); LastHidden = DateTime.Now; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Hide();
            base.OnKeyDown(e);
        }

        void Submit()
        {
            var urls = Fmt.AnyUrls(url.Text);
            if (urls.Count == 0) urls = Fmt.OnlyUrls(App.ReadClipboard());
            if (urls.Count == 0) { urlError = true; Invalidate(); url.Focus(); return; }
            foreach (var u in urls) app.Dl.Add(u);
            url.Clear();
            ScrollTo(0);
        }

        void Paste()
        {
            var urls = Fmt.AnyUrls(App.ReadClipboard());
            if (urls.Count > 0) SetUrl(string.Join(" ", urls));
        }

        // ------------------------------------------------------------ painting
        void AddHit(Rectangle r, string key, Action click, Action<Point> right = null) => hits.Add(new Hit { R = r, Key = key, Click = click, Right = right });

        Hit FindHit(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }

        const TextFormatFlags L = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        const TextFormatFlags R = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags C = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        void IconButton(Graphics g, string glyph, Rectangle r, string key, Action click, Font f = null, Color? color = null)
        {
            if (hover == key) Theme.Fill(g, r, Theme.CardHover, S(6));
            TextRenderer.DrawText(g, glyph, f ?? fIcon, r, color ?? (hover == key ? Theme.Text : Theme.Sub), C);
            AddHit(r, key, click);
        }

        void Segmented(Graphics g, Rectangle r, string[] labels, int selected, string key, Action<int> pick)
        {
            Theme.Fill(g, r, Theme.Card, S(7));
            int n = labels.Length;
            for (int i = 0; i < n; i++)
            {
                int x0 = r.X + r.Width * i / n, x1 = r.X + r.Width * (i + 1) / n;
                var cell = new Rectangle(x0, r.Y, x1 - x0, r.Height);
                var inner = Rectangle.Inflate(cell, -S(3), -S(3));
                string k = key + i;
                if (i == selected) Theme.Fill(g, inner, Theme.Selected, S(5));
                else if (hover == k) Theme.Fill(g, inner, Theme.CardHover, S(5));
                TextRenderer.DrawText(g, labels[i], i == selected ? fBold : fBody, cell, i == selected ? Theme.Text : Theme.Sub, C);
                int idx = i;
                AddHit(cell, k, () => pick(idx));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            hits.Clear();

            // header
            Theme.DrawLogo(g, new RectangleF(Pad, S(14), S(26), S(26)));
            TextRenderer.DrawText(g, "Pour", fTitle, new Rectangle(Pad + S(32), S(12), S(120), S(30)), Theme.Text, L);
            IconButton(g, "", new Rectangle(W - Pad - S(32), S(12), S(32), S(32)), "menu", () => app.ShowMenu(this, PointToScreen(new Point(W - Pad - S(32), S(44)))));
            IconButton(g, "", new Rectangle(W - Pad - S(68), S(12), S(32), S(32)), "folder", app.OpenFolder);

            // link field + pour button
            var uf = UrlField;
            Theme.Fill(g, uf, Theme.Card, S(8));
            if (urlError) Theme.Stroke(g, uf, Theme.Red, S(8), Math.Max(1, s));
            else if (url.Focused) Theme.Stroke(g, uf, Theme.Border, S(8), Math.Max(1, s));
            IconButton(g, "", new Rectangle(uf.Right - S(34), uf.Y + S(4), S(30), S(30)), "paste", Paste, fIconSmall);

            var pb = PourButton;
            Theme.Fill(g, pb, hover == "pour" ? Theme.AccentHover : Theme.Accent, S(8));
            TextRenderer.DrawText(g, "Pour", fBold, pb, Theme.AccentText, C);
            AddHit(pb, "pour", Submit);

            // format + quality
            var st = app.S;
            Segmented(g, new Rectangle(Pad, S(104), S(116), S(32)), new[] { "MP4", "MP3" }, st.Mp3 ? 1 : 0, "fmt", i => { st.Mp3 = i == 1; st.Save(); Invalidate(); });
            var qr = new Rectangle(Pad + S(124), S(104), W - Pad * 2 - S(124), S(32));
            if (st.Mp3)
            {
                Theme.Fill(g, qr, Theme.Card, S(7));
                TextRenderer.DrawText(g, st.EmbedExtras ? "Best audio · with cover art" : "Best audio quality", fBody, qr, Theme.Sub, C);
            }
            else
            {
                var qv = new[] { "best", "1440", "1080", "720" };
                Segmented(g, qr, new[] { "Best", "2K", "1080p", "720p" }, Array.IndexOf(qv, st.Quality), "q", i => { st.Quality = qv[i]; st.Save(); Invalidate(); });
            }

            // playlist toggle + clear
            var tr = new Rectangle(Pad, S(144), S(150), S(26));
            var box = new Rectangle(Pad + S(2), tr.Y + (tr.Height - S(16)) / 2, S(16), S(16));
            if (st.Playlist)
            {
                Theme.Fill(g, box, Theme.Accent, S(4));
                TextRenderer.DrawText(g, "", fIconSmall, box, Theme.AccentText, C);
            }
            else Theme.Stroke(g, box, hover == "pl" ? Theme.Sub : Theme.Dim, S(4), Math.Max(1, s));
            TextRenderer.DrawText(g, "Whole playlist", fBody, new Rectangle(box.Right + S(8), tr.Y, S(120), tr.Height), hover == "pl" ? Theme.Text : Theme.Sub, L);
            AddHit(tr, "pl", () => { st.Playlist = !st.Playlist; st.Save(); Invalidate(); });

            if (app.Dl.Jobs.Any(j => !j.Active))
            {
                var cr = new Rectangle(W - Pad - S(100), tr.Y, S(100), tr.Height);
                TextRenderer.DrawText(g, "Clear finished", fSmall, cr, hover == "clear" ? Theme.Text : Theme.Sub, R);
                AddHit(cr, "clear", () => { app.Dl.ClearFinished(); ScrollTo(0); });
            }

            using (var p = new Pen(Theme.Border, Math.Max(1, s))) g.DrawLine(p, Pad, S(176), W - Pad, S(176));

            // list
            var jobs = app.Dl.Jobs;
            if (jobs.Count == 0)
            {
                var er = new Rectangle(Pad, ListTop, W - Pad * 2, ListH);
                TextRenderer.DrawText(g, "Nothing pouring yet", fBold, new Rectangle(er.X, er.Y + S(18), er.Width, S(22)), Theme.Sub, C);
                TextRenderer.DrawText(g, "Copy a link, drop one here, or press Ctrl+Alt+D", fSmall, new Rectangle(er.X, er.Y + S(42), er.Width, S(20)), Theme.Dim, C);
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
                    Theme.Fill(g, new Rectangle(W - S(5), thumbY, S(3), thumbH), Theme.Border, S(2));
                }
            }

            // footer
            var fr = new Rectangle(Pad, Height - FooterH, W - Pad * 2, FooterH - S(6));
            string status = app.Status;
            Color sc = app.StatusIsError ? Theme.Red : Theme.Dim;
            if (string.IsNullOrEmpty(status))
            {
                int running = jobs.Count(j => j.State == JobState.Running), queued = jobs.Count(j => j.State == JobState.Queued);
                status = running + queued > 0 ? running + " pouring" + (queued > 0 ? " · " + queued + " waiting" : "") : "Saving to " + ShortFolder(st.Folder);
            }
            TextRenderer.DrawText(g, status, fSmall, fr, hover == "status" && app.StatusIsError ? Theme.Text : sc, L);
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
            bool hov = hover == rowKey || (hover != null && hover.StartsWith(actPrefix));
            if (hov) Theme.Fill(g, r, Theme.Card, S(8));
            AddHit(r, rowKey, () => { if (j.State == JobState.Done) app.OpenResult(j); }, pt => app.ShowRowMenu(this, j, PointToScreen(pt)));

            var inner = Rectangle.Inflate(r, -S(8), 0);
            string fmt = j.Mp3 ? "MP3" : "MP4 · " + Settings.QualityLabel(j.Quality);

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
                if (j.Active) acts.Add(Tuple.Create("", "cancel", (Action)(() => app.Dl.Cancel(j))));
                if (j.State == JobState.Done)
                {
                    acts.Add(Tuple.Create("", "open", (Action)(() => app.OpenResult(j))));
                    acts.Add(Tuple.Create("", "show", (Action)(() => app.ShowInFolder(j))));
                }
                if (j.State == JobState.Failed || j.State == JobState.Canceled) acts.Add(Tuple.Create("", "retry", (Action)(() => app.Dl.Retry(j))));
                if (!j.Active) acts.Add(Tuple.Create("", "remove", (Action)(() => app.Dl.Remove(j))));
                int bw = S(26), x = inner.Right - bw * acts.Count;
                foreach (var a in acts)
                {
                    IconButton(g, a.Item1, new Rectangle(x, top.Y - S(2), bw, bw), actPrefix + a.Item2, a.Item3, fIconSmall);
                    x += bw;
                }
                rightW = bw * acts.Count + S(6);
            }
            else
            {
                rightW = TextRenderer.MeasureText(g, right, fBold, Size.Empty, R).Width + S(10);
                TextRenderer.DrawText(g, right, fBold, top, rc, R);
            }

            TextRenderer.DrawText(g, j.DisplayTitle, fBody, new Rectangle(top.X, top.Y, top.Width - rightW, top.Height), Theme.Text, L);

            // bar
            var br = new Rectangle(inner.X, r.Y + S(31), inner.Width, Math.Max(3, S(4)));
            Theme.Fill(g, br, Theme.Track, br.Height / 2f);
            if (j.State == JobState.Running && (j.Progress < 0 || j.Phase == "Finishing"))
            {
                // indeterminate shimmer
                int seg = br.Width / 4;
                int pos = (int)((Environment.TickCount / 4) % (br.Width + seg)) - seg;
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
                case JobState.Queued: parts.Add(fmt); parts.Add(app.Dl.ToolsReady ? "waiting" : "waiting for setup"); break;
                case JobState.Running:
                    if (j.Count > 1 && j.Index > 0) parts.Add(j.Index + " of " + j.Count);
                    if (j.Phase.Length > 0 && j.Phase != "Finishing") parts.Add(j.Phase);
                    else if (j.Phase == "Finishing") parts.Add(j.Mp3 ? "converting to MP3" : "merging");
                    if (!double.IsNaN(j.Total) && j.Total > 0) parts.Add(Fmt.Size(j.Total));
                    var sp = Fmt.Speed(j.Speed); if (sp.Length > 0) parts.Add(sp);
                    var et = Fmt.Eta(j.Eta); if (et.Length > 0) parts.Add(et);
                    break;
                case JobState.Done:
                    parts.Add(fmt);
                    if (j.FilesDone > 1) parts.Add(j.FilesDone + " files");
                    else try { if (j.FilePath != null && System.IO.File.Exists(j.FilePath)) parts.Add(Fmt.Size(new System.IO.FileInfo(j.FilePath).Length)); } catch { }
                    parts.Add("click to open");
                    break;
                case JobState.Failed: parts.Add(j.Error ?? "Something went wrong"); subc = Color.FromArgb(220, 140, 140); break;
                case JobState.Canceled: parts.Add(fmt); parts.Add("canceled"); break;
            }
            TextRenderer.DrawText(g, string.Join("  ·  ", parts), fSmall, new Rectangle(inner.X, r.Y + S(37), inner.Width, S(18)), subc, L);
        }

        // ------------------------------------------------------------ mouse
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = FindHit(e.Location);
            string k = h?.Key;
            if (k != hover) { hover = k; Invalidate(); }
            Cursor = h != null && !(h.Key.StartsWith("row:") && app.Dl.Jobs.FirstOrDefault(j => "row:" + j.Id == h.Key)?.State != JobState.Done) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = FindHit(e.Location);
            if (h == null) return;
            if (e.Button == MouseButtons.Left) h.Click?.Invoke();
            else if (e.Button == MouseButtons.Right) h.Right?.Invoke(e.Location);
            Invalidate();
        }

        int MaxScrollPx => Math.Max(0, (app.Dl.Jobs.Count - MaxRows) * RowH);

        void ScrollTo(int px) { scrollAnim.Stop(); scrollGoal = scrollPx = px; Invalidate(); }

        void ScrollStep()
        {
            float d = scrollGoal - scrollPx;
            if (Math.Abs(d) < 0.6f) { scrollPx = scrollGoal; scrollAnim.Stop(); }
            else scrollPx += d * 0.3f;
            Invalidate();
            Update();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // the real delta: a touchpad scrolls by the pixel, a wheel notch by one row
            scrollGoal = Math.Max(0, Math.Min(MaxScrollPx, scrollGoal - e.Delta * RowH / 120f));
            if (!scrollAnim.Enabled) scrollAnim.Start();
        }
    }
}
