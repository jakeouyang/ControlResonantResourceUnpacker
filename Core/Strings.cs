namespace RmdblobUnpacker.Core;

/// <summary>Bilingual UI strings. ZH default, EN toggle.</summary>
public static class L
{
    public static bool En = true;

    static string P(string zh, string en) => En ? en : zh;

    public static string Title          => P("CONTROL Resonant 资源解包导出器", "CONTROL Resonant Resource Unpacker");
    public static string GameDir        => P("游戏目录：", "Game directory:");
    public static string Browse         => P("浏览...", "Browse...");
    public static string Scan           => P("扫描资源", "Scan resources");
    public static string Packs        => P("资源包", "Packs");
    public static string Search         => P("模糊搜索文件名（支持空格分隔多个关键字、* 通配符）", "Fuzzy search (space = AND, * wildcard)");
    public static string ColPath        => P("文件路径", "Path");
    public static string ColSize        => P("大小", "Size");
    public static string ColPack        => P("资源包", "Pack");
    public static string ColType        => P("格式", "Format");
    public static string TypeLz4        => P("LZ4", "LZ4");
    public static string TypeEngine     => P("引擎专用", "Engine");
    public static string ExportSel      => P("导出选中", "Export selected");
    public static string ExportFiltered => P("导出筛选结果", "Export filtered");
    public static string Cancel         => P("取消", "Cancel");
    public static string Ready          => P("就绪", "Ready");
    public static string Scanning       => P("正在解析资源包 ...", "Parsing packs ...");
    public static string Exporting      => P("正在导出 ...", "Exporting ...");
    public static string Done           => P("完成", "Done");
    public static string PickGameDir    => P("选择游戏根目录（包含 CONTROLResonant.exe 或 data_pack2）", "Select game root (contains CONTROLResonant.exe or data_pack2)");
    public static string PickOutDir     => P("选择导出输出目录", "Select output directory");
    public static string NoPcDir        => P("未找到 data_pack2\\pc，请选择游戏根目录或其上级目录。", "data_pack2\\pc not found. Pick the game root or a parent folder.");
    public static string NeedScan       => P("请先扫描资源。", "Scan resources first.");
    public static string NothingMatch   => P("没有匹配的资源。", "No matching resources.");
    public static string Entries        => P("条资源", "entries");
    public static string ExportDone     => P("导出完成", "Export finished");
    public static string ExportAborted  => P("导出已取消", "Export cancelled");
    public static string ShowSel        => P("只看已选", "Selected only");
    public static string LangToggle     => P("English", "中文");
    public static string Manifest       => P("已生成清单 manifest.csv", "manifest.csv written");
    public static string RawSuffixNote  => P("纹理额外导出 DDS；支持的纹理同时导出 PNG。详见 manifest.csv。", "Textures include DDS and supported PNG conversions. See manifest.csv.");
    public static string ConfirmExportMany(int n, long bytes) =>
        P($"共 {n} 条资源，约 {FmtSize(bytes)}，确定导出？", $"Export {n} entries, approx {FmtSize(bytes)}?");
    public static string Deleted        => P("[引擎压缩-原始导出]", "[engine-compressed-raw]");

    public static string FmtSize(long b)
    {
        if (b >= 1 << 30) return (b / (double)(1 << 30)).ToString("0.##") + " GB";
        if (b >= 1 << 20) return (b / (double)(1 << 20)).ToString("0.##") + " MB";
        if (b >= 1 << 10) return (b / (double)(1 << 10)).ToString("0.#") + " KB";
        return b + " B";
    }
}
