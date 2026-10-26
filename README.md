# 看球 · Touchline

Windows 10/11 桌面足球卡片，支持五大联赛和欧冠。

[下载最新版](https://github.com/AEGO-WRJ/touchline-desktop/releases/latest)，双击 `Touchline.exe` 运行；升级前先退出旧版。

- 按北京时间连续显示赛程、比分和比赛数据。
- 支持关注球队、排序及只看关注，首位球队显示队徽。
- 可拖动、置顶、收纳为小条；卡片与小条分别记忆透明度。
- “设置 → 开机自启”默认关闭，开启后登录 Windows 自动运行。

配置和缓存保存在 `%LOCALAPPDATA%\TouchlineCard`。开启自启后请保持程序位置固定，移动后重新运行一次即可更新路径。

## 构建

Windows PowerShell 执行 `./build.ps1`，生成 `dist/Touchline.exe`，无需额外开发依赖。

`src/` 存放 C#/WPF 源码，`assets/` 存放图标和球队目录。

数据来自 [ESPN](https://www.espn.com/soccer/scoreboard)，赛中每 30 秒、其余每 2 分钟检查，可能延迟或缺失。
