# Toto（日程提醒工具）

Toto 的业务程序集是平台无关的 `Juju.Tools.Toto`（`net10.0`）。WPF 主窗、事项编辑/详情、历史和提醒窗属于
`Juju.App.Presentation.Toto`；App 通过 `ITotoApplicationService` 使用 Toto，不直接访问 Repository。

## 包结构

- `Domain`：事项状态、查询领域模型、节假日值对象。
- `Application`：`ITotoApplicationService` 及 App 可见的事项/历史/日历快照与请求。
- `Application.Ports`：事项、设置、日历 Repository 和提醒运行时协作端口。
- `Application.Reminders`：唯一 `TotoReminderRuntime` / `ReminderScheduler`，不依赖 WPF、WinForms 或 Windows 系统事件。
- `Infrastructure.Items`、`.Settings`、`.Calendar`、`.Storage`：现行 CSV/JSON 文件、月归档、节假日缓存及注入的数据路径。

Windows 会话锁定/解锁、电源恢复和系统时间变化由 `Juju.Platform.Windows.Lifecycle.WindowsSystemLifecycleEvents`
转换成 Core 的 `ISystemLifecycleEvents` 信号。Toto runtime 接收平台无关信号，提醒快照交给 App；WPF Dispatcher
和提醒窗口都留在 `Juju.App`。

## 数据格式

默认目录由 App Composition 注入，当前为 `<Juju 数据根目录>/tools/toto/`：

| 文件 | 内容 |
| --- | --- |
| `config.json` | Toto 设置、计划弹窗标记和历史归档进度 |
| `toto_ing.csv` | 当前活动事项，固定 9 列：事项 ID、内容、计划时间、提醒时间、创建时间、创建序号、提醒状态、响铃时间、备注 |
| `toto_end.csv` | 当前历史事项，固定 9 列：事项 ID、内容、计划时间、提醒时间、创建时间、创建序号、结束状态、结束时间、备注 |
| `toto_end_yyyyMM.csv` | 按结束月份归档的历史事项，列格式与 `toto_end.csv` 一致 |
| `yyyy.json` | 指定年份的节假日/调休缓存 |
| `logs/` | Toto 运行日志；通过共享 `ILogger<T>` 记录 |

每次启动时将上一个完整月份及更早历史归档，并在 JSON 配置中保存归档进度。历史查询按筛选月份读取归档和当前 CSV，
合并去重后按结束时间倒序分页。数据路径、现行 JSON/CSV 字段和业务规则由现有 Repository 测试锁定。

## 设置和提醒

- Launcher 单键默认为 `T`；Toto 全局直达键默认为 `None`。
- 窗口内快捷键默认为新增 `Alt+A`、历史 `Alt+Q`、刷新 `Alt+R`、详情 `Alt+D`、编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C`。
- 新增事项支持 `内容 @时间` 和 `内容 @时间@分钟数`；时间支持 `HHmm`、`ddHHmm`、`MMddHHmm`、`yyyyMMddHHmm`、连续 `+` 加天或连续 `W` 加周后的星期几。
- 必需后台模块由 `IToolManager` 在 App 启动时启动；隐藏或关闭 Toto 窗口不停止提醒。
- 日历未就绪时保留五分钟重试；锁屏暂停提醒，解锁、电源恢复及系统时间变化后重新调度。

## 验证

```powershell
dotnet test tests\Juju.Tools.Toto.Tests\Juju.Tools.Toto.Tests.csproj --no-restore
dotnet test tests\Juju.Architecture.Tests\Juju.Architecture.Tests.csproj --no-restore
```
