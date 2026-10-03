namespace RmdblobUnpacker;
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    { e.TextColor = e.Item.Enabled ? MainForm.CfgAccent : Color.Gray; base.OnRenderItemText(e); }
    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.Black;
        public override Color ImageMarginGradientBegin => Color.Black;
        public override Color ImageMarginGradientMiddle => Color.Black;
        public override Color ImageMarginGradientEnd => Color.Black;
        public override Color MenuItemSelected => Color.FromArgb(30, 30, 30);
        public override Color MenuItemBorder => MainForm.CfgDim;
        public override Color MenuBorder => MainForm.CfgDim;
        public override Color SeparatorDark => MainForm.CfgDim;
        public override Color SeparatorLight => Color.Black;
    }
}
