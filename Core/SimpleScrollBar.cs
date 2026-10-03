using System.Drawing.Drawing2D;

namespace RmdblobUnpacker.Core;

/// <summary>Custom scrollbar: black track, red thumb.</summary>
public sealed class SimpleScrollBar : Control
{
    public bool Vertical { get; }
    int _max = 100, _page = 100, _val;
    bool _drag;
    int _dragOffset;
    bool _hover;

    public event Action ValueChanged;

    static readonly Color Track = Color.FromArgb(10, 10, 10);
    static readonly Color Thumb = Color.FromArgb(226, 34, 34);
    static readonly Color ThumbHi = Color.FromArgb(255, 96, 96);

    public SimpleScrollBar(bool vertical)
    {
        Vertical = vertical;
        BackColor = Track;
        Cursor = Cursors.Default;
        if (vertical) Width = 12; else Height = 12;
    }

    /// <summary>max = last valid scroll offset + page (win32 style).</summary>
    public void UpdateRange(int max, int page, int value)
    {
        int nmax = Math.Max(0, max);
        int npage = Math.Max(1, page);
        int nval = Math.Clamp(value, 0, Math.Max(0, nmax - Math.Max(0, npage - 1)));
        bool dirty = nmax != _max || npage != _page || nval != _val;
        _max = nmax; _page = npage; _val = nval;
        Enabled = MaxValue > 0;
        if (dirty) Invalidate();
    }

    public int Value
    {
        get => _val;
        set
        {
            var v = Math.Clamp(value, 0, MaxValue);
            if (v == _val) return;
            _val = v;
            Invalidate();
            ValueChanged?.Invoke();
        }
    }

    int MaxValue => Math.Max(0, _max - Math.Max(0, _page - 1));

    float TrackLen => (Vertical ? Height : Width) - 2;

    float ThumbLen
    {
        get
        {
            float total = _max + 1;
            if (total <= 0 || _page <= 0) return TrackLen;
            var f = Math.Clamp(_page / total, 0.05f, 1f);
            return TrackLen * f;
        }
    }

    int PosToFrac => MaxValue <= 0 ? 0 : _max; // px range equals scroll range

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Track);
        float tl = TrackLen;
        float thumb = ThumbLen;
        float f = MaxValue <= 0 ? 0 : (float)_val / MaxValue;
        float pos = 1 + f * (tl - thumb - 2);
        using var b = new SolidBrush(_hover || _drag ? ThumbHi : Thumb);
        var r = Vertical
            ? new RectangleF(2, pos, Width - 5, thumb)
            : new RectangleF(pos, 2, thumb, Height - 5);
        using var path = Rounded(r, 4);
        e.Graphics.FillPath(b, path);
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
        p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
        p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    void ThumbRect(out RectangleF r)
    {
        float tl = TrackLen, thumb = ThumbLen;
        float f = MaxValue <= 0 ? 0 : (float)_val / MaxValue;
        float pos = 1 + f * (tl - thumb - 2);
        r = Vertical ? new RectangleF(2, pos, Width - 4, thumb)
                     : new RectangleF(pos, 2, thumb, Height - 4);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ThumbRect(out var r);
        bool onThumb = Vertical
            ? e.Y >= r.Y && e.Y <= r.Bottom
            : e.X >= r.X && e.X <= r.Right;
        if (onThumb)
        {
            _drag = true;
            _dragOffset = (int)((Vertical ? e.Y : e.X) - (Vertical ? r.Y : r.X));
            Capture = true;
        }
        else
        {
            // page jump
            int v = Vertical ? e.Y : e.X;
            float f = (v - ThumbLen / 2f) / Math.Max(1, TrackLen - ThumbLen);
            Value = (int)Math.Clamp(f * MaxValue, 0, MaxValue);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
        {
            float avail = Math.Max(1, TrackLen - ThumbLen - 2);
            float v = ((Vertical ? e.Y : e.X) - _dragOffset - 1) / avail * MaxValue;
            Value = (int)Math.Clamp(v, 0, MaxValue);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Capture = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
}
