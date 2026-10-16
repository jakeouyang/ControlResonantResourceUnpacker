# v1.4.0 — UI bundle export / UI 容器资源导出

中文：导出 `ui.ui` 时自动提取内嵌 SVG、HTML 和 JavaScript，写入原文件旁的 `ui.ui.contents` 目录并保留内部路径。原始 `.ui` 保留，外部引用跳过，清单记录数量或失败原因。修复无法获取菜单图标 `symbols/` 目录的问题；未知布局不会强行解析。GUI 与命令行均生效。本次真实样本提取并校验 728 SVG、13 HTML 和 5 JavaScript；数量随游戏版本而异。不支持 UI 容器回导。

English: Exporting `ui.ui` now automatically extracts embedded SVG, HTML and JavaScript files to an adjacent `ui.ui.contents` folder, preserving their internal paths. The original container is retained; external references are skipped and counts/failures are recorded in the manifest. This makes the nested menu-icon `symbols/` folder available in both GUI and CLI exports. Unsupported layouts are reported without guessing. Verified against a real game bundle containing 728 SVGs, 13 HTML files and 5 JavaScript files; counts may vary by game build. UI bundle re-import is not supported.
