using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Pour
{
    // Run: PourTests.exe <output folder>. Writes results.txt and panel pictures. Never reads or saves the real settings.
    static class Tests
    {
        static int fails; static StringWriter o = new StringWriter();
        static void Check(string name, bool ok) { o.WriteLine((ok ? "ok    " : "FAIL  ") + name); if (!ok) fails++; }

        [STAThread]
        static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            try { Logic(); Pictures(dir); }
            catch (Exception ex) { o.WriteLine("CRASH " + ex); fails++; }
            o.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            File.WriteAllText(Path.Combine(dir, "results.txt"), o.ToString());
            return fails;
        }

        static void Logic()
        {
            Check("Lexend Medium loads", AppFonts.Medium != null);
            Check("Lexend Bold loads", AppFonts.Bold != null);
            Check("link keeps no trailing comma", Fmt.AnyUrls("see https://a.com/x, ok")[0] == "https://a.com/x");
            Check("link keeps no trailing dot", Fmt.OnlyUrls("https://a.com/x.")[0] == "https://a.com/x");
            Check("query link untouched", Fmt.OnlyUrls("https://a.com/w?v=1_-")[0] == "https://a.com/w?v=1_-");

            Check("single video opens file", !new Job { FilesDone = 1 }.OpensFolder);
            Check("playlist toggle with one video opens file", !new Job { Playlist = true, Count = 1, FilesDone = 1 }.OpensFolder);
            Check("playlist opens folder", new Job { Playlist = true, Count = 8, FilesDone = 8 }.OpensFolder);
            Check("partial playlist opens folder", new Job { Playlist = true, Count = 8, FilesDone = 1 }.OpensFolder);
            Check("result folder", new Job { FilePath = @"C:\a\List\01.mp4" }.ResultFolder == @"C:\a\List");

            var d = new Downloader(new Settings());
            var j = d.Add("https://a.com/1");
            Check("plain pour doesn't open", !j.OpenWhenDone);
            Check("duplicate isn't added", d.Add("https://a.com/1", true) == null && d.Jobs.Count == 1);
            Check("Pour & open applies to a running link", j.OpenWhenDone);

            d = new Downloader(new Settings());
            d.AddAndOpen(new[] { "https://a.com/1", "https://a.com/2" });
            Check("several links don't each open", d.Jobs.All(x => !x.OpenWhenDone && x.OpenGroup > 0));
            d.Jobs[0].State = JobState.Done;
            Check("batch waits for the last one", !d.SettleOpenGroups());
            d.Jobs[1].State = JobState.Failed;
            Check("batch opens once when finished", d.SettleOpenGroups());
            Check("batch never opens twice", !d.SettleOpenGroups());

            d = new Downloader(new Settings());
            d.AddAndOpen(new[] { "https://a.com/1", "https://a.com/2" });
            foreach (var x in d.Jobs) x.State = JobState.Failed;
            Check("all failed opens nothing", !d.SettleOpenGroups());
        }

        static void Save(PourPanel p, string file, bool settings)
        {
            p.Relayout();
            if (settings) p.ShowSettings();
            using (var b = new Bitmap(p.Width, p.Height)) { p.RenderTo(b, settings); b.Save(file, ImageFormat.Png); }
        }

        static void Pictures(string dir)
        {
            var st = new Settings { Playlist = true };
            var app = new App(st, null);
            var p = app.Panel;
            var h = p.Handle;
            Save(p, Path.Combine(dir, "main-empty.png"), false);
            Save(p, Path.Combine(dir, "settings.png"), true);
            var js = app.Dl.Jobs;
            js.Add(new Job { Url = "https://a.com/1", Title = "Building a tiny espresso machine from scratch", State = JobState.Running, Progress = 0.62, Phase = "Video", Speed = 8200000, Eta = 74, Total = 412000000, OpenWhenDone = true, Quality = "1080" });
            js.Add(new Job { Url = "https://a.com/2", Title = "أغنية طويلة جدا بعنوان عربي لاختبار عرض النص في القائمة", State = JobState.Done, Mp3 = true, FilesDone = 1, Quality = "best" });
            js.Add(new Job { Url = "https://a.com/3", Title = "Late night lo-fi mix", State = JobState.Done, Playlist = true, Count = 24, FilesDone = 24, Quality = "720" });
            js.Add(new Job { Url = "https://a.com/4", Title = "Private video", State = JobState.Failed, Error = "This video is private", Quality = "1080" });
            Save(p, Path.Combine(dir, "main-list.png"), false);
        }
    }
}
