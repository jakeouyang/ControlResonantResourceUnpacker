using System.Text;
using System.Windows.Forms;
using RmdblobUnpacker.Core;

namespace RmdblobUnpacker;

public sealed partial class MainForm : Form
{
    // ---- theme ----
    internal static readonly Color CfgBg      = Color.FromArgb(0, 0, 0);
    internal static readonly Color CfgPanel   = Color.FromArgb(21, 21, 21);
    internal static readonly Color CfgPanel2  = Color.FromArgb(17, 17, 17);
    internal static readonly Color CfgAccent  = Color.FromArgb(226, 34, 34);
    internal static readonly Color CfgAccentHi= Color.FromArgb(255, 92, 92);
    internal static readonly Color CfgDim     = Color.FromArgb(190, 70, 70);
    internal static readonly Color CfgGreen   = Color.FromArgb(46, 204, 94);

    TextBox _dirBox;
    TextBox _searchBox;
    DarkPackList _packList;
    ScrollableDarkList _list;
    Label _status, _dirLabel, _packLabel, _searchLabel;
    Panel _header;
    Panel header => _header;
    TextButton _browseBtn, _exportSelBtn, _exportAllBtn, _langBtn, _githubBtn, _closeBtn;
    SimpleScrollBar _vbar, _hbar, _packBar;
    Panel _dirUnderline, _searchUnderline;
    ToolTip _pathTip;
    TextButton _maxBtn, _showSelBtn;
    readonly Dictionary<string, bool> _selected = new();
    bool _showSelectedOnly;

    readonly List<TocFile> _tocs = new();
    readonly HashSet<int> _checkedPacks = new();
    List<ResourceEntry> _filtered = new();
    string _pcDir;
    CancellationTokenSource _cts;

    public MainForm(bool scanDefault = false)
    {
        BuildUi();
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("RmdblobUnpacker.app.ico"))
            if (stream != null) Icon = new Icon(stream);
        ApplyTexts();
        _dirBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !_isBusy) { e.SuppressKeyPress = true; _ = ScanAsync(); } };
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    // ---------------------------------------------------------- ui build

    void BuildUi()
    {
        Text = "Control Resonant Resource Unpacker";
        Width = 2150; Height = 1398;
        MinimumSize = new Size(1080, 700);
        BackColor = CfgBg;
        Font = new Font("Segoe UI", 11f);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(192, 192);
        AutoScaleMode = AutoScaleMode.Dpi;

        // header ---------------------------------------------------------
        _header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = CfgBg };
        var icon = new Label
        {
            Text = "::", AutoSize = true, ForeColor = CfgAccent,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Location = new Point(26, 22),
        };
        var title = new Label
        {
            Text = "Control Resonant Resource Unpacker", AutoSize = true,
            ForeColor = CfgAccent, Font = new Font("Segoe UI", 17f),
            Location = new Point(60, 18), BackColor = CfgBg,
        };
        _githubBtn = new TextButton { Text = "GitHub", Font = new Font("Segoe UI", 12f) };
        _githubBtn.Click += (s, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = "https://github.com/jakeouyang/ControlResonantResourceUnpacker", UseShellExecute = true }); } catch { }
        };
        _langBtn = new TextButton { Text = "中文", Font = new Font("Segoe UI", 12f) };
        _langBtn.Click += (s, e) => { L.En = !L.En; ApplyTexts(); };
        _maxBtn = new TextButton { Text = "▢", Font = new Font("Segoe UI", 12f, FontStyle.Bold), Width = 40 };
        _maxBtn.Click += (s, e) =>
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        };
        _closeBtn = new TextButton { Text = "✕", Font = new Font("Segoe UI", 12f, FontStyle.Bold), Width = 40 };
        _closeBtn.Click += (s, e) => Close();
        header.Resize += (s, e) => LayoutHeaderButtons();
        header.Controls.Add(_langBtn);
        header.Controls.Add(_githubBtn);
        header.Controls.Add(_closeBtn);
        header.Controls.Add(_maxBtn);
        // drag window by header
        Action<MouseEventArgs> drag = e =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                _ = SendMessage(Handle, 0xA1, (IntPtr)0x02, IntPtr.Zero);
            }
        };
        header.MouseDown += (s, e) => drag(e);
        title.MouseDown += (s, e) => drag(e);
        icon.MouseDown += (s, e) => drag(e);
        header.Controls.Add(icon);
        header.Controls.Add(title);
        LayoutHeaderButtons();

        // content --------------------------------------------------------
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = CfgBg,
            Padding = new Padding(56, 8, 56, 0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // dir row
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // packs label
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // panels
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));   // buttons

        // dir row
        var dirRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0),
            BackColor = CfgBg, MaximumSize = new Size(1500, 0),
        };
        dirRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _dirLabel = new Label
        {
            Text = "Game Directory:", AutoSize = true, ForeColor = CfgAccent,
            Font = new Font("Segoe UI", 12.5f), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 16, 0),
            BackColor = CfgBg, Cursor = Cursors.Hand,
        };
        _dirLabel.Click += (s, e) => Browse();
        _dirBox = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = CfgBg, ForeColor = CfgAccentHi,
            Font = new Font("Segoe UI", 12f), Margin = new Padding(0, 12, 0, 0),
            Dock = DockStyle.Fill, PlaceholderText = "D:\\Games\\CONTROL Resonant",
        };
        _dirUnderline = new Panel { Height = 2, BackColor = CfgAccent, Dock = DockStyle.Bottom, Margin = new Padding(0) };
        var dirBoxHost = new Panel { Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = CfgBg, Margin = new Padding(0), Height = 48 };
        _dirBox.Dock = DockStyle.None;
        _dirBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        dirBoxHost.Resize += (s, e) => _dirBox.SetBounds(0, Math.Max(0, (dirBoxHost.Height - _dirBox.PreferredHeight) / 2), dirBoxHost.Width, _dirBox.PreferredHeight);
        _dirUnderline.Dock = DockStyle.Bottom;
        dirBoxHost.Controls.Add(_dirBox);
        dirBoxHost.Controls.Add(_dirUnderline);
        _browseBtn = new TextButton
        {
            Text = "Browse...", Font = new Font("Segoe UI", 12f),
            DashedBorder = true, Padding2 = new Padding(18, 8, 18, 8),
            Anchor = AnchorStyles.Left, Margin = new Padding(16, 0, 0, 0),
        };
        _browseBtn.Click += (s, e) => Browse();
        dirRow.Controls.Add(_dirLabel, 0, 0);
        dirRow.Controls.Add(dirBoxHost, 1, 0);
        dirRow.Controls.Add(_browseBtn, 2, 0);

        // packs label
        _packLabel = new Label
        {
            Text = "Packs", AutoSize = true, ForeColor = CfgAccent,
            Font = new Font("Segoe UI", 12.5f), Margin = new Padding(0, 6, 0, 0), BackColor = CfgBg,
        };

        // pack list (left dark panel)
        _packList = new DarkPackList
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = CfgPanel, ForeColor = CfgAccent,
            Font = new Font("Segoe UI", 10.5f), IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34,
        };
        _packList.DrawItem += PackList_DrawItem;
        _packBar = new SimpleScrollBar(true) { Dock = DockStyle.Right, Width = 12, BackColor = CfgBg };
        _packBar.ValueChanged += () => { if (_packList.IsHandleCreated) _packList.TopIndex = _packBar.Value; };
        _packList.Scrolled += () => { if (_packBar != null) UpdatePackBar(); };
        _packList.Resize += (s, e) => UpdatePackBar();
        var packHost = new Panel { Dock = DockStyle.Fill, BackColor = CfgPanel, Padding = new Padding(0) };
        packHost.Controls.Add(_packList);
        packHost.Controls.Add(_packBar);
        _packList.MouseClick += (s, e) =>
        {
            int idx = _packList.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _tocs.Count) return;
            if (e.Button == MouseButtons.Left && e.X <= 28)
            {
                if (!_checkedPacks.Remove(idx)) _checkedPacks.Add(idx);
                _packList.Invalidate();
                Refilter();
            }
        };
        _packList.SelectedIndexChanged += (s, e) => Refilter();

        // right dark panel: search + list
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = CfgPanel2, Padding = new Padding(14, 10, 14, 10) };
        _searchLabel = new Label { Visible = false };
        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = CfgPanel2, ForeColor = CfgAccentHi,
            Font = new Font("Segoe UI", 11.5f), Dock = DockStyle.Top, Margin = new Padding(0),
        };
        _searchUnderline = new Panel { Height = 1, BackColor = Color.FromArgb(120, CfgAccent), Dock = DockStyle.Top, Margin = new Padding(0) };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = CfgPanel2, Padding = new Padding(4, 4, 4, 0) };
        _searchBox.Dock = DockStyle.Fill;
        _searchUnderline.Dock = DockStyle.Bottom;
        searchHost.Controls.Add(_searchBox);
        searchHost.Controls.Add(_searchUnderline);

        _list = new ScrollableDarkList
        {
            Dock = DockStyle.Fill, BackColor = CfgPanel2, ForeColor = CfgAccent,
            Font = new Font("Segoe UI", 10.5f),
        };
        _list.Columns.Add(new ColumnHeader { Text = "Path", Width = 900 });
        _list.Columns.Add(new ColumnHeader { Text = "Size", Width = 120 });
        _list.Columns.Add(new ColumnHeader { Text = "Pack", Width = 170 });
        _list.Columns.Add(new ColumnHeader { Text = "Format", Width = 110 });
        _list.RetrieveRow = index =>
        {
            if (index < 0 || index >= _filtered.Count) return null;
            var entry = _filtered[index];
            return new ScrollableDarkList.Row(new[] { (_replacements.ContainsKey(entry.Key) ? (L.En ? "[Modified] " : "[已修改] ") : "") + entry.Path, FmtSize(entry.Size), entry.Pack,
                Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant() }, _selected.ContainsKey(entry.Key));
        };
        BuildResourceMenu();
        _list.MouseDoubleClick += (s, e) => PreviewSelected();
        _list.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; PreviewSelected(); }
            if (e.KeyCode == Keys.Space && _list.SelectedIndices.Count > 0)
            {
                var entry = _filtered[_list.SelectedIndices[0]];
                if (!_selected.Remove(entry.Key)) _selected[entry.Key] = true;
                _list.Invalidate(); RefreshSelectionStatus(); e.Handled = true;
            }
        };
        _list.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            int hit = _list.HitIndex(e.Location);
            if (hit < 0 || hit >= _filtered.Count) return;
            var en = _filtered[hit];
            if (!_selected.Remove(en.Key)) _selected[en.Key] = true;
            _list.Invalidate();
            RefreshSelectionStatus();
        };
        _pathTip = new ToolTip { InitialDelay = 400, ReshowDelay = 200 };
        _list.MouseMove += (s, e) =>
        {
            int hit = _list.HitIndex(e.Location);
            if (hit >= 0 && hit < _filtered.Count)
                _pathTip.SetToolTip(_list, _filtered[hit].Path);
            else
                _pathTip.SetToolTip(_list, null);
        };
        _list.Resize += (s, e) => LayoutListColumns();
        LayoutListColumns();
        var searchTimer = new System.Windows.Forms.Timer { Interval = 300 };
        searchTimer.Tick += (s, e) => { searchTimer.Stop(); Refilter(); };
        _searchBox.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
        _list.SelectedIndexChanged += (s, e) => RefreshSelectionStatus();
        _vbar = new SimpleScrollBar(true) { Dock = DockStyle.Right, Width = 12, BackColor = CfgBg };
        _hbar = new SimpleScrollBar(false) { Dock = DockStyle.Bottom, Height = 12, BackColor = CfgBg };
        _list.Resize += (s, e) => UpdateScrollBars();
        _vbar.ValueChanged += () =>
        {
            int rows = _vbar.Value - _list.ReadScroll(true).Position;
            if (rows != 0) _list.ScrollBy(0, rows * _list.RowHeight);
            UpdateScrollBars();
        };
        _list.VScrolled += _ => UpdateScrollBars();
        _hbar.ValueChanged += () =>
        {
            int dx = _hbar.Value - _list.ReadScroll(false).Position;
            if (dx != 0) _list.ScrollBy(dx, 0);
            UpdateScrollBars();
        };
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = CfgPanel2 };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_vbar);
        listHost.Controls.Add(_hbar);
        rightPanel.Controls.Add(listHost);
        rightPanel.Controls.Add(searchHost);

        // buttons row (right aligned)
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            BackColor = CfgBg, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0),
        };
        _showSelBtn = new TextButton { Text = L.ShowSel, Font = new Font("Segoe UI", 12f) };
        _exportAllBtn = new TextButton { Text = "Export filtered", Font = new Font("Segoe UI", 12f), Margin = new Padding(0, 0, 24, 0) };
        _exportSelBtn = new TextButton { Text = "Export selected", Font = new Font("Segoe UI", 12f), Margin = new Padding(0, 0, 24, 0) };
        _exportSelBtn.Click += (s, e) => Export(true);
        _exportAllBtn.Click += (s, e) => Export(false);
        _showSelBtn.Click += (s, e) =>
        {
            if (_isBusy) { _cts?.Cancel(); return; }
            _showSelectedOnly = !_showSelectedOnly;
            _showSelBtn.ForeColor = _showSelectedOnly ? CfgGreen : CfgAccent;
            Refilter();
        };
        btnRow.Controls.Add(_showSelBtn);
        btnRow.Controls.Add(_exportAllBtn);
        btnRow.Controls.Add(_exportSelBtn);
        _buildModBtn = new TextButton { Text = "BUILD MOD", Font = new Font("Segoe UI", 12f), Margin = new Padding(0, 0, 24, 0) };
        _buildModBtn.Click += (s, e) => BuildMod();
        btnRow.Controls.Add(_buildModBtn);
        btnRow.WrapContents = false;

        content.Controls.Add(dirRow, 0, 0);
        content.SetColumnSpan(dirRow, 2);
        content.Controls.Add(_packLabel, 0, 1);
        content.Controls.Add(packHost, 0, 2);
        content.Controls.Add(rightPanel, 1, 2);
        content.Controls.Add(btnRow, 0, 3);
        content.SetColumnSpan(btnRow, 2);

        // status row (bottom-left) + red line ---------------
        var statusRow = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = CfgBg, Padding = new Padding(56, 8, 56, 0) };
        _status = new Label
        {
            Text = "Ready", Dock = DockStyle.Fill, AutoSize = false, ForeColor = CfgGreen,
            Font = new Font("Segoe UI", 11.5f), BackColor = CfgBg,
        };
        statusRow.Controls.Add(_status);
        _versionLabel = new Label { Text = AppVersion.Display, Dock = DockStyle.Right, Width = 70, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), TextAlign = ContentAlignment.MiddleRight };
        statusRow.Controls.Add(_versionLabel);
        var redline = new Panel { Dock = DockStyle.Bottom, Height = 2, BackColor = CfgAccent };

        Controls.Add(content);
        Controls.Add(statusRow);
        Controls.Add(redline);
        Controls.Add(_header);
    }

    static string FmtSize(long n) =>
        n >= (1 << 20) ? $"{n / 1048576.0:F2} MB" : n >= (1 << 10) ? $"{n / 1024.0:F1} KB" : $"{n} B";

    static void DrawCheckbox(Graphics g, int x, int y, bool check, bool selected, Color? force = null)
    {
        var col = force ?? (check ? Color.White : (selected ? Color.Black : CfgAccent));
        using var pen = new Pen(col, 1.6f);
        g.DrawRectangle(pen, x, y, 14, 14);
        if (check)
        {
            using var b = new SolidBrush(col);
            g.FillRectangle(b, x + 3, y + 3, 9, 9);
        }
    }

    void PackList_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tocs.Count) return;
        e.DrawBackground();
        bool check = _checkedPacks.Contains(e.Index);
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var b = new SolidBrush(sel ? Color.FromArgb(34, 34, 34) : CfgPanel);
        e.Graphics.FillRectangle(b, e.Bounds);
        DrawCheckbox(e.Graphics, e.Bounds.X + 8, e.Bounds.Y + (e.Bounds.Height - 14) / 2, check, sel, CfgAccent);
        var tf = _tocs[e.Index];
        TextRenderer.DrawText(e.Graphics, $"{tf.Pack}   [{tf.Entries.Count}]",
            _packList.Font,
            new Rectangle(e.Bounds.X + 30, e.Bounds.Y, e.Bounds.Width - 34, e.Bounds.Height),
            CfgAccent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if ((e.State & DrawItemState.Focus) != 0)
            e.DrawFocusRectangle();
    }

    void LayoutHeaderButtons()
    {
        int w = headerWidth;
        _maxBtn.Location = new Point(w - 104, 20);
        _closeBtn.Location = new Point(w - 58, 20);
        _githubBtn.Location = new Point(w - 220, 24);
        _langBtn.Location = new Point(w - 320, 24);
    }

    int headerWidth => (header != null ? header.ClientSize.Width : ClientSize.Width);

    void ApplyTexts()
    {
        Text = L.Title;
        _dirLabel.Text = L.GameDir;
        _browseBtn.Text = L.Browse;
        _showSelBtn.Text = _isBusy ? (L.En ? "Cancel" : "取消") : L.ShowSel;
        _packLabel.Text = L.Packs;
        _searchBox.PlaceholderText = L.Search;
        _exportSelBtn.Text = L.ExportSel;
        _exportAllBtn.Text = L.ExportFiltered;
        _langBtn.Text = L.En ? "中文" : "English";
        _list.Columns[0].Text = L.ColPath;
        _list.Columns[1].Text = L.ColSize;
        _list.Columns[2].Text = L.ColPack;
        _list.Columns[3].Text = L.ColType;
        ApplyModTexts();
        RefreshSelectionStatus();
        _list.Invalidate();
        _packList.Invalidate();
    }

    // ---------------------------------------------------------- logic

    void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = L.PickGameDir, ShowNewFolderButton = false };
        if (_dirBox.Text.Length > 0 && Directory.Exists(_dirBox.Text)) dlg.SelectedPath = _dirBox.Text;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var pc = Program.FindPcDir(dlg.SelectedPath);
        if (pc == null)
        {
            MessageBox.Show(this, L.NoPcDir, L.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _dirBox.Text = pc;
        _ = ScanAsync();
    }

    async Task ScanAsync()
    {
        string pc = _dirBox.Text.Trim();
        if (!Directory.Exists(pc) || !File.Exists(Path.Combine(pc, "base-generic.rmdtoc")))
        {
            pc = Program.FindPcDir(_dirBox.Text.Trim());
            if (pc == null) { MessageBox.Show(this, L.NoPcDir, L.Title); return; }
            _dirBox.Text = pc;
        }
        if (_replacements.Count > 0 && MessageBox.Show(this, L.En ? "Changing the game directory clears staged replacements. Continue?" : "重新扫描将清空已导入的修改，是否继续？", L.Title, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        _replacements.Clear(); _modReview = false; _selected.Clear();
        _pcDir = pc;
        _packList.Items.Clear();
        _checkedPacks.Clear();
        _tocs.Clear();
        _filtered.Clear();
        _list.VirtualListSize = 0;
        SetBusy(true, L.Scanning);

        var infos = TocFile.Discover(pc).ToList();
        try
        {
            await Task.Run(() =>
            {
                foreach (var info in infos)
                {
                    BeginInvoke(new Action(() => _status.Text = $"{L.Scanning} {info.Pack} ({_tocs.Count + 1}/{infos.Count})"));
                    var tf = TocFile.Load(info.TocPath);
                    BeginInvoke(new Action(() =>
                    {
                        _tocs.Add(tf);
                        _checkedPacks.Add(_tocs.Count - 1);
                        _packList.Items.Add(tf.Pack);
                        _packList.Invalidate();
                    }));
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "ERROR");
        }
        SetBusy(false, L.Ready);
        Refilter();
    }

    IEnumerable<TocFile> CheckedPacks()
    {
        for (int i = 0; i < _tocs.Count; i++)
            if (_checkedPacks.Contains(i)) yield return _tocs[i];
    }

    void Refilter()
    {
        var matcher = new Matcher(_searchBox.Text);
        _filtered = new List<ResourceEntry>();
        foreach (var tf in (_modReview ? _tocs : CheckedPacks()))
            foreach (var e in tf.Entries)
                if ((_modReview || matcher.Matches(e.Path)) && (!_showSelectedOnly || _modReview || _selected.ContainsKey(e.Key)) && (!_modReview || _replacements.ContainsKey(e.Key)))
                    _filtered.Add(e);
        _list.VirtualListSize = _filtered.Count;
        LayoutListColumns();
        _list.ScrollToTop();
        UpdateScrollBars();
        UpdatePackBar();
        _list.Invalidate(true);   // 全量重绘: 防缩小后残留旧行
        RefreshSelectionStatus();
    }

    async void Export(bool selectedOnly)
    {
        List<ResourceEntry> items;
        if (selectedOnly)
        {
            items = new List<ResourceEntry>();
            foreach (var tf in _tocs)
                foreach (var e in tf.Entries)
                    if (_selected.ContainsKey(e.Key)) items.Add(e);
            if (items.Count == 0)
            {
                MessageBox.Show(this, L.NothingMatch, L.Title);
                return;
            }
        }
        else items = _filtered;

        if (items.Count == 0) { MessageBox.Show(this, L.NothingMatch, L.Title); return; }
        long bytes = items.Sum(e => e.Size);
        if (MessageBox.Show(this, L.ConfirmExportMany(items.Count, bytes), L.Title,
                MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        using var dlg = new FolderBrowserDialog { Description = L.PickOutDir, ShowNewFolderButton = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string outRoot = dlg.SelectedPath;

        SetBusy(true, L.Exporting);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int exported = 0;
        var manifest = new StringBuilder(ResourceExporter.Header);
        try
        {
            var tocMap = _tocs.ToDictionary(t => t.Pack);
            await Task.Run(() =>
            {
                foreach (var e in items)
                {
                    token.ThrowIfCancellationRequested();
                    var tf = tocMap[e.Pack];
                    string row = ResourceExporter.Write(tf, e, outRoot, token);
                    manifest.AppendLine(row);
                    exported++;
                    if (exported % 50 == 0 || exported == items.Count)
                        BeginInvoke(new Action(() =>
                        {
                            _status.Text = $"{L.Exporting} {exported}/{items.Count}";
                        }));
                }
            });
            File.WriteAllText(Path.Combine(outRoot, "manifest.csv"), manifest.ToString());
            _status.Text = $"{L.Done}: {exported} {L.Manifest}";
            MessageBox.Show(this, $"{L.ExportDone}: {exported}\n{L.RawSuffixNote}", L.Title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        catch (OperationCanceledException) { _status.Text = L.ExportAborted; }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "ERROR");
        }
        finally
        {
            try { File.WriteAllText(Path.Combine(outRoot, "manifest.csv"), manifest.ToString()); } catch { }
            SetBusy(false, L.Ready);
            _cts.Dispose(); _cts = null;
        }
    }

    async void PreviewSelected()
    {
        if (_isBusy || _list.SelectedIndices.Count == 0) return;
        int index = _list.SelectedIndices[0];
        if (index < 0 || index >= _filtered.Count) return;
        var entry = _filtered[index];
        if (!CanPreview(entry)) return;
        var toc = _tocs.FirstOrDefault(t => t.Pack == entry.Pack);
        if (toc == null) return;
        SetBusy(true, L.En ? "Decoding preview..." : "正在解码预览...");
        try
        {
            var result = await Task.Run(() => _replacements.TryGetValue(entry.Key, out var replacement) ? (data: replacement.Data, raw: false) : toc.Extract(entry));
            if (result.raw) throw new NotSupportedException("Unsupported storage type");
            if(entry.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase))
            {
                var model=await Task.Run(()=>BinFbxDecoder.Decode(result.data));
                using var modelPreview=new ModelPreviewForm(entry.Name+(_replacements.ContainsKey(entry.Key)?(L.En?" [Modified]":" [已修改]"):""),model) { Icon=Icon };
                modelPreview.ShowDialog(this);return;
            }
            bool isDds = TextureDecoder.IsDds(result.data);
            string details = isDds ? TextureDecoder.Parse(result.data).ToString() : Path.GetExtension(entry.Path);
            using var bitmap = await Task.Run(() =>
            {
                if (isDds) return TextureDecoder.Decode(result.data);
                using var stream = new MemoryStream(result.data);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            });
            using var preview = new Form { Text = entry.Name + " — " + details, Width = 1000, Height = 800,
                StartPosition = FormStartPosition.CenterParent, BackColor = CfgPanel2, Icon = Icon };
            var picture = new PictureBox { Dock = DockStyle.Fill, Image = bitmap, SizeMode = PictureBoxSizeMode.Zoom };
            var note = new Label { Dock = DockStyle.Bottom, Height = 32, ForeColor = Color.White,
                Text = !isDds ? (L.En ? "Image preview" : "图片预览") : L.En ? "Mip 0 / surface 0. DDS export preserves every mip and surface." : "预览第 0 级 mip / 第 0 个表面；DDS 导出保留全部 mip 和表面。" };
            preview.Controls.Add(picture); preview.Controls.Add(note);
            preview.ShowDialog(this);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.En ? "Preview" : "预览"); }
        finally { SetBusy(false, L.Ready); }
    }

    void LayoutListColumns()
    {
        try
        {
            int others = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width;
            _list.Columns[0].Width = Math.Max(500, _list.ClientSize.Width - others);
        }
        catch { }
    }

    void UpdatePackBar()
    {
        if (_packBar == null || !_packList.IsHandleCreated) return;
        int visible = Math.Max(1, _packList.ClientSize.Height / Math.Max(1, _packList.ItemHeight));
        _packBar.UpdateRange(Math.Max(0, _packList.Items.Count - 1), visible, _packList.TopIndex);
    }

    void UpdateScrollBars()
    {
        if (_vbar == null || _hbar == null || !_list.IsHandleCreated) return;
        var v = _list.ReadScroll(true);
        var h = _list.ReadScroll(false);
        _vbar.UpdateRange(v.Maximum, v.Page, v.Position);
        _hbar.UpdateRange(h.Maximum, h.Page, h.Position);
    }

    bool _isBusy;
    void SetBusy(bool busy, string msg)
    {
        _isBusy = busy;
        _showSelBtn.Text = busy ? (L.En ? "Cancel" : "取消") : L.ShowSel;
        _browseBtn.Enabled = _langBtn.Enabled = !busy;
        _exportSelBtn.Enabled = _exportAllBtn.Enabled = !busy;
        _dirBox.Enabled = !busy;
        _buildModBtn.Enabled = !busy;
        _resourceMenu.Enabled = !busy;
        _status.Text = msg;
    }

    // borderless: resize + caption -----------------------------------
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            int lx = (short)(m.LParam.ToInt64() & 0xFFFF);
            int ly = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
            var pt = PointToClient(new Point(lx, ly));
            int g = 8;
            bool l = pt.X <= g, r = pt.X >= ClientSize.Width - g;
            bool t = pt.Y <= g, b = pt.Y >= ClientSize.Height - g;
            m.Result = (l && t) ? (IntPtr)13 : (r && t) ? (IntPtr)14 : (l && b) ? (IntPtr)16
                     : (r && b) ? (IntPtr)17 : t ? (IntPtr)12 : b ? (IntPtr)15
                     : l ? (IntPtr)10 : r ? (IntPtr)11 : IntPtr.Zero;
            if (m.Result != IntPtr.Zero) return;
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

internal sealed class DarkPackList : ListBox
{
    public event Action Scrolled;

    private const int WM_NCCALCSIZE = 0x83;
    private const int WM_VSCROLL = 0x115;
    private const int WM_MOUSEWHEEL = 0x20A;
    private const int WM_KEYDOWN = 0x100;
    private const int LB_GETTOPINDEX = 0x018E;
    private const int LB_SETTOPINDEX = 0x0197;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }


    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    public new int TopIndex
    {
        get => (int)SendMessage(Handle, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
        set => SendMessage(Handle, LB_SETTOPINDEX, (IntPtr)value, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            base.WndProc(ref m);
            var rct = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(m.LParam);
            rct.Right += SystemInformation.VerticalScrollBarWidth;
            System.Runtime.InteropServices.Marshal.StructureToPtr(rct, m.LParam, true);
            return;
        }
        base.WndProc(ref m);
        if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN) Scrolled?.Invoke();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
    }
}
