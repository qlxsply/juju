# toto

`toto` 是一个面向 Windows 11 的轻量事项管理与提醒工具，使用 C# 和 .NET 10 WinForms。它保留轻量的 CSV/INI 文件存储方案，不依赖数据库服务或 ORM。

## 功能

- 托盘常驻、单实例运行、全局唤醒快捷键和 Windows 登录后自动启动。
- 进行中事项的新增、编辑、详情、完成和取消；主列表按计划时间和创建序号稳定排序。
- 快速输入：`事项内容[@计划时间[@提前提醒分钟数]]`，支持 `HHmm`、`ddHHmm`、`MMddHHmm`、`yyyyMMddHHmm`、`+HHmm` 等时间格式。
- 单次 Timer 调度事项提醒；提醒状态先原子写回 CSV，再显示提醒窗口；处理锁屏、解锁、休眠恢复和系统时间变化。
- `toto_ing.csv` 保存进行中事项，`toto_end.csv` 保存历史事项；历史列表支持分页，默认每页 200 条。
- 历史记录页面提供内容、备注筛选和分页；进行中事项主页面不提供搜索或功能按钮，统一通过快捷键操作。
- 启动时从 `holiday-cn` 下载缺失的法定节假日 JSON，并转换为本地缓存；工作日特殊日期维护，以及可选的上班/下班汇总提醒。
- 使用 `DataGridView` 原生网格线，避免旧版 ListView/GDI 自绘网格的 DPI 与滚动错位问题。

## 架构

```text
toto/
├── Toto.sln                         # Rider / Visual Studio 入口
├── src/Toto.App/
│   ├── Domain/                      # 事项、状态、查询条件等模型
│   ├── Data/                        # CSV、INI 文件读写和仓储
│   ├── Services/                    # 单实例、快捷键、调度、工作日和启动项
│   ├── UI/                          # WinForms 窗口和 DataGridView 界面
│   ├── Program.cs
│   └── TotoApplicationContext.cs    # 托盘与应用生命周期
└── templates/                       # CSV/INI 格式示例
```

应用数据位于 `%USERPROFILE%\.toto\`：

- `config.ini`：快捷键、默认提醒时间、开机启动和工作日提醒配置，使用 UTF-16 INI 格式。
- `toto_ing.csv`：进行中事项，使用 UTF-8 BOM CSV 格式。
- `toto_end.csv`：已完成和已取消事项，使用 UTF-8 BOM CSV 格式。
- `yyyy.json`：对应年份的本地节假日与调休缓存，例如 `2026.json`。
- `logs/`：按月滚动的运行日志。

首次启动会创建缺少的 `config.ini`、`toto_ing.csv` 和 `toto_end.csv`，已有文件会被直接继续使用。CSV 写入先生成临时文件，再替换原文件，降低写入中断导致数据损坏的风险。

首次启动和跨年首次使用时，如果本地 `{year}.json` 缺失，应用从 `https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json` 下载并校验后转换为本地格式。下载失败时应用会显示错误并停止启动，不会使用不准确的周末规则降级。

本地 `{year}.json` 仅保存节假日数据，不含上游元数据。例如将 2026 年 2 月 14 日设为调休工作日：

```json
[
  {
    "name": "春节",
    "date": "2026-02-14",
    "isOffDay": false
  }
]
```

`isOffDay: false` 表示工作日，`isOffDay: true` 表示休息日。未配置的日期按周一至周五工作、周六周日休息的基础规则判断。已有本地 JSON 不会自动联网覆盖，可在“工作日管理”窗口选择“下载/更新”手动刷新。

## 开发与运行

要求：Windows 11、.NET 10 SDK。项目没有第三方运行时依赖。

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
- 主页面只显示进行中事项列表，以上功能均通过快捷键操作；历史页面保留搜索和分页控件。
- 关闭主窗口或按 `Esc` 时仅隐藏到托盘；从托盘菜单选择“退出”才会结束进程。
- 重复启动时，新的进程立即退出，已经运行的 Toto 自动显示主窗口。
- 提醒窗口关闭不会改变事项状态；可在窗口中完成选中的事项。
