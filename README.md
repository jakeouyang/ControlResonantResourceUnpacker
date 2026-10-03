# Control Resonant Resource Unpacker

Windows x64 / .NET 9 WinForms tool for browsing and exporting resources from Control: Resonant Edition archives.

[中文](#中文) · [English](#english) · [Releases](https://github.com/jakeouyang/ControlResonantResourceUnpacker/releases) · [Bilibili](https://space.bilibili.com/8480063)

## 中文

这是一个面向 Windows x64 的 Control: Resonant Edition 资源浏览、导出和开发包制作工具。程序启动时不会读取默认目录，请手动选择包含 `data_pack2/pc` 的游戏目录。

功能：

- 浏览、搜索和导出 RTOC/blob 资源；
- TEX/DDS/常见图片预览，TEX 导出 DDS/PNG；
- WEM 导出 WAV；
- TTF、TEX 和受支持的静态 BINFBX 导入校验；
- 静态 BINFBX 几何预览、LOD/线框切换和受限 FBX 回导；
- 在游戏目录之外生成 development MOD，不修改原始游戏包；
- 中英文界面、单 EXE 发布、内嵌 `app.ico`。

BINFBX 支持范围请阅读 [FORMAT_RESEARCH.md](FORMAT_RESEARCH.md)。FBX 回导不是通用模型转换器：目前要求保留拓扑、网格名称、材质槽和 UV 层，只支持整体平移、正向等比缩放及整层 UV 变换。骨骼、蒙皮、形态键、动画、局部变形和拓扑重建尚未实现。回导结果已做离线结构校验，但没有游戏内兼容性保证。

### 使用

1. 启动程序并选择游戏目录。
2. 双击 TEX、图片或受支持的静态 BINFBX 进行预览。
3. 右键 TEX/TTF/BINFBX 导入替换文件。
4. 点击“制作 MOD”，确认修改后点击“生成 MOD”，选择游戏目录之外的输出目录。

### 命令行

```powershell
.\RmdblobUnpacker.exe --headless "<game-directory>" "<output-directory>" "*.binfbx"
```

### 开发与构建

需要 .NET 9 SDK 和 Windows x64 环境：

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o publish
```

推送 `v*` 标签后，GitHub Actions 会自动构建单 EXE 并创建 GitHub Release。发布包不包含游戏资源、测试样本或本机路径。

第三方许可见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

## English

Control Resonant Resource Unpacker is a Windows x64 / .NET 9 WinForms tool for browsing and exporting resources from Control: Resonant Edition archives. It does not load a default directory on startup; select a game directory containing `data_pack2/pc`.

Features include archive browsing and search, TEX/DDS/image preview, TEX export, WEM-to-WAV export, validated TTF/TEX/static-BINFBX replacement imports, static BINFBX geometry preview, restricted FBX round-trip editing, and development MOD packaging outside the game directory. The release executable is self-contained and includes `app.ico`.

See [FORMAT_RESEARCH.md](FORMAT_RESEARCH.md) for the BINFBX support boundary. FBX re-import is intentionally conservative: preserve mesh names, topology, material slots and UV layers; whole-model translation, positive uniform scale and whole-layer UV transforms are supported. Skeletons, skinning, blend shapes, animation, local deformation and topology rebuild are not implemented. Offline validation does not guarantee in-game compatibility.

Pushing a `v*` tag runs GitHub Actions, builds a single-file Windows executable and attaches it to a GitHub Release. Game data, samples and machine-specific paths are excluded from the repository.

See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for dependency licenses.
