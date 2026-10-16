# Super Panel · 快捷启动面板

> 为个人 Win11 定制的「全局热键呼出 + 等大磁贴网格 + 极速搜索」桌面启动面板。C# / .NET 8 / WPF，托盘常驻，单 exe 免安装，免管理员。

![平台](https://img.shields.io/badge/Windows-10%2F11-blue) ![运行时](https://img.shields.io/badge/.NET-8.0--windows-purple) ![许可](https://img.shields.io/badge/License-MIT-green)

## 功能特性

- **全局热键呼出**：默认 `Alt+S`（设置内直接按组合键录制），失焦 / Esc / 再按热键 / ✕ 均可隐藏；面板置顶、Win11 原生圆角
- **磁贴网格**：程序(.exe/.lnk)、文件、文件夹、网址；拖入即添加；拖拽排序；右键改名/换图标/删除；每行列数 2–16 可调；末行自动居中；面板高度贴内容自适应
- **三层极速搜索**：磁贴（拼音首字母）→ 已装程序（开始菜单索引，拼音首字母）→ Everything 全盘（官方 SDK 进程内直连 70–200ms）；↑↓ 选行、回车打开；结果右键直接固定到面板（.lnk 自动解析程序本体）
- **设置面板**：明暗主题 + 强调色、列数、热键录制、开机自启、Everything 开关、数据目录迁移（可指向同步盘实现双机共用布局）；点面板外自动关闭
- **资源克制**：无插件、无动画、事件驱动零轮询；空载 ~61MB、CPU ≈ 0%

## 安装运行（最终用户）

1. 安装 .NET 8 桌面运行时（一次性，约 55MB）：
   - 官网下载：<https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0>（选「.NET 桌面运行时 8.0.x Windows x64」）
   - 或命令行：`winget install Microsoft.DotNet.DesktopRuntime.8`
2. 拷贝 `src\QuickPanel\bin\Release\net8.0-windows\win-x64\publish\` 整个文件夹到任意位置
3. 双击 `QuickPanel.exe`——托盘出现图标，热键呼出

**可选**：安装 [Everything](https://www.voidtools.com/zh-cn/)（含服务模式）后自动启用全盘文件搜索；不装则磁贴 + 已装程序两层照常用。

## 从源码构建

- 双击 `build.bat`（需 .NET 8 SDK：`winget install Microsoft.DotNet.SDK.8`）
- 或命令行：`dotnet publish src\QuickPanel\QuickPanel.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`
- 产物：单文件 `QuickPanel.exe`（约 600KB），`tools\` 内自带 Everything SDK/CLI，不碰系统安装

## 配置参考（exe 旁 `config\`）

| 文件/键 | 说明 |
|---|---|
| `config.json` | 主题 mode/accentColor、hotkey、gridColumns、search、window 位置、dataDir |
| `tiles.json` | 磁贴清单（JSON 明文，可备份/放同步盘） |
| `icons\` | 图标 PNG 缓存（建议留本机） |
| `logs\p0.log` | 热键注册、首帧耗时、定位自检、搜索分阶段耗时 |

## 性能指标（真机实测，2560×1440@150%）

| 指标 | 数值 |
|---|---|
| 热键 → 面板首帧 | 热态 12–28ms |
| 冷启动 → 面板可见 | 0.69s |
| 全盘搜索（Everything SDK） | 70–200ms |
| 空载内存（隐藏态） | ~61MB；用后有界 ~100MB（隐藏自动清结果 + 强制 GC） |
| 空载 CPU | ≈ 0% |

## 已知限制

- 全盘**拼音首字母**搜索需 Everything 1.5a 或 IbEverythingExt 插件；本地层不受影响
- Everything 为可选依赖，版本检测自动降级（SDK → es.exe → 禁用全盘层）
- 非管理员面板收管理员资源管理器的拖放会被 Windows 拒绝（UIPI），右键手动添加即可

## 目录结构

```
src\QuickPanel\    C# WPF 源码（.cs 逻辑 + .xaml 界面）
  assets\          图标与拼音首字母资源
  tools\           Everything SDK (Everything64.dll) 与 CLI (es.exe)
outputs\           立项方案、开发踩坑实录等文档
build.bat          双击构建
```

## License

[MIT](LICENSE)
