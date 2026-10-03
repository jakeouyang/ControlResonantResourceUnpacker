# 格式研究（2026-09-28）

## WEM

实际非空样本使用 Wwise Custom Vorbis。官方 vgmstream r2117 成功转换 6 个样本为 48 kHz PCM WAV，已集成 GUI/CLI 共用导出流程。空 WEM 是资源占位，不能生成音频。转换失败保留原始数据，在 manifest 记录原因。

- 官方发布：https://github.com/vgmstream/vgmstream/releases/tag/r2117
- CLI 文档：https://github.com/vgmstream/vgmstream/blob/master/doc/USAGE.md
- 内嵌 ZIP SHA256：6C4A8A3813864FEFED081BBD337DBC0AD93BF88E0B92F5DB98D7AB258B22DC6C

## BINFBX

本机提取的 ladder_a_folded、pie_tray_a_cherry 等实际样本首个 little-endian u32 为 0x5C（92），不是通用 FBX。不能只改后缀。旧布局预期的顶点/索引大小位置在这些样本中为零，尚未建立可靠的缓冲区定位和顶点布局。

比较过以下源代码/格式说明：

- https://github.com/Kwizatz/control-modding ：旧 Control BINFBX 说明使用不同魔数/布局。
- https://github.com/riverence/io_scene_binfbx ：Alan Wake 2 导入器依赖模型特定偏移，不能通用于本游戏样本。
- https://github.com/OpenAWE-Project/BlenderNorthlight ：加载器支持版本 19、20、21、43，未支持 92。

上述源文件仅用于研究，未作为转换器集成。2026-09-28 已通过真实样本建立版本 92 **静态模型**解析器 `Core/BinFbxDecoder.cs` 和二进制 FBX 7.4 写入器 `Core/FbxWriter.cs`，接入 GUI/CLI 共用导出流程。

### 已验证结构

- 文件头 u32 版本 92、u32 LOD 数量。当前只接受接下来 9 个 u32 均为零的静态布局。带骨骼/pose 数据的头部不按静态结构强行跳过。
- 全局 scale、LOD 阈值数组、坐标符号、全局球体与 AABB，随后每个 LOD 的量化球体（中心 XYZ 和半径）。当前 scale 必须为 1。
- 材质为 u64 ID 数组，并有逐子网格主映射与命名备选映射。未找到可靠的引擎材质/纹理还原路径，因此只输出以 ID 命名的材质槽。
- 渲染图为长度前缀 u32 数组，后有两个标记字节、LOD 命令表，再到子网格表。通过结构逐段读取，不扫描字节猜顶点起点，不按模型名硬编码偏移。
- 网格记录包括 LOD、顶点/三角形计数、两个顶点流偏移、索引宽度/偏移、包围盒、属性描述符以及 meshlet 数量/偏移。顶点属性明确区分 buffer、format、usage。
- buffer 1（法线/UV 等）先存储，buffer 0（位置）后存储，之后是 16/32 位索引，再后是 meshlet 描述、局部顶点重映射、三角形和包围体数据。LOD 偏移重置标识新流组；每组范围及文件末尾均须完全吻合。
- 位置 format 8：signed int16 XYZ / 32767 × LOD 球半径 + 球中心；format 2 为 float32 XYZ。UV format 7 为 uint16 / 4096。FBX 保留 Y 向上，以源坐标符号转换手性并同步翻转三角形顺序；V 转换为 1−V，单位设置为米。
- meshlet 描述为 20 字节，包括重映射与三角形偏移、最小顶点编号、顶点/三角形数量、索引宽度、起始三角形。重映射索引加最小顶点编号后，须与原三角形索引逐项相同；要求完整且无重复的三角形覆盖。

### 输出与边界

导出保留原 BINFBX，附带 `name.binfbx.fbx`。当前 FBX 输出 LOD0，多 LOD 均参与解析和校验。保留 UV 与材质槽，按三角形计算法线；不声称恢复原压缩法线/切线、贴图、着色器、骨骼或动画。骨骼/pose 头部、未知格式、非 1 全局缩放及校验不通过的模型明确跳过 FBX，并写入 manifest 原因。

### 验证证据

- `dotnet run --project validation -c Release -- binfbx`：14 个重点静态样本，涵盖四顶点平面、球体、梯子、碎块、弯曲平面、门框和多材质馅饼；另 3 个带骨骼样本按预期拒绝。
- `-- binfbx-scan`：从 base-generic、stream0-generic 的模型列表等距抽样 204 个，180 个通过；17 个骨骼/pose、1 个旧版本、3 个非 1 缩放和 3 个尚不符合已验证布局的模型被拒绝。此比例只代表本次样本，不代表全游戏覆盖率。
- 包围盒校验、索引范围校验、meshlet 双份拓扑逐项比较、全文件消费检查；损坏、截断、版本、超大计数、取消与 GUI/CLI 共用导出状态测试通过。
- 192 个去重后的生成 FBX 经 **ufbx v0.21.3 strict 模式**独立读取，顶点位置、三角形顺序、UV 和材质 ID 均与转换前数据一致。脚本：`validation/check_fbx_roundtrip.py`，报告：`validation/fbx-roundtrip-results.txt`。
- `validation/fbx-inspection.png` 为独立读回 FBX 的四个几何渲染图；没有用源 BINFBX 数据替代结果渲染。v1.3 已使用本机 Blender 5.2.2 后台 API 验证导入、编辑和导出；未测试 Maya。
- v1.3 的 FBX 导入使用内嵌 ufbx v0.21.3 辅助程序（静态 CRT，源码在 Native/FbxImport），首次使用解包到本地工具缓存。许可证随 EXE 内嵌。FBX 导出仍使用自有写入器。

## v1.3 回写验证补充

- 记录原全局/LOD/子网格/meshlet 包围球和 AABB 的偏移。平移及正向等比缩放修改这些包围体；量化位置使用变换后的 LOD 球，原 int16 编码保持不变；float32 位置流直接变换。UV 修改原 uint16 流并校验可表示范围。
- 不重新生成未知格式 15 法线/切线或 meshlet 锥体，因此不开放旋转、非等比缩放、变形及拓扑修改。原索引、meshlet 重映射、材质 ID 和其他未知数据保留；写后重新完整解析并逐 LOD 比较位置、UV 与拓扑。
- 修复原 FBX 写入器 `double/Number`、`int/Integer` 属性标记与 `FrontAxisSign=+1` 的右手系声明；本机 Blender 5.2.2 可实际读取。ufbx 导入统一到同一 Y-up、米制坐标。
- `validation/ModelImportChecks.cs` 验证平面、四 LOD 梯子、多材质三 LOD 托盘、四 LOD 双材质门框：未修改 FBX 字节一致，修改后所有 LOD 匹配；拒绝改名、改材质、变形、拓扑变化、负向/非等比缩放、UV 越界、损坏文件及无修改暂存。中英文预览窗实际渲染检查通过。
- `validation/blender_model_roundtrip.py` 使用 Blender 实际导入、整体缩放、导出 3 个样本，再经 C# 导入验证全部 LOD。`validation/check_model_mod_reference.py` 使用 RMT v0.2.9 独立解包本工具生成的模型 MOD，确认字节及 CRC 与预期一致；原游戏 TOC 未修改。
- 原有 UI、纹理/字体 MOD 与 BINFBX 导出回归通过。没有进行游戏内加载、碰撞或全量模型测试。不能把受限回写宣传为通用 FBX 转 BINFBX。

## 其他格式限制

未发现 PBX 样本，不能将 BINPX 视为已知等价格式。HDR/BC6H 等纹理保留完整 DDS，暂不转 PNG。完整纹理与导入限制见 README.md。
