using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using RmdblobUnpacker.Core;

namespace RmdblobUnpacker;

public sealed class ModelPreviewForm : Form
{
    sealed class PreviewSurface : PictureBox
    {
        public PreviewSurface() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }
    readonly BinFbxModel model;
    readonly PictureBox picture = new PreviewSurface() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17,17,17), SizeMode = PictureBoxSizeMode.Zoom };
    readonly ComboBox lod = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.Black, ForeColor = Color.FromArgb(235,32,39), Width = 100 };
    readonly CheckBox wire = new() { Text = L.En ? "Wireframe" : "线框", AutoSize = true, ForeColor = Color.FromArgb(235,32,39), Margin = new Padding(14,5,8,0) };
    readonly Label status = new() { AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(12,5,0,0) };
    CancellationTokenSource rendering;
    float yaw = -.6f, pitch = -.25f, zoom = 1;
    Point mouse; bool dragging;
    public ModelPreviewForm(string name, BinFbxModel model)
    {
        this.model = model; Text = name; BackColor = Color.Black; ForeColor = Color.FromArgb(235,32,39);
        Font = new Font("Microsoft YaHei UI", 10); Width = 1100; Height = 800; MinimumSize = new Size(640,480); StartPosition = FormStartPosition.CenterParent;
        lod.DrawMode=DrawMode.OwnerDrawFixed;lod.ItemHeight=Font.Height+4;
        lod.DrawItem+=(_,e)=> { e.Graphics.FillRectangle(Brushes.Black,e.Bounds);if(e.Index>=0)TextRenderer.DrawText(e.Graphics,lod.Items[e.Index].ToString(),Font,e.Bounds,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter); };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12,6,12,6), BackColor = Color.Black };
        tools.Controls.Add(new Label {Text="LOD",AutoSize=true,Margin=new Padding(0,5,6,0)});tools.Controls.Add(lod); tools.Controls.Add(wire);
        var reset = new Button { Text = L.En ? "Reset view" : "重置视角", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = ForeColor, BackColor = Color.Black };
        reset.Click += (_,_) => { yaw=-.6f; pitch=-.25f; zoom=1; RequestRender(); }; tools.Controls.Add(reset); tools.Controls.Add(status);
        var note = new Label { Name = "PreviewHelp", Dock = DockStyle.Bottom, AutoSize = false, Padding = new Padding(12,6,12,8), ForeColor = Color.Gray,
            Text = L.En ? "Drag to rotate · Wheel to zoom · Choose LOD / wireframe\nGeometry preview; game textures/shaders are not displayed." : "拖动旋转 · 滚轮缩放 · 可切换 LOD / 线框\n显示模型几何，不显示游戏贴图和着色器效果。" };
        Controls.Add(picture); Controls.Add(note); Controls.Add(tools);
        // Measure wrapped text at the actual font/DPI and available width instead of reserving two fixed-height lines.
        void LayoutHelp()
        {
            int width=Math.Max(1,note.ClientSize.Width);
            int height=note.GetPreferredSize(new Size(width,0)).Height;
            if(note.Height!=height)note.Height=height;
        }
        note.SizeChanged+=(_,_)=>LayoutHelp();note.FontChanged+=(_,_)=>LayoutHelp();
        DpiChanged+=(_,_)=>BeginInvoke((Action)LayoutHelp);Shown+=(_,_)=>LayoutHelp();
        LayoutHelp();
        foreach(int n in model.Meshes.Select(m=>m.Lod).Distinct().Order()) lod.Items.Add(n);
        lod.SelectedItem = 0; lod.SelectedIndexChanged += (_,_)=>RequestRender(); wire.CheckedChanged += (_,_)=>RequestRender();
        picture.MouseDown += (_,e)=> { if(e.Button==MouseButtons.Left) { dragging=true; mouse=e.Location; picture.Capture=true; } };
        picture.MouseUp += (_,_)=> { dragging=false; picture.Capture=false; };
        picture.MouseMove += (_,e)=> { if(!dragging)return; yaw+=(e.X-mouse.X)*.008f; pitch=Math.Clamp(pitch+(e.Y-mouse.Y)*.008f,-1.55f,1.55f); mouse=e.Location; RequestRender(); };
        picture.MouseWheel += (_,e)=> { zoom=Math.Clamp(zoom*MathF.Pow(1.12f,e.Delta/120f),.1f,15); RequestRender(); };
        picture.MouseEnter += (_,_)=>picture.Focus(); picture.Resize += (_,_)=>RequestRender(); Shown += (_,_)=>RequestRender();
    }
    async void RequestRender()
    {
        if (!IsHandleCreated || IsDisposed || lod.SelectedItem is not int selected || picture.Width<10 || picture.Height<10) return;
        rendering?.Cancel(); var current = new CancellationTokenSource(); rendering=current;
        var meshes = model.Meshes.Where(m=>m.Lod==selected).ToArray();
        status.Text = L.En ? $"LOD {selected} · {meshes.Length} meshes · {meshes.Sum(m=>m.Indices.Length/3):N0} triangles" : $"LOD {selected} · {meshes.Length} 个网格 · {meshes.Sum(m=>m.Indices.Length/3):N0} 个三角形";
        int width=Math.Min(1600,picture.Width),height=Math.Min(1000,picture.Height);float y=yaw,p=pitch,z=zoom;bool lines=wire.Checked;
        Bitmap bitmap=null;
        try
        {
            bitmap=await Task.Run(()=>Render(meshes,width,height,y,p,z,lines,current.Token));
            if(!current.IsCancellationRequested&&!IsDisposed) { var old=picture.Image;picture.Image=bitmap;bitmap=null;old?.Dispose(); }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!IsDisposed&&!current.IsCancellationRequested) status.Text=(L.En?"Preview failed: ":"预览失败：")+ex.Message; }
        finally { bitmap?.Dispose();if(ReferenceEquals(rendering,current))rendering=null;current.Dispose(); }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){rendering?.Cancel();var old=picture.Image;picture.Image=null;old?.Dispose();}
        base.Dispose(disposing);
    }
    public static Bitmap Render(BinFbxMesh[] meshes,int width,int height,float yaw,float pitch,float zoom,bool wire,CancellationToken token=default)
    {
        var lo=new Vector3(float.PositiveInfinity);var hi=new Vector3(float.NegativeInfinity);
        foreach(var mesh in meshes)foreach(var v in mesh.Positions){lo=Vector3.Min(lo,v);hi=Vector3.Max(hi,v);}
        Vector3 center=(lo+hi)*.5f;float radius=Math.Max((hi-lo).Length()*.5f,.00001f);float scale=Math.Min(width,height)*.44f*zoom;
        var rotation=Matrix4x4.CreateRotationY(yaw)*Matrix4x4.CreateRotationX(pitch);
        var pixels=new int[checked(width*height)];Array.Fill(pixels,unchecked((int)0xff111111));var depth=new float[pixels.Length];Array.Fill(depth,float.NegativeInfinity);
        float Edge(Vector3 a,Vector3 b,float x,float y)=>(x-a.X)*(b.Y-a.Y)-(y-a.Y)*(b.X-a.X);
        void Line(Vector3 a,Vector3 b)
        {
            int steps=Math.Min(5000,(int)MathF.Ceiling(MathF.Max(MathF.Abs(b.X-a.X),MathF.Abs(b.Y-a.Y))));
            for(int i=0;i<=steps;i++){float t=steps==0?0:(float)i/steps;var v=Vector3.Lerp(a,b,t);int x=(int)v.X,y=(int)v.Y;if(x<0||y<0||x>=width||y>=height)continue;int at=y*width+x;if(v.Z>=depth[at]-.006f)pixels[at]=unchecked((int)0xffef383e);}
        }
        var projected=new List<(Vector3[] Vertices,int[] Indices)>();int meshIndex=0;
        foreach(var mesh in meshes)
        {
            token.ThrowIfCancellationRequested();var vertices=new Vector3[mesh.Positions.Length];
            for(int i=0;i<vertices.Length;i++){var v=Vector3.Transform((mesh.Positions[i]-center)/radius,rotation);vertices[i]=new(width*.5f+v.X*scale,height*.5f-v.Y*scale,v.Z);}
            projected.Add((vertices,mesh.Indices));
            foreach(int face in Enumerable.Range(0,mesh.Indices.Length/3))
            {
                if((face&127)==0)token.ThrowIfCancellationRequested();int at=face*3;
                var a=vertices[mesh.Indices[at]];var b=vertices[mesh.Indices[at+1]];var c=vertices[mesh.Indices[at+2]];
                float area=Edge(a,b,c.X,c.Y);if(MathF.Abs(area)<.00001f)continue;
                var n=Vector3.Cross((mesh.Positions[mesh.Indices[at+1]]-mesh.Positions[mesh.Indices[at]])/radius,(mesh.Positions[mesh.Indices[at+2]]-mesh.Positions[mesh.Indices[at]])/radius);
                n=Vector3.TransformNormal(n,rotation);float light=.25f+.75f*MathF.Abs(Vector3.Dot(Vector3.Normalize(n),Vector3.Normalize(new(.3f,.7f,1))));
                int red=(int)((meshIndex%2==0?192:170)*light),green=(int)((meshIndex%2==0?198:145)*light),blue=(int)((meshIndex%2==0?208:134)*light);
                int color=unchecked((int)0xff000000)|(red<<16)|(green<<8)|blue;
                int left=Math.Clamp((int)MathF.Floor(MathF.Min(a.X,MathF.Min(b.X,c.X))),0,width-1),right=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X,MathF.Max(b.X,c.X))),0,width-1);
                int top=Math.Clamp((int)MathF.Floor(MathF.Min(a.Y,MathF.Min(b.Y,c.Y))),0,height-1),bottom=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y,MathF.Max(b.Y,c.Y))),0,height-1);
                for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
                {
                    float w0=Edge(b,c,x+.5f,y+.5f)/area,w1=Edge(c,a,x+.5f,y+.5f)/area,w2=1-w0-w1;if(w0<0||w1<0||w2<0)continue;
                    float z=w0*a.Z+w1*b.Z+w2*c.Z;int index=y*width+x;if(z>depth[index]){depth[index]=z;pixels[index]=color;}
                }
            }
            meshIndex++;
        }
        if(wire)foreach(var mesh in projected)for(int i=0;i<mesh.Indices.Length;i+=3){if((i&1023)==0)token.ThrowIfCancellationRequested();var a=mesh.Vertices[mesh.Indices[i]];var b=mesh.Vertices[mesh.Indices[i+1]];var c=mesh.Vertices[mesh.Indices[i+2]];Line(a,b);Line(b,c);Line(c,a);}
        token.ThrowIfCancellationRequested();var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        var bits=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{Marshal.Copy(pixels,0,bits.Scan0,pixels.Length);}finally{bitmap.UnlockBits(bits);}return bitmap;
    }
}
