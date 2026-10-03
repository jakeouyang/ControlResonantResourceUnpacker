using System.Windows.Forms;

namespace RmdblobUnpacker;

/// <summary>Flat text button with red accent, optional dashed border (mockup style).</summary>
public sealed class TextButton : Button
{
    static readonly Color Accent = MainForm.CfgAccent;
    static readonly Color AccentHi = MainForm.CfgAccentHi;
    bool _hover;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DashedBorder { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Padding Padding2 { get; set; } = new Padding(10, 6, 10, 6);

    public TextButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Black;
        ForeColor = Accent;
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);
        MouseEnter += (s, e) => { _hover = true; Invalidate(); };
        MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        EnabledChanged += (s, e) => Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var pad = DashedBorder ? Padding2 : new Padding(4);
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(text.Width + pad.Horizontal + 4, text.Height + pad.Vertical + 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var fg = Enabled ? (_hover ? AccentHi : Accent) : Color.FromArgb(110, 110, 110);
        if (DashedBorder)
        {
            using var pen = new Pen(fg, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        else if (_hover && Enabled)
        {
            using var uPen = new Pen(fg, 1f);
            g.DrawLine(uPen, 0, Height - 1, Width, Height - 1);
        }
        var pad = DashedBorder ? Padding2 : new Padding(4, 4, 4, 4);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(pad.Left, pad.Top, Width - pad.Horizontal, Height - pad.Vertical),
            fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
    }
}
