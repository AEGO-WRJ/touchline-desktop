# 看球 · Touchline Desktop

Windows 桌面足球卡片，支持五大联赛和欧冠。

## 运行

从 [Releases](https://github.com/AEGO-WRJ/touchline-desktop/releases/latest) 下载 `Touchline.exe`，双击运行。升级前先从托盘菜单退出旧版。需要 Windows 10/11 的 .NET Framework 4.x。

## 使用

- 按北京时间连续显示赛程，点击比赛查看比分、进球和统计。
- 可关注多支球队，调整关注顺序，并切换“只看关注球队”。首位球队队徽显示在顶部。
- 点击“收纳”缩成约 216×36 的小条；拖动可移动，点击“展开”或双击文字区恢复完整卡片。
- 小条上的“◐”可调透明度，范围为 0%～90%；完整卡片在“设置”中调节。两种模式分别记忆透明度，展开时恢复原大小。
- 右键小条可展开、调透明度、隐藏到托盘或退出。双击托盘图标恢复完整卡片。
- 队徽下载后缓存在本机，失败会重试并尝试备用地址；断网时可继续显示已有缓存。
- 程序、任务栏和托盘使用同一款足球图标。

关注配置自动保存在 `%LOCALAPPDATA%\TouchlineCard\favorites.json`，备份为 `favorites.json.bak`。窗口、收纳及透明度设置保存在同目录的 `settings.json`。这些个人配置不会上传到仓库。

## 从源码构建

在 Windows PowerShell 中运行 `./build.ps1`，生成 `dist/Touchline.exe`。无需安装其他开发依赖。

## 数据

数据及队徽地址来自 [ESPN](https://www.espn.com/soccer/scoreboard) 的公开接口，可能延迟、缺失或变更。比赛进行时每30秒检查，其他时间每2分钟检查；关闭程序或电脑休眠后不再刷新。本项目不隶属于 ESPN 或懂球帝。
