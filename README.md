# zHWriter

离线、悬浮式 Windows 周期笔记软件。基于 .NET 6 + WPF，主窗口是一个完全透明的悬浮层，只显示文字，贴在桌面上随时记录；笔记按周期（每日 / 每周 / 每月）组织为 Markdown，兼容 Obsidian 附件规则。

## 功能

- **全透明悬浮编辑器**：窗口完全透明、无边框，文字贴近窗口边缘；悬停或点击 `zH` 展开，移出后自动保存并折叠。
- **周期笔记**：日记 / 周记 / 月记三种周期，周记使用 ISO 8601 周号，跨年周自动归属正确年份。
- **LifeOS 风格周期日历**：三个居中标签页（日记 / 周记 / 月记），已有笔记的日期显示小圆点；双击标签页打开今天 / 本周 / 本月，单击切换周期，避免误触。
- **模板系统**：`Templates/Daily.md`、`Weekly.md`、`Monthly.md`，仅在缺失时自动创建，绝不覆盖已有模板；支持日期与周期变量。
- **图片附件**：`Ctrl+V` 粘贴截图或拖入本地图片，自动复制到当前笔记同级 `assets/` 目录并插入 Markdown 引用，不覆盖现有附件。
- **数据安全**：排他创建、原子保存（先写 `.zhw.tmp` 再替换目标文件）、`.zhw.bak` 备份、外部修改冲突检测、崩溃恢复。
- **快捷操作**：`Alt+左键` 拖动、`Alt+右键` 缩放、`Alt+滚轮` 调透明度，全部操作集中在右键菜单和托盘菜单。
- **单实例运行**：重复启动只提示已运行，不会产生第二个写入进程。

## 运行环境

- Windows 10 / 11
- [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)（双击运行发布版需要；或按「发布」一节生成自包含版本）
- 从源码构建还需要 .NET 6 SDK

## 首次使用

启动后选择日记库根目录。程序会自动创建三种默认模板，并按周期生成笔记：

```text
<DiaryRoot>/
├── Templates/
│   ├── Daily.md
│   ├── Weekly.md
│   └── Monthly.md
├── Daily/
│   └── 08/
│       ├── 2026-08-07.md
│       └── assets/
│           └── 2026-08-07-142010-028.png
├── Weekly/
│   └── 2026-32W.md
└── Monthly/
    └── 2026-08.md
```

设置位于 `%LocalAppData%/zHWriter/settings.json`，不会写进日记库。

## 模板变量

| 变量 | 说明 |
|---|---|
| `{{year}}` / `{{month}}` / `{{day}}` | 年 / 月 / 日（月、日补零） |
| `{{weekday}}` | 中文星期 |
| `{{date}}` | `yyyy-MM-dd` |
| `{{date:格式}}` | 自定义日期格式，如 `{{date:yyyy年MM月dd日}}` |
| `{{fileName}}` | 当前笔记文件名（不含扩展名） |
| `{{week}}` / `{{weekYear}}` | ISO 周号 / 周所属年份 |
| `{{weekStart}}` / `{{weekEnd}}` | 本周周一 / 周日日期 |
| `<% tp.file.title %>` | Obsidian 兼容变量，等价于 `{{fileName}}` |

未知变量会原样保留。

## 操作与快捷键

| 操作 | 效果 |
|---|---|
| 悬停 / 点击 `zH` | 展开编辑器；移出后自动保存并折叠 |
| `Alt + 左键` | 拖动窗口位置 |
| `Alt + 右键` | 拖动调整窗口大小（右下角显示尺寸） |
| `Alt + 滚轮` | 以 10% 步进调整文字透明度（右下角显示百分比） |
| `Ctrl + S` | 保存 |
| `Ctrl + O` | 打开周期日历 |
| `Esc` | 折叠 |
| `Ctrl + Shift + Q` | 保存并退出 |
| 右键菜单 | 打开日历、打开今日 / 本周 / 本月、保存、打开文件夹、设置、退出 |

## 数据安全

- 新建笔记使用排他创建：同一周期被并发打开时只会生成一个主文件。
- 保存先写同目录 `.zhw.tmp`，原子替换目标文件并保留 `.zhw.bak` 备份；失败时正文仍保留在编辑器。
- 保存前发现磁盘版本已被外部修改，会要求选择：保留我的内容 / 重新加载磁盘版 / 另存为副本。
- 启动时发现较新的临时保存文件会询问是否恢复，恢复前先备份正式笔记。

## 项目结构

```text
src/
├── zHWriter.Core/            # 领域模型、服务接口、路径规则、模板变量展开
├── zHWriter.Infrastructure/  # 文件 IO、设置持久化、日历索引、附件处理
└── zHWriter.App/             # WPF 界面：悬浮编辑器、周期日历、设置窗口、托盘
tests/
├── zHWriter.Core.Tests/          # 路径与模板单元测试
└── zHWriter.IntegrationTests/    # 文件服务集成测试（原子保存 / 冲突 / 并发 / 恢复）
```

分层约定：路径规则属于 Core，文件 IO 属于 Infrastructure，UI 状态属于 ViewModel。

## 构建与测试

需要 .NET 6 SDK：

```powershell
dotnet build zHWriter.sln
dotnet test zHWriter.sln
```

## 发布

生成可在已安装 .NET 6 Desktop Runtime 的机器上双击运行的版本：

```powershell
dotnet publish src/zHWriter.App/zHWriter.App.csproj -c Release -r win-x64 -o publish/win-x64 --self-contained false
```

若目标机器没有安装 .NET 6，改用自包含版本（无需预装运行时，体积更大）：

```powershell
dotnet publish src/zHWriter.App/zHWriter.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## 许可证

[MIT](LICENSE)
