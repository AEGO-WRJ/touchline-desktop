# 看球 · Touchline

Windows / macOS 桌面足球卡片：五大联赛、欧冠、足总杯、英格兰联赛杯、德国杯和国王杯。

| 平台 | 下载 | 系统 |
| --- | --- | --- |
| Windows | [Windows 版](https://github.com/AEGO-WRJ/touchline-desktop/releases/tag/v1.3.3-windows.1) → `Touchline.exe` | Windows 10/11 |
| macOS | [Mac 版](https://github.com/AEGO-WRJ/touchline-desktop/releases/tag/v1.3.3-macos.1) → `Touchline-macOS-universal.zip` | macOS 13+，Apple Silicon / Intel |

Windows 双击运行；Mac 解压后放入“应用程序”。两个版本默认驻留托盘或菜单栏，点击图标显示卡片。Windows 1.2.0 及更早版本需手动替换一次，此后支持应用内更新。[Mac 首次安装与详细说明](macos/README.md)。

## 使用

- 赛程按北京时间排序，可选日期、切换周、回到今天；点击比赛展开统计和关键事件。
- 可关注多支球队、排序、只看关注；首位球队队徽也会显示在托盘或菜单栏。
- “显示杯赛”默认开启，可单独筛选杯赛；关闭后隐藏并停止请求国内杯赛，欧冠不受影响。
- 可置顶、收纳为小条，分别设置卡片和小条透明度。完整卡片显示时才更新比分，收纳或隐藏后取消请求，展开立即同步。
- `×` 隐藏卡片；完全退出请使用托盘或菜单栏的“退出看球”。登录自启默认关闭，启动时仅显示图标可在设置中关闭。
- 托盘菜单和设置提供“检查更新”。默认每天检查、自动下载，并在退出时安装；也可手动安装并重启，或关闭自动更新。

更新源按平台分开，安装前验证签名和安装包；个人设置与缓存保留。Windows 卡片内可用 `Ctrl+1` 展开、`Ctrl+R` 刷新、`Ctrl+W` 隐藏、`Ctrl+,` 打开设置、`Ctrl+Q` 退出。

数据来自 [ESPN](https://www.espn.com/soccer/scoreboard)：完整卡片显示时，赛中每 30 秒、其余每 2 分钟检查，可能延迟或缺失。

## 本地数据

Windows：`%LOCALAPPDATA%\TouchlineCard`。Mac 配置：`~/Library/Application Support/TouchlineCard`，缓存：`~/Library/Caches/TouchlineCard`。两个平台不自动同步设置。

开启自启后请保持程序位置固定；Windows 移动后重新运行一次可更新自启路径。

## 构建与发布

- Windows：PowerShell 执行 `./build.ps1`，生成 `dist/Touchline.exe`，使用系统自带 .NET Framework。
- Mac：执行 `./build-macos.sh`，需要 Xcode Command Line Tools；签名与发布步骤见 [Mac 说明](macos/README.md)。

`src/` 为 Windows 源码，`macos/Sources/` 为 Mac 源码，`assets/` 为共享资源。

Windows 更新发布：修改 `src/Version.cs` 并构建，先上传原始 EXE 到 GitHub Release，再运行 `release-windows-update.ps1 -Version 版本 -Tag 标签 -Executable EXE路径 -KeyPath 私钥路径`，最后推送生成的 `windows/update.json`。私钥必须保存在仓库之外；应用仅内置公钥。派生项目需配置自己的密钥和更新地址。
