# 快捷启动面板

> 一句话：为个人 Win11 定制「快捷键呼出 + 等大磁贴网格 + 极速搜索」的桌面启动面板；当前阶段 = 市场调研 + 自研方案，代码未开工。

## 状态

进行中 —— 方案已拍板（2026-10-15），P0 骨架 + P1 磁贴面板均已交付并运行（当晚）；热键终选 = Ctrl+Alt+Space。等用户实测 P1 后启动 P2 搜索层。

## 入口

- 方案正文：`outputs\快捷启动面板-调研与方案-2026-10-15-2121.md`
- 已拍板（2026-10-15 晚）：直接自研 / WPF(.NET 8) / 装 Everything 作全盘后端 / 拼音首字母搜索要做
- P2 搜索（2026-10-15 深夜交付）：输入即搜三层结果——磁贴（拼音首字母支持）→ 已装程序（开始菜单索引，拼音首字母支持）→ 全盘文件/文件夹（Everything es.exe 通道，约 0.6s 流式上屏）。↑↓ 选行、回车/单击打开、Esc 分级退出。es.exe 内置于 `tools\`，不碰用户 Everything 安装。
- P1 数据与用法（2026-10-15 深夜交付）：
  - 布局数据：exe 旁 `config\tiles.json`（磁贴清单，JSON 可备份/可放同步盘）；图标缓存 `config\icons\`（本机）
  - 添加：从资源管理器把 exe/.lnk/文件/文件夹拖进面板即可；或右键空白处 → 添加文件/文件夹/网址
  - 编辑：右键空白处 → 进入编辑模式（磁贴边框变强调色）→ 右键磁贴改 名/图标/删除；拖拽磁贴排序；再右键空白处退出
  - 启动：普通模式左键点击磁贴；面板自动隐藏
  - 过滤：顶部搜索框按名称过滤磁贴（全盘搜索在 P2 接入）
  - 磁贴变暗 = 目标路径不存在
- P0 测试版 exe：`src\QuickPanel\bin\Release\net8.0-windows\win-x64\publish\QuickPanel.exe`（单文件、框架依赖；托盘左键 = 呼出面板）
- 配置：exe 旁 `config\config.json`（明暗 mode：dark/light/auto、accentColor 强调色、hotkey 热键、gridColumns 列数）
- 日志：exe 旁 `logs\p0.log`（热键注册结果、面板弹出耗时）
- 开发：`dotnet build src\QuickPanel -c Release`；发布：`dotnet publish src\QuickPanel -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`

## 目录

| 目录 | 内容 |
|---|---|
| `outputs\` | 调研与方案文档 |
| `src\` | C# WPF 源码（`src\QuickPanel\`，.NET 8） |

## 备注

- 热键终选 = Ctrl+Alt+Space（用户原选 Ctrl+Win+Space 实测被系统占用；真机扫描 18 组合，占用/空闲清单见 ACTIVE.md T7）
- 实测（2026-10-15 真机）：空载内存 私有 49.2MB / 工作集 100MB；热键→面板首帧 = 15ms（目标 ≤100ms）；验收口径 = 私有内存 ≤60MB + 首帧 ≤100ms，P3 复核
- Listary 无公开 SDK，不可作集成后端（方案 §2.2 有出处）；性能验收指标见方案 §4.6，交付时须附实测数字。
- 依赖：Hardcodet.NotifyIcon.Wpf 2.0.1（托盘图标）；.NET 8 桌面运行时（本机已有 8.0.10）。
