using System.Diagnostics;
using System.Reflection;
using RmdblobUnpacker;
using RmdblobUnpacker.Core;

static class BulkSelectionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name);
    }
    public static void Run(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        using var form = new MainForm(false) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-30000, -30000) };
        object Field(string name) => typeof(MainForm).GetField(name, Private)!.GetValue(form)!;
        object Call(string name, params object[] values) => typeof(MainForm).GetMethod(name, Private)!.Invoke(form, values);
        var tocs = (List<TocFile>)Field("_tocs"); var packs = (HashSet<int>)Field("_checkedPacks");
        var search = (TextBox)Field("_searchBox"); var list = (ScrollableDarkList)Field("_list");
        var selected = (Dictionary<string, bool>)Field("_selected");
        var selectAll = (TextButton)Field("_selectAllBtn"); var clear = (TextButton)Field("_clearSelectionBtn");
        var exportAll = (TextButton)Field("_exportAllBtn");
        form.Show(); Application.DoEvents();
        Assert(!selectAll.Enabled && !clear.Enabled, "empty-list selection controls disabled");
        var stream = new TocFile { Pack = "stream0-generic", Entries = Enumerable.Range(0, 145212).Select(i => new ResourceEntry
        { Pack = "stream0-generic", Path = i < 29607 ? $"data_pc/audio/media/{i}.wem" : $"data_pc/objects/{i}.binfbx", Size = i < 518 ? 0 : 1024 }).ToList() };
        var hidden = new ResourceEntry { Pack = "base-generic", Path = "data_pc/audio/media/0.wem", Size = 1024 };
        tocs.Add(stream); tocs.Add(new TocFile { Pack = "base-generic", Entries = new() { hidden } }); packs.Add(0);
        search.Text = "data_pc/audio/media/"; Call("Refilter");
        Assert(list.VirtualListSize == 29607, "145212-entry archive filters to 29607 audio rows");
        var watch = Stopwatch.StartNew(); selectAll.PerformClick(); watch.Stop();
        Assert(selected.Count == 29607 && stream.Entries.Take(29607).All(e => selected.ContainsKey(e.Key)), "one click checks all 29607 results including offscreen rows");
        Console.WriteLine("Select all elapsed: " + watch.ElapsedMilliseconds + " ms");
        selectAll.PerformClick(); Assert(selected.Count == 29607, "Select all is idempotent");
        var checkedExport = (List<ResourceEntry>)Call("ExportItems", true);
        Assert(checkedExport.Count == 29607 && checkedExport.All(e => e.Path.Contains("/audio/media/")), "Export selected includes every checked audio row");
        var allResults = (List<ResourceEntry>)Call("ExportItems", false);
        selected[hidden.Key] = true; selectAll.PerformClick();
        Assert(selected.Count == 29608 && selected.ContainsKey(hidden.Key), "selecting results preserves hidden checks in another pack");
        clear.PerformClick(); Assert(selected.Count == 0 && list.VirtualListSize == 29607, "Clear selection clears all checks without removing filter results");
        Assert(((List<ResourceEntry>)Call("ExportItems", false)).Count == 29607, "Export all results needs no checked rows");
        search.Text = ".binfbx"; Call("Refilter");
        Assert(allResults.Count == 29607 && allResults.All(e => e.Path.EndsWith(".wem")), "queued export snapshot is independent of subsequent filter changes");
        search.Text = "data_pc/audio/media/"; Call("Refilter");
        Assert((bool)typeof(ScrollableDarkList).GetMethod("IsInputKey", Private)!.Invoke(list, new object[] { Keys.Control | Keys.A }), "list accepts Ctrl+A as an input shortcut");
        typeof(ScrollableDarkList).GetMethod("OnKeyDown", Private)!.Invoke(list, new object[] { new KeyEventArgs(Keys.Control | Keys.A) });
        Assert(selected.Count == 29607, "Ctrl+A in list checks all filtered results");
        typeof(ScrollableDarkList).GetMethod("OnKeyDown", Private)!.Invoke(list, new object[] { new KeyEventArgs(Keys.Control | Keys.Shift | Keys.A) });
        Assert(selected.Count == 0, "Ctrl+Shift+A clears all checks");
        Call("SetBusy", true, "Busy"); Call("SelectAllResults");
        Assert(!selectAll.Enabled && !clear.Enabled && selected.Count == 0, "bulk selection cannot change during export/scan");
        Call("SetBusy", false, L.Ready); selectAll.PerformClick(); Call("SetBusy", true, "Busy"); Call("ClearSelection");
        Assert(selected.Count == 29607, "busy guard also prevents clearing checks"); Call("SetBusy", false, L.Ready);
        ((TextButton)Field("_showSelBtn")).PerformClick(); clear.PerformClick();
        Assert(selected.Count == 0 && list.VirtualListSize == 0, "clearing while Selected only is active refreshes results");
        ((TextButton)Field("_showSelBtn")).PerformClick();
        int game = Array.IndexOf(args, "--game-dir");
        if (game >= 0 && game + 1 < args.Length)
        {
            tocs.Clear(); tocs.Add(TocFile.Load(Path.Combine(args[game + 1], "data_pack2", "pc", "stream0-generic.rmdtoc")));
            packs.Clear(); packs.Add(0); Call("Refilter"); selectAll.PerformClick();
            int expected = tocs[0].Entries.Count(e => e.Path.Contains("data_pc/audio/media/"));
            Assert(expected > 0 && selected.Count == expected && ((List<ResourceEntry>)Call("ExportItems", true)).Count == expected,
                "actual stream0-generic audio results all selected: " + expected);
            clear.PerformClick();
        }
        string screenshots = Environment.GetEnvironmentVariable("RMD_SELECTION_SCREENSHOTS");
        foreach (bool english in new[] { true, false }) foreach (var size in new[] { new Size(2150,1398), new Size(1080,700) })
        {
            L.En = english; Call("ApplyTexts"); form.Size = size; form.PerformLayout(); Application.DoEvents();
            Assert(selectAll.Text == (english ? "Select all" : "全选结果") && clear.Text == (english ? "Clear selection" : "清空选择") && exportAll.Text == (english ? "Export all results" : "导出全部结果"), "selection/export actions use chosen language");
            foreach (var button in new[] { selectAll, clear })
                Assert(button.Width >= button.GetPreferredSize(Size.Empty).Width && button.Height >= button.GetPreferredSize(Size.Empty).Height && button.Bottom <= button.Parent!.Height && button.Right <= button.Parent.Width,
                    "selection button fits at " + form.Width + "px: " + button.Text);
            if (!string.IsNullOrWhiteSpace(screenshots))
            {
                Directory.CreateDirectory(screenshots); using var image = new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(screenshots,$"{(english ? "en" : "zh")}-{form.Width}.png"));
            }
        }
        form.Close(); Application.DoEvents(); Console.WriteLine("SUCCESS: bulk selection checks passed");
    }
}
