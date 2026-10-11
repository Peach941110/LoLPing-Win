# LoLPing for Windows

英雄联盟桌面信号轮盘的 **Windows 版本**，移植自原项目 [AllenTHT/LoLPing-Mac](https://github.com/AllenTHT/LoLPing-Mac)。

按住自定义组合键呼出屏幕中央的信号轮盘，鼠标滑到某个信号上松手，即向全队发送该信号（轮盘飞入动画 + 对应信号音效），效果与游戏内信号一致。无需管理员权限，无需任何辅助功能授权。

## 与 Mac 版的差异

| | Mac 版 | Windows 版（本项目） |
|---|---|---|
| 技术栈 | Swift / AppKit | C# / WPF (.NET 8) |
| 全局输入 | CGEventTap（需辅助功能授权） | WH_KEYBOARD_LL / WH_MOUSE_LL 低级钩子（免授权） |
| 信号矢量 / 音效 | `ping-vectors.json` + 9 个 MP3 | 完全复用同一套资源 |
| 手势逻辑 | 按住组合键 → 轮盘 → 移动选择 → 松开发送 | 1:1 一致 |

新增：可自定义快捷键（带实时冲突检测）、呼出延时可调（0~300ms）、轮盘缩放、音量、托盘常驻。

## 使用

1. 下载解压，双击 `LoLPing.exe`（首次运行 Windows 可能提示 SmartScreen，选择"仍要运行"）
2. 托盘右键 → 设置，选择你喜欢的组合键（例如 `Ctrl+Alt` + 触发键 `V`）
3. 游戏内或桌面上一按组合键 → 轮盘出现 → 滑动选择 → 松手发送

## 构建

```bash
dotnet build -c Release
# 产物在 bin/Release/net8.0-windows/LoLPing.exe（需 .NET 8 桌面运行时）
```

## 下载

见 [Releases](../../releases)，提供两个版本，解压后双击 `LoLPing.exe` 即可使用：

| 版本 | 大小 | 要求 |
|---|---|---|
| [LoLPing-1.0.0-win-x64.zip](https://github.com/Peach941110/LoLPing-Win/releases/download/v1.0.0/LoLPing-1.0.0-win-x64.zip) | 58 MB | **无需任何运行时**，自包含绿色版，下载即用 |
| [LoLPing-1.0.0-win-x64-dotnet8.zip](https://github.com/Peach941110/LoLPing-Win/releases/download/v1.0.0/LoLPing-1.0.0-win-x64-dotnet8.zip) | 0.4 MB | 需先安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) |

首次运行 Windows 可能弹出 SmartScreen 提示，选择"仍要运行"即可。

## 免责声明

本项目为非官方粉丝工具。轮盘矢量与信号音效资源源自《英雄联盟》，版权归 [Riot Games](https://www.riotgames.com/) 所有；本仓库仅作学习研究用途，请勿用于商业用途。
