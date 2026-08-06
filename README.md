# zHWriter

离线、悬浮式 Windows 日记软件。它只管理一件事：每天唯一的一篇 Markdown 日记，并保持与 Obsidian 的 `./assets` 附件规则兼容。

## 首次使用

启动后选择日记库根目录。程序会创建 `Templates/Daily.md`（不存在时）和按日期生成的目录，例如：

```text
DiaryRoot/Journal/2026/Daily/08/2026-08-06.md
DiaryRoot/Journal/2026/Daily/08/assets/2026-08-06-142010-028.png
```

设置位于 `%LocalAppData%/zHWriter/settings.json`，不会写进日记库。

## 模板与附件

支持 `{{date}}`、`{{date:yyyy-MM-dd}}`、`{{year}}`、`{{month}}`、`{{day}}`、`{{weekday}}`、`{{fileName}}` 和 `<% tp.file.title %>`。未知变量会被原样保留。

Ctrl+V 粘贴截图时，图片将保存到当前日记同级的 `assets/`，正文插入 `![](assets/name.png)`。粘贴本地图片文件同样会复制到该目录，且不会覆盖现有附件。

## 操作

- 悬停或点击 `zH`：展开编辑器；移出后自动保存并折叠。
- `Ctrl+S`：保存；`Ctrl+O`：打开日历；`Esc`：折叠；`Ctrl+Shift+Q`：保存并退出。
- `Alt+左键`：拖动窗口；`Alt+右键`：调整窗口宽高；`Alt+滚轮`：以 10% 调整文字透明度。
- 右键菜单或托盘菜单可打开日历、日记库和退出。

程序采用单实例保护；再次启动时会提示已运行，不会创建第二个写入进程。

## 数据安全

新建日记使用排他创建：同一天被连续点击或并发请求时只会产生一个主文件。保存先写同目录 `.zhw.tmp`，再原子替换目标文件，并保留 `.zhw.bak` 备份；保存失败时正文会留在编辑器。

保存前如发现磁盘版本已由外部程序更新，程序会要求选择保留当前内容、重新加载磁盘版本或另存为副本。启动时发现较新的临时保存文件会询问是否恢复，并先备份正式日记。

## 构建与发布

当前工作区使用 `net6.0-windows`，因为开发机器仅安装了 .NET 6 SDK；安装 .NET 8 SDK 后，将项目目标框架改为 `net8.0-windows` 即可。

应用清单启用 Per-Monitor V2 DPI 感知、长路径与普通用户权限；仍建议在目标机器的 100%、125%、150%、175% 缩放及实际多显示器组合下做发布前人工验收。

编辑器每约 750ms 采样透明窗口下的合成屏幕颜色，在黑字和白字之间切换；采样不可用时会保持上一次可读的前景色。

```powershell
dotnet build zHWriter.sln
dotnet test zHWriter.sln
dotnet publish src/zHWriter.App/zHWriter.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
