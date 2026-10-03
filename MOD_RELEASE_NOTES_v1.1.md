# Control Resonant Resource Unpacker v1.1

## 中文说明

本版本重点增强资源预览、导出与 MOD 制作流程：

- TEX 纹理支持双击预览，并支持导出为可直接查看的图片格式。
- TEX 纹理支持导入替换，可用于制作开发包 MOD。
- WEM 音频支持导出为 WAV，方便试听与后续编辑。
- TTF 字体支持导入替换，并可与 TEX 一起打包为 MOD。
- 新增 BUILD MOD 功能，可将已导入修改的 TEX/TTF 资源生成开发包 MOD。
- 双击预览仅对 TEX、DDS 与常见图片格式生效，其他格式不会弹窗打扰。

计划支持：

- BINFBX 转 FBX，用于导出普通 3D 模型文件。
- 继续分析更多资源格式，逐步扩展可导出、可预览和可制作 MOD 的类型。

注意：

- 当前 MOD 打包功能主要面向 TEX 与 TTF 资源。
- BINFBX 转 FBX 尚未完成，原因是 CONTROL Resonant 使用的 BINFBX 版本与已有公开工具格式不完全一致，仍需进一步解析模型结构。

## English Notes

This release improves resource preview, export, and mod package creation:

- TEX textures can now be opened by double-clicking and exported as viewable image files.
- TEX textures can be imported as replacements for development mod packages.
- WEM audio can now be exported as WAV for playback and editing.
- TTF fonts can be imported as replacements and packed together with TEX changes.
- Added BUILD MOD, which packages imported TEX/TTF replacements into a development mod.
- Double-click preview only applies to TEX, DDS, and common image formats, so unsupported files no longer show repeated popups.

Planned support:

- BINFBX to FBX conversion for standard 3D model export.
- Continued research into more resource formats for export, preview, and mod creation.

Notes:

- The current mod builder is focused on TEX and TTF resources.
- BINFBX to FBX is not implemented yet because CONTROL Resonant uses a BINFBX version that does not match the currently available public tools. More format analysis is still needed.
