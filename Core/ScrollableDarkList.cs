using System.Drawing.Drawing2D;

namespace RmdblobUnpacker.Core;

/// <summary>Virtual resource table. Only visible rows are drawn; headers never scroll vertically.</summary>
public sealed class ScrollableDarkList : Control
{
    public sealed record Row(string[] Cells, bool Checked);
    public List<ColumnHeader> Columns { get; } = new();
    public Func<int, Row> RetrieveRow { get; set; }
    public List<int> SelectedIndices { get; } = new();
    public event EventHandler SelectedIndexChanged;
    public event Action<int> VScrolled;
    int _count, _top, _left, _wheel;
    public int RowHeight => Font.Height + Math.Max(6, DeviceDpi / 16);
    public int HeaderHeight => Font.Height + Math.Max(10, DeviceDpi / 12);
    int Page => Math.Max(1, (ClientSize.Height - HeaderHeight) / RowHeight);
    int ContentWidth => Columns.Sum(c => c.Width);
    public int VirtualListSize
    {
        get => _count;
        set { _count = Math.Max(0, value); SelectedIndices.Clear(); Clamp(); Invalidate(); }
    }
    public ScrollableDarkList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }
    public (int Maximum, int Page, int Position) ReadScroll(bool vertical) => vertical
        ? (Math.Max(0, _count - 1), Page, _top)
        : (Math.Max(0, ContentWidth - 1), Math.Max(1, ClientSize.Width), _left);
    void Clamp()
    {
        _top = Math.Clamp(_top, 0, Math.Max(0, _count - Page));
        _left = Math.Clamp(_left, 0, Math.Max(0, ContentWidth - ClientSize.Width));
    }
    public void ScrollBy(int dx, int dy)
    {
        _top += dy / RowHeight; _left += dx; Clamp(); Invalidate(); VScrolled?.Invoke(0);
    }
    public void ScrollToTop() { _top = _left = 0; Invalidate(); VScrolled?.Invoke(0); }
    public int HitIndex(Point point)
    {
        if (point.Y < HeaderHeight || point.Y >= Height) return -1;
        int i = _top + (point.Y - HeaderHeight) / RowHeight;
        return i < _count ? i : -1;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Clamp(); VScrolled?.Invoke(0); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _wheel += e.Delta; int steps = _wheel / 120; _wheel %= 120;
        int lines = SystemInformation.MouseWheelScrollLines;
        if ((ModifierKeys & Keys.Shift) != 0) ScrollBy(-steps * 60, 0);
        else ScrollBy(0, -steps * (lines < 0 ? Page : lines) * RowHeight);
        base.OnMouseWheel(e);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus(); int i = HitIndex(e.Location);
        SelectedIndices.Clear(); if (i >= 0) SelectedIndices.Add(i);
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int target = SelectedIndices.Count > 0 ? SelectedIndices[0] : _top;
        switch(e.KeyCode)
        {
            case Keys.Down: target++; break;
            case Keys.Up: target--; break;
            case Keys.PageDown: target += Page; break;
            case Keys.PageUp: target -= Page; break;
            case Keys.Home: target = 0; break;
            case Keys.End: target = _count - 1; break;
            case Keys.Left: ScrollBy(-60, 0); return;
            case Keys.Right: ScrollBy(60, 0); return;
            default: base.OnKeyDown(e); return;
        }
        if (_count > 0)
        {
            target = Math.Clamp(target, 0, _count - 1);
            SelectedIndices.Clear(); SelectedIndices.Add(target);
            if (target < _top) _top = target;
            if (target >= _top + Page) _top = target - Page + 1;
            Clamp(); Invalidate(); VScrolled?.Invoke(0); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var headerFont = new Font(Font, FontStyle.Bold);
        using var line = new Pen(Color.FromArgb(60, MainForm.CfgAccent));
        int x = -_left;
        foreach(var col in Columns)
        {
            TextRenderer.DrawText(e.Graphics, col.Text, headerFont, new Rectangle(x + 4, 0, col.Width - 8, HeaderHeight),
                MainForm.CfgAccent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x += col.Width;
        }
        e.Graphics.DrawLine(line, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
        var saved = e.Graphics.Save();
        e.Graphics.SetClip(new Rectangle(0, HeaderHeight, Width, Math.Max(0, Height - HeaderHeight)));
        int visible = (Height - HeaderHeight + RowHeight - 1) / RowHeight;
        for (int index = _top; index < Math.Min(_count, _top + visible); index++)
        {
            var row = RetrieveRow?.Invoke(index); if (row == null) continue;
            int y = HeaderHeight + (index - _top) * RowHeight; x = -_left;
            for (int c = 0; c < Columns.Count; c++)
            {
                var color = row.Checked ? Color.White : c == 0 ? MainForm.CfgAccent : MainForm.CfgDim;
                int inset = 4;
                if (c == 0)
                {
                    int side = Math.Max(10, Font.Height / 2), cy = y + (RowHeight - side) / 2;
                    using var pen = new Pen(color);
                    e.Graphics.DrawRectangle(pen, x + 4, cy, side, side);
                    if (row.Checked) { using var fill = new SolidBrush(color); e.Graphics.FillRectangle(fill, x + 7, cy + 3, side - 5, side - 5); }
                    inset = side + 12;
                }
                TextRenderer.DrawText(e.Graphics, c < row.Cells.Length ? row.Cells[c] : "", Font,
                    new Rectangle(x + inset, y, Math.Max(1, Columns[c].Width - inset - 4), RowHeight), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                x += Columns[c].Width;
            }
        }
        e.Graphics.Restore(saved);
    }
}
