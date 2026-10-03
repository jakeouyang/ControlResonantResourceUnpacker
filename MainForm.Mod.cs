using RmdblobUnpacker.Core;

namespace RmdblobUnpacker;
public sealed partial class MainForm
{
    readonly Dictionary<string,ModReplacement> _replacements = new();
    bool _modReview;
    TextButton _buildModBtn;
    Label _versionLabel;
    ContextMenuStrip _resourceMenu;
    ToolStripMenuItem _previewItem, _importItem, _removeItem, _allResourcesItem;
    static bool CanPreview(ResourceEntry entry) => entry != null && Path.GetExtension(entry.Path).ToLowerInvariant() is
        ".tex" or ".dds" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".binfbx";
    ResourceEntry CurrentEntry() => _list.SelectedIndices.Count>0 && _list.SelectedIndices[0]<_filtered.Count
        ? _filtered[_list.SelectedIndices[0]] : null;
    void BuildResourceMenu()
    {
        _resourceMenu=new ContextMenuStrip { BackColor=Color.Black, ForeColor=CfgAccent, ShowImageMargin=false, Renderer=new DarkMenuRenderer() };
        _previewItem=new ToolStripMenuItem();_previewItem.Click+=(s,e)=>PreviewSelected();
        _importItem=new ToolStripMenuItem();_importItem.Click+=(s,e)=>ImportReplacement();
        _removeItem=new ToolStripMenuItem();_removeItem.Click+=(s,e)=>
        {var entry=CurrentEntry();if(entry!=null)_replacements.Remove(entry.Key);Refilter();ApplyModTexts();};
        _allResourcesItem=new ToolStripMenuItem();_allResourcesItem.Click+=(s,e)=>{_modReview=false;Refilter();ApplyModTexts();};
        _resourceMenu.Items.AddRange(new ToolStripItem[]{_previewItem,_importItem,_removeItem,new ToolStripSeparator(),_allResourcesItem});
        _resourceMenu.Opening+=(s,e)=>
        {
            var entry=CurrentEntry();_previewItem.Enabled=!_isBusy&&CanPreview(entry);
            _importItem.Enabled=!_isBusy&&entry!=null&&ModImport.Supports(entry);
            _removeItem.Enabled=!_isBusy&&entry!=null&&_replacements.ContainsKey(entry.Key);
            _allResourcesItem.Enabled=!_isBusy&&_modReview;
        };
        _list.ContextMenuStrip=_resourceMenu;
    }
    void ApplyModTexts()
    {
        _previewItem.Text=L.En?"Preview":"预览";
        _importItem.Text=L.En?"Import replacement...":"导入替换文件…";
        _removeItem.Text=L.En?"Remove replacement":"移除导入修改";
        _allResourcesItem.Text=L.En?"Back to resources":"返回资源列表";
        _buildModBtn.Text=_modReview?(L.En?"PACK MOD":"生成 MOD"):(L.En?"BUILD MOD":"制作 MOD");
    }
    void RefreshSelectionStatus()
    {
        if(_status==null)return;
        if(_modReview) _status.Text=L.En?$"{_replacements.Count} modified files — click PACK MOD to package all changes":$"修改清单：{_replacements.Count} 个文件，再次点击“生成 MOD”打包全部修改";
        else if(_tocs.Count==0)_status.Text=L.Ready;
        else _status.Text=L.En?$"{_selected.Count} selected / {_filtered.Count} shown / {_replacements.Count} modified":$"已选择 {_selected.Count} / 显示 {_filtered.Count} / 已修改 {_replacements.Count}";
    }
    async void ImportReplacement()
    {
        var entry=CurrentEntry();if(_isBusy||entry==null||!ModImport.Supports(entry))return;
        using var dialog=new OpenFileDialog {Title=L.En?"Import replacement for "+entry.Name:"导入替换文件："+entry.Name,
            Filter=entry.Path.EndsWith(".tex",StringComparison.OrdinalIgnoreCase)
            ?(L.En?"Texture (PNG / DDS / TEX)|*.png;*.dds;*.tex":"纹理（PNG / DDS / TEX）|*.png;*.dds;*.tex")
            :entry.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase)
            ?(L.En?"FBX model (*.fbx)|*.fbx":"FBX 模型（*.fbx）|*.fbx")
            :(L.En?"TrueType font (*.ttf)|*.ttf":"TrueType 字体（*.ttf）|*.ttf")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        SetBusy(true,L.En?"Validating replacement...":"正在校验导入文件…");
        try
        {
            var toc=_tocs.First(t=>t.Pack==entry.Pack);
            var replacement=await Task.Run(()=>ModImport.Prepare(toc,entry,dialog.FileName));
            _replacements[entry.Key]=replacement;
            if(entry.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase))
                MessageBox.Show(this,replacement.Validation+"\n\n"+(L.En?"Preview the modified model, then use BUILD MOD. Offline validation does not guarantee in-game compatibility.":"可双击预览修改结果，再点击“制作 MOD”。离线校验不等于已验证游戏内兼容性。"),L.En?"Model imported":"模型已导入",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,L.En?"Import failed":"导入失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally {SetBusy(false,L.Ready);_list.Invalidate();ApplyModTexts();RefreshSelectionStatus();}
    }
    async void BuildMod()
    {
        if(_isBusy)return;
        if(_replacements.Count==0)
        {MessageBox.Show(this,L.En?"Right-click a TEX, TTF or BINFBX resource and import a replacement first.":"请先右键选择 TEX、TTF 或 BINFBX 资源，导入替换文件。",L.Title);return;}
        if(!_modReview){_modReview=true;Refilter();ApplyModTexts();return;}
        using var dialog=new FolderBrowserDialog {Description=L.En?"Choose where to create the MOD package (outside the game folder)":"选择 MOD 包的生成位置（游戏目录之外）",ShowNewFolderButton=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var replacements=_replacements.Values.ToArray();
        string output=Path.Combine(dialog.SelectedPath,"CONTROL-Resonant-Mod-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        _cts=new CancellationTokenSource();var token=_cts.Token;
        SetBusy(true,L.En?"Building and verifying MOD...":"正在生成并回读校验 MOD…");
        try
        {
            await Task.Run(()=>ModBuilder.Build(output,replacements,token));
            MessageBox.Show(this,(L.En?"MOD created and verified:\n":"MOD 已生成并通过回读校验：\n")+output,L.Title);
        }
        catch(OperationCanceledException){MessageBox.Show(this,L.ExportAborted,L.Title);}
        catch(Exception ex){MessageBox.Show(this,ex.Message,L.En?"MOD build failed":"MOD 生成失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{_cts.Dispose();_cts=null;SetBusy(false,L.Ready);RefreshSelectionStatus();}
    }
}
