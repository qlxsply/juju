# 阶段 0：现有行为基线

建立时间：2026-10-30。本文记录重构前可观察行为，用于阶段 1 之后逐项回归。当前记录依据现行实现与自动化测试；手动 UI 场景列在文末，需在 Windows 桌面运行中逐项观察并填写结果。

## 自动化基线

执行命令：

```powershell
dotnet test Juju.sln
```

重构前结果：

| 测试项目 | 结果 | 主要锁定语义 |
| --- | ---: | --- |
| `Juju.Tools.Json.Tests` | 38 passed | 创建/排序/重命名/回收删除、版本冲突、外部文件协调、导入、文件监视、JSON 压缩/差异、快捷键、数据目录验证/复制/恢复 |
| `Juju.Tools.Toto.Tests` | 49 passed | 事项生命周期与并发、当前 CSV 与 JSON 配置、历史筛选/分页/月归档、提醒调度/每日复盘、快捷键解析、集成资源 |
| 合计 | 87 passed | 0 failed, 0 skipped |

这些既有 Repository/服务测试作为重构业务语义基线保留；不能以新测试替代或删减。测试细节见 `tests/Juju.Tools.Json.Tests/JsonDocumentServiceTests.cs`、`tests/Juju.Tools.Toto.Tests/ItemRepositoryTests.cs` 和 `tests/Juju.Tools.Toto.Tests/QuickItemParserTests.cs`。

阶段 0 完成后的复验：`dotnet test Juju.sln` 为 90 passed（JSON 38、Toto 49、项目引用图及测试项目隔离 3）；`dotnet build Juju.sln -c Release --no-restore -v:q` 成功，0 warnings / 0 errors。前述 87 项是新增架构测试之前的业务测试基线。

阶段 1 完成后的复验：`dotnet test Juju.sln --no-restore -m:1` 为 96 passed（JSON 38、Toto 49、架构 5、路由/目录/生命周期 4）；`dotnet build Juju.sln -c Release --no-restore -m:1 -v:q` 成功，0 warnings / 0 errors。阶段 1 后的手动 UI 对照仍按下方清单执行。

阶段 2 完成后的复验：`dotnet test Juju.sln --no-restore -m:1` 为 100 passed（JSON 38、Toto 52、架构 6、路由/目录/生命周期 4）；`dotnet build Juju.sln -c Release --no-restore -m:1 -v:q` 成功，0 warnings / 0 errors。手动 UI 对照仍需按下方清单执行。

阶段 3 完成后的复验：`dotnet test Juju.sln --no-restore -m:1` 为 104 passed（JSON 38、Toto 54、架构 8、路由/目录/生命周期 4）；`dotnet build Juju.sln -c Release --no-restore -m:1 -v:q` 成功，0 warnings / 0 errors。手动 UI 对照仍需按下方清单执行。

阶段 4 完成后的复验：`dotnet test Juju.sln --no-restore -m:1` 为 105 passed（JSON 38、Toto 55、架构 8、路由/目录/生命周期 4）；`dotnet build Juju.sln -c Release --no-restore -m:1 -v:q` 成功，0 warnings / 0 errors。手动 UI 对照仍需按下方清单执行。

阶段 5 自动化与发布复验：`dotnet test Juju.sln` 为 106 passed（JSON 38、Toto 56、架构 8、App 4）；`dotnet build src/Juju.App/Juju.App.csproj -c Release` 成功，0 warnings / 0 errors；Inno Setup 成功生成 `artifacts/installer/juju_v0.1.0_setup_win64.exe`（56,690,730 bytes）。手动桌面 UI 回归仍待执行。

阶段 5 自动化与发布复验：`dotnet test Juju.sln` 为 106 passed（JSON 38、Toto 56、架构 8、App 路由/目录/生命周期 4）；`dotnet build src/Juju.App/Juju.App.csproj -c Release` 成功；`artifacts/win-x64/` 自包含发布和 `artifacts/installer/juju_v0.1.0_setup_win64.exe` 安装包均已生成。手动桌面 UI 回归尚待执行。

## 当前项目引用基线

`Juju.Architecture.Tests` 读取 `src/**/*.csproj`，精确锁定以下产品项目 DAG；测试项目自身不参与产品 DAG，也不引用产品项目：

```text
Juju.App ───────────────> Juju.Core
   ├───────────────────> Juju.Tools.Json ──> Juju.Core
   ├───────────────────> Juju.Tools.Toto ──> Juju.Core
   └───────────────────> Juju.Platform.Windows ──> Juju.Core
```

当前各产品项目直接引用：

| 项目 | 直接引用 |
| --- | --- |
| `Juju.Core` | 无 |
| `Juju.Tools.Json` | `Juju.Core` |
| `Juju.Tools.Toto` | `Juju.Core` |
| `Juju.Platform.Windows` | `Juju.Core` |
| `Juju.App` | `Juju.Core`、`Juju.Tools.Json`、`Juju.Tools.Toto`、`Juju.Platform.Windows` |

这只是阶段 0 对**当前** `.csproj` 的保护基线，不代表目标 package DAG 已经在代码中实现。后续阶段若有意调整产品项目引用，须先更新重构方案和基线断言，再执行调整。

## 当前行为记录

### 启动器、单实例和工具入口

- 首次启动创建系统级单实例 Mutex；后续进程通过命名管道发送激活命令，已有实例唤起 Launcher 后，新进程退出。
- 启动顺序为单实例检查、数据根/设置加载、DI 构建、Toto 初始化、Launcher 显示和托盘创建。默认设置启动时显示 Launcher；`StartMinimized` 可令它启动后隐藏。
- Launcher 主快捷键切换入口；失焦自动隐藏受设置控制；入口显示后的超时、`Escape`、方向键切换焦点及无修饰键单键打开均由 Launcher 处理。
- 当前 Launcher 固定显示 JSON、Toto、EchoHttp、Kairos 四项。JSON/Toto 卡片打开工具；EchoHttp/Kairos 显示“正在开发中”提示。JSON/Toto 全局直达热键直接打开对应窗口；占位工具热键走占位提示。
- JSON 窗口按需创建并复用，关闭后销毁；Toto 主窗口可隐藏后再打开，只有应用退出时显式允许关闭。设置窗口由 Launcher 打开，工具窗口打开时收起 Launcher/设置覆盖层。
- Launcher、单键和全局热键目前没有统一经过 `IToolManager`，其实际打开路径为 `IWindowManager.ShowTool`。

来源：`src/Juju.App/App.xaml.cs`、`src/Juju.App/Views/LauncherWindow.xaml.cs`、`src/Juju.App/Bootstrap/DesktopWindowManager.cs`。

### Toto 初始化、提醒和恢复

- App 启动时先创建唯一 `TotoToolHost`，再显示 Launcher；提醒调度不依赖 Toto 主窗口开关。
- Toto 初始化读取本年及未来 7 天覆盖到的日历年份；日历未就绪时运行时继续启动，失败后每 5 分钟重试。用户尝试打开 Toto 时若仍未就绪，会看到含错误信息的模态提示，窗口不打开。
- 运行时按最近待提醒事项、每日盘点时间和 6 小时兜底检查点安排一次性计时器；到期事项先持久化为已提醒，再向 UI 发布提醒。每日开始/结束盘点各自按日期去重；可配置只在工作日触发。
- 锁屏暂停计时器；解锁时重新检查日历并立即恢复调度。电源恢复、系统时间变化也重新检查并调度。
- 启动时尝试将上一个完整月份及更早的历史记录归档，并记录归档进度；归档失败记录日志并留待下次启动重试。
- 应用关闭时释放 Windows 系统事件订阅、重试计时器和唯一提醒调度器。关闭/隐藏 Toto 窗口本身不停止提醒。

来源：`src/Juju.Tools.Toto/TotoToolHost.cs`、`src/Juju.Tools.Toto/Services/ReminderScheduler.cs`、`src/Juju.App/App.xaml.cs`、`src/Juju.App/Bootstrap/DesktopWindowManager.cs`。

### 数据与设置语义

- JSON 与 Toto 使用现有 Juju 工具数据路径；JSON 当前格式、Toto 当前 JSON 配置与现行 CSV 列格式不变。
- JSON 自动化测试锁定文档 ID/次序、回收删除、外部修订冲突、文件协调/监视、导入不覆盖、目录切换复制和启动恢复等行为。
- Toto 自动化测试锁定 CSV 状态/时间/备注字段、历史筛选和分页、跨月归档与查询、提醒状态持久化、每日盘点去重、设置 JSON 及快捷键解析行为。

来源：两组现有测试项目，详见上方自动化基线表。

## 手动 UI 验收清单

每次手动验收记录 Windows 版本、构建版本、数据目录和结果。重构前观察结果填写在“基线观察”栏；重构后使用相同步骤对照。当前还未执行桌面手动验收。

| ID | 场景与步骤 | 预期基线 | 基线观察 / 结果 |
| --- | --- | --- | --- |
| UI-01 | 干净启动 Juju，不设置最小化启动 | Launcher 显示；托盘出现；不打开 JSON/Toto 窗口 | 待手动观察 |
| UI-02 | 重新启动第二个 Juju 实例 | 第二实例退出；已有 Launcher 被唤起，不产生第二套托盘/窗口 | 待手动观察 |
| UI-03 | 使用 Launcher 主快捷键、失焦、超时和 Escape | 按设置显示/隐藏；Escape 隐藏；超时与自动隐藏遵循设置 | 待手动观察 |
| UI-04 | 使用方向键和配置的单键打开四个卡片 | 焦点按卡片顺序循环；JSON/Toto 打开工具；EchoHttp/Kairos 显示开发中提示 | 待手动观察 |
| UI-05 | 从 Launcher 和设置分别配置/触发 JSON、Toto 全局直达热键 | 仅注册有效且不冲突组合；设置捕获期间热键暂时释放；冲突或注册失败保留旧配置/注册 | 待手动观察 |
| UI-06 | 连续打开、隐藏、重开 JSON 和 Toto | JSON 窗口复用直至关闭；Toto 隐藏后重开同一窗口；工具窗口打开时 Launcher/设置覆盖层收起 | 待手动观察 |
| UI-07 | 让日历可用后启动 Toto；再用无效/缺失日历环境尝试打开 | 日历可用时正常打开；不可用时显示错误提示、窗口不打开，后台按 5 分钟间隔重试 | 待手动观察 |
| UI-08 | 创建即将到期事项；关闭/隐藏 Toto 主窗口并等待到期 | 只出现一次提醒；事项标记为已提醒；主窗口关闭不停止后台调度 | 待手动观察 |
| UI-09 | 配置每日开始/结束盘点时间为当前时间后触发，并重复恢复调度 | 相应盘点提示各出现一次；同一天重复恢复不重复提示；周末过滤遵循设置 | 待手动观察 |
| UI-10 | 在到期事项等待期间锁屏、解锁；另测睡眠恢复和系统时间变更 | 锁屏暂停；恢复/解锁/改时后立即重新检查并按现有数据状态继续调度 | 待手动观察 |
| UI-11 | 检查 Launcher、JSON、Toto、设置页的位置/尺寸/主题/图标和快捷键文案 | 与当前设置和现有布局一致；多屏上设置窗口保持在可用工作区 | 待手动观察 |
| UI-12 | 从托盘菜单退出应用 | 关闭所有窗口、停止后台提醒并释放进程；重新启动后单实例可正常获得 | 待手动观察 |
