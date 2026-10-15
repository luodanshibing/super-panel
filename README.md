# 快捷启动面板

> 一句话：为个人 Win11 定制「快捷键呼出 + 等大磁贴网格 + 极速搜索」的桌面启动面板；当前阶段 = 市场调研 + 自研方案，代码未开工。

## 状态

进行中 —— 方案已拍板（2026-10-15），P0 骨架已交付并运行（当晚）；等用户实测热键与观感、终选热键，然后启动 P1。

## 入口

- 方案正文：`outputs\快捷启动面板-调研与方案-2026-10-15-2121.md`
- 已拍板（2026-10-15 晚）：直接自研 / WPF(.NET 8) / 装 Everything 作全盘后端 / 拼音首字母搜索要做
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

- 热键：用户原选 Ctrl+Win+Space 实测被系统占用（RegisterHotKey 失败）；真机扫描 18 组合，空闲清单见 ACTIVE.md T7。测试版临时 = Ctrl+Alt+Space，终选待定。
- 内存实测（P0，空载）：私有 49.2MB / 工作集 100MB；验收口径拟定为「私有 ≤60MB」，P3 复核。
- Listary 无公开 SDK，不可作集成后端（方案 §2.2 有出处）；性能验收指标见方案 §4.6，交付时须附实测数字。
- 依赖：Hardcodet.NotifyIcon.Wpf 2.0.1（托盘图标）；.NET 8 桌面运行时（本机已有 8.0.10）。
