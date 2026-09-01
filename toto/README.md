# toto

`toto` 是一个面向 Windows 11 的轻量事项管理与提醒工具。当前实现使用 C#、.NET 10 WinForms 和 SQLite。

## 功能

- 托盘常驻、单实例运行、全局唤醒快捷键和 Windows 登录后自动启动。
- 进行中事项的新增、编辑、详情、完成和取消；主列表按计划时间和创建序号稳定排序。
- 快速输入：`事项内容[@计划时间[@提前提醒分钟数]]`，支持 `HHmm`、`ddHHmm`、`MMddHHmm`、`yyyyMMddHHmm`、`+HHmm` 等时间格式。
- 单次 Timer 调度事项提醒；提醒状态先写入数据库，再显示提醒窗口；处理锁屏、解锁、休眠恢复和系统时间变化。
- SQLite 保存进行中与历史事项；历史列表支持数据库分页，默认每页 200 条。
- 内容、备注和时间范围筛选；所有用户条件使用参数化 SQL。
- 工作日特殊日期维护，以及可选的上班/下班汇总提醒。
- 使用 `DataGridView` 原生网格线，避免旧版 ListView/GDI 自绘网格的 DPI 与滚动错位问题。

## 架构

```text
toto/
├── Toto.sln                         # Rider / Visual Studio 入口
├── src/Toto.App/
│   ├── Domain/                      # 事项、状态、查询条件等模型
│   ├── Data/                        # SQLite schema、仓储和旧数据迁移
│   ├── Services/                    # 单实例、快捷键、调度、工作日和启动项
│   ├── UI/                          # WinForms 窗口和 DataGridView 界面
│   ├── Program.cs
│   └── TotoApplicationContext.cs    # 托盘与应用生命周期
└── templates/                       # CSV/INI 格式示例
```

应用数据位于 `%USERPROFILE%\.toto\`：

- `toto.db`：主 SQLite 数据库。
- `legacy_backup/`：首次迁移旧 INI/CSV 前创建的备份。
- `logs/`：按月滚动的运行日志。

首次启动会备份并迁移同目录下已有的 `config.ini`、`toto_ing.csv` 和 `toto_end.csv`。迁移成功后保留原文件，不会将其删除或覆盖。

## 开发与运行

要求：Windows 11、.NET 10 SDK。首次构建需要访问 NuGet 以还原 `Microsoft.Data.Sqlite`。

```powershell
dotnet build .\Toto.sln
dotnet run --project .\src\Toto.App\Toto.App.csproj
```

在 Rider 或 Visual Studio 中打开 `toto/Toto.sln`。

发布 framework-dependent x64 版本：

```powershell
dotnet publish .\src\Toto.App\Toto.App.csproj -c Release -r win-x64 --self-contained false
```

## 使用

- 默认全局唤醒：`Ctrl+Alt+Space`。
- 默认应用内快捷键：新增 `Alt+A`、历史 `Alt+Q`、设置 `Alt+S`、刷新 `Alt+R`、详情 `Alt+D`、编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C`。
- 关闭主窗口或按 `Esc` 时仅隐藏到托盘；从托盘菜单选择“退出”才会结束进程。
- 提醒窗口关闭不会改变事项状态；可在窗口中完成选中的事项。
