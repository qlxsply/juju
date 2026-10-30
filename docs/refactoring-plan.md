# Juju 架构重构方案与实施计划

## 1. 目标与约束

### 重构目标

- 所有工具通过统一的 `IToolManager` 路由打开，便于注册新工具。
- WPF 表现层调用工具应用服务，不直接访问 Repository。
- Toto 完全融入 Juju 的工具模块和生命周期架构，不保留旧应用兼容层。
- Toto 的业务规则、当前数据格式和用户可见行为保持不变。
- 启动器、工具窗口、设置页和快捷键行为保持当前表现。
- Windows 系统事件由 Windows 平台适配层负责，不由 Toto 业务模块直接订阅。

### 不可变约束

1. 重构只调整职责、依赖方向、类型和包结构，不改变已有业务结果。
2. 不改变当前 Juju 数据路径、当前 JSON/CSV 格式、筛选/分页/排序、提醒时机和归档规则。
3. Toto 提醒仍在 Juju 启动时初始化；隐藏或关闭 Toto 窗口不停止后台提醒。
4. Toto 的节假日就绪检查、失败提示、五分钟重试、锁屏暂停、解锁/恢复后的重新调度均保持原行为。
5. JSON、Toto 和开发中工具在启动器中的显示与交互结果保持不变。
6. `JsonToolWindow` 的内部拆分类重构不在本轮优先范围；只在统一模块边界必需时调整其依赖。
7. 不拆分 `Juju.Core` 或增加通用基础设施程序集；先在现有解决方案结构内收紧边界。
8. **不兼容旧 Toto 应用代码**：不保留旧命名空间别名、旧 Host 转发层、旧配置/CSV 解析器或旧数据导入路径。

> “删除旧应用兼容”针对旧 Toto 版本的代码、配置、数据格式和启动行为；不删除 Juju 当前使用的数据，也不改变当前格式。

## 2. 当前结构与问题

当前解决方案包含：

```text
Juju.App                 WPF 启动器、工具窗口、设置页和应用组合根
Juju.Core                工具描述、应用设置、存储、日志与共享契约
Juju.Platform.Windows    Windows 热键、开机启动、窗口外观等平台能力
Juju.Tools.Json          JSON 文档、元数据、设置、文件监控与编辑业务
Juju.Tools.Toto          Toto 模型、仓储、日历与提醒调度
editor/                  Monaco 前端与 WebView2 消息协议
tests/                   JSON 与 Toto 自动化测试
```

待处理的边界问题：

1. `IToolManager` 已注册但未参与实际打开流程。Launcher 卡片、单键和 JSON/Toto 全局热键仍直接调用 `IWindowManager.ShowTool`。
2. `DesktopWindowManager.ShowTool` 集中分支创建 JSON/Toto 窗口；开发中工具的占位提示则由 Launcher 自行处理。新工具需要修改多个入口。
3. App 中的 Toto 主窗口、事项编辑窗口、历史窗口和窗口管理器通过 `TotoToolHost.Items` 直接访问 `ItemRepository`。
4. `TotoToolHost` 同时管理事项仓储、设置、提醒调度、日历重试、Timer、UI 同步上下文和 Windows `SystemEvents`。
5. Toto 项目名为 `Juju.Tools.Toto`，但源码仍使用 `Toto.App.*` 命名空间。
6. Toto 项目仍启用 WPF，并将 `toto.ico` 作为“迁移后的兼容资源”重复嵌入；实际窗口属于 `Juju.App`。
7. `ToolManager` / `RuntimeManager` 只有部分占位行为：`RuntimeManager.StartAsync` 只改状态；Toto 提醒运行时又由 App 单独启动。

## 3. 目标模块边界

### 3.1 统一工具入口

所有工具打开请求通过同一个路由：

```text
启动器卡片 / 工具单键 / 全局直达键
                 ↓
             IToolManager
                 ↓
          工具模块注册表
                 ↓
     就绪检查、工具操作及窗口展示
```

`IToolManager` 是工具打开的唯一应用入口。工具模块适配器负责工具自身的就绪、打开及可选后台生命周期；Launcher 和窗口管理器不再各自实现 JSON、Toto 或占位工具分支。

工具目录统一描述稳定 ID、名称、说明、图标键、启动单键和能力。Launcher 使用现有行模板呈现目录数据，保留现有视觉和交互。模块注册也应能贡献工具设置页。

### 3.2 Toto 应用服务

Toto 应用服务封装 App 所需的用例：

- 获取活动事项、按条件读取/分页查询历史。
- 新增、更新、完成和取消事项。
- 获取每日盘点所需的活动和完成事项快照。
- 提供 Toto 设置和节假日日历所需的应用操作。

应用服务初期以调用现有 Repository 为主，保留现有筛选、排序、归档、校验和错误语义。Repository 留在 Toto 模块内部，App 只依赖应用服务接口和对外数据类型。

### 3.3 Toto 运行时和 Windows 事件

```text
Juju.App
  Toto 工具模块、WPF 窗口、设置页、平台事件协调

Juju.Tools.Toto
  Domain          事项模型、状态、查询条件、日历规则
  Application     Toto 应用服务、提醒运行时和用例协调
  Infrastructure  Repository、CSV/JSON、日历下载和文件访问

Juju.Platform.Windows
  Windows 会话、电源恢复、系统时间事件的订阅与适配
```

Windows 事件经平台适配和 App 组合层传给 Toto 运行时。Toto 模块不引用 WPF，不直接订阅 `Microsoft.Win32.SystemEvents`，不接收 WPF `Dispatcher` 或 UI 同步上下文。

### 3.4 精确到包的目标结构

本方案中的 **模块** 指解决方案项目/程序集；**包** 指项目内的目录与 C# namespace。小型功能先留在现有程序集内，但按下列包边界组织；不为每个包单独建立 NuGet 包或项目。包内的类型可以在后续不改 namespace 的前提下迁移到独立程序集。

```text
src/Juju.Core/                         # 跨工具、平台无关的共享内核
  Domain/
    Errors/                            # ErrorCode 等稳定领域错误
    Tools/                             # ToolId、后台/存储能力等稳定枚举
  Contracts/
    Tools/                             # IToolRegistry、通用 ToolDescriptor 契约；不含 IToolManager
    Settings/                          # ISettingsService
    Storage/                           # IAtomicFileWriter、IStorageManager、IToolDataPaths 等端口
    Platform/                           # ISystemLifecycleEvents、IFolderPicker、IWorkAreaProvider、ITrayIconService 等平台端口
    Diagnostics/                        # IPerformanceTelemetry 等观测端口
  Policies/
    Shortcuts/                          # GlobalHotkeyPolicy 等跨工具快捷键规则
    Tools/                              # ToolLaunchKeys 等宿主级、平台无关策略
  Settings/
    AppSettings.cs                      # 宿主配置模型
  Infrastructure/
    Settings/                           # SettingsService
    Storage/                            # AtomicFileWriter、DataRootService、StorageManager、目录变更
    Diagnostics/                        # RollingFileLoggerProvider、PerformanceTelemetry

src/Juju.Platform.Windows/             # Core 平台端口的 Windows 实现
  HotKeys/                              # RegisterHotKey 封装
  Startup/                              # Windows 开机启动实现
  Windowing/                            # WindowAppearance、工作区查询
  Shell/                                # NotifyIcon 与托盘菜单基础设施
  Dialogs/                              # FolderPicker 等 Windows 对话框适配
  Lifecycle/                            # SystemEvents 会话、电源、时间变化订阅器

src/Juju.Tools.Json/                   # JSON 工具业务模块，不引用 App/WPF/Windows/Toto
  Domain/
    Documents/                           # JsonDocumentId、文档值对象、版本语义
    Diff/                                # 纯差异比较规则
  Application/
    Ports/                               # IJsonDocumentRepository、revision/store 接口
    Documents/                           # IJsonDocumentService、文档用例编排
    Minification/                        # JSON 压缩规则
    Settings/                            # JsonToolSettings、命令目录、快捷键校验
  Infrastructure/
    Documents/                           # 文档文件仓储、metadata、revision、FileSystemWatcher
    Settings/                            # JsonToolSettingsService 的文件持久化

src/Juju.Tools.Toto/                   # Toto 业务模块，不引用 App/WPF/Windows/Json
  Domain/
    Items/                               # TodoItem、状态、查询条件、历史页
    Calendar/                            # HolidayCalendarDay、工作日规则
  Application/
    Ports/                               # 事项、设置、日历 Repository/Store 接口
    ITotoApplicationService.cs           # App 面向的 Toto 用例入口
    Items/                               # ITotoItemsApplicationService、事项请求/快照/历史分页模型
    Settings/                            # ITotoSettingsApplicationService、设置用例
    Calendar/                            # ITotoCalendarApplicationService、节假日快照/用例
    Reminders/                           # ReminderScheduler、TotoReminderRuntime
  Infrastructure/
    Items/                               # ItemRepository、CSV 编解码
    Settings/                            # JSON 设置文件仓储
    Calendar/                            # 工作日仓储、节假日下载
    Storage/                             # TotoPaths 等工具数据路径适配

src/Juju.App/                           # 唯一桌面组合根和 WPF 表现层
  Bootstrap/                             # App composition、关停、窗口状态协调
  Contracts/
    Bootstrap/                           # IAppLifecycle 等应用启动/关停契约
    Windows/                              # IAppWindowManager、IJsonWindowFactory、ITotoWindowFactory 等 WPF 宿主端口
    Settings/                             # ISettingsSection、设置页贡献契约
  Tools/
    Contracts/                           # IToolManager、IToolModule、工具设置贡献契约
    Catalog/                              # ToolRegistry、LauncherToolMetadata、模块描述与注册表
    Routing/                              # ToolManager 的实现和 ToolId → IToolModule 路由
    Json/                                 # JsonToolModule、Json 窗口/编辑器生命周期
    Toto/                                 # TotoToolModule、提醒调度和 Toto 窗口生命周期
    Placeholders/                         # EchoHttp/Kairos 开发中模块
  Presentation/
    Launcher/                             # LauncherWindow 及其模板，只依赖 IToolManager/目录模型
    Settings/                             # SettingsWindow、设置页、SettingsViewFactory
    Json/                                 # JsonToolWindow、编辑器视图，只依赖 JSON Application API
    Toto/                                 # TotoMain/Item/History/Reminder 窗口，只依赖 Toto Application API
    Shared/                               # ToolWindowChrome、共享窗口样式行为
  Integrations/
    JsonFileClipboard.cs                  # 依赖 Windows 剪贴板/宿主快照策略的 App 适配器

editor/src/                              # Monaco/WebView2 前端
  protocol.ts                             # 前后端消息 schema 与版本
  bridge.ts                               # WebView 消息收发
  editor.ts                               # Monaco 命令和编辑器适配

tests/
  Juju.Tools.Json.Tests/                  # JSON 领域、用例、仓储测试
  Juju.Tools.Toto.Tests/                  # Toto 领域、应用服务、仓储测试
  Juju.Architecture.Tests/                # namespace/package 依赖规则测试
  Juju.App.Tests/                         # 工具目录、路由与后台生命周期测试
```

#### 当前类型迁移目标

| 当前类型/位置 | 目标包 | 说明 |
| --- | --- | --- |
| `Core.App.IToolManager` | `Juju.App.Tools.Contracts.IToolManager` | 工具打开是桌面宿主应用端口，不属于 Core 通用契约 |
| `Core.App.ToolManager` | `Juju.App.Tools.Routing.ToolManager` | 统一调用注册的 `IToolModule` |
| `Core.App.IRuntimeManager` / `RuntimeManager` | `Juju.App.Tools.Contracts` 模块生命周期或移除 | 删除不实际启动工具、只改内存状态的占位运行时 |
| `Core.App.IAppLifecycle` | `Juju.App.Contracts.Bootstrap.IAppLifecycle` | 应用关停只属于宿主组合层 |
| `Core.App.IWindowManager` | `Juju.App.Contracts.Windows.IAppWindowManager` | 从 Core 移出窗口宿主端口；不再承担工具打开旁路路由 |
| `Core.Tools.ToolRegistry` | `Juju.App.Tools.Catalog.ToolRegistry` | Core 保留通用 `IToolRegistry`/descriptor 契约，不列举具体内置模块 |
| `Toto.App.Domain.*` | `Juju.Tools.Toto.Domain.*` | 不提供旧 namespace alias |
| `Toto.App.Data.ItemRepository` | `Juju.Tools.Toto.Infrastructure.Items` | 仅由 Toto Application/ReminderRuntime 使用 |
| `Toto.App.Services.ReminderScheduler` | `Juju.Tools.Toto.Application.Reminders` | 无 WPF `SynchronizationContext` 或 Windows `SystemEvents` 依赖 |
| `Toto.App.Services.WorkCalendarService` | `Juju.Tools.Toto.Domain.Calendar.WorkdayPolicy` + `Juju.Tools.Toto.Application.Calendar` | 把纯工作日规则与仓储读取分开；规则只接收日期和覆盖值 |
| `Toto.App.Data.DateTimeText` | `Juju.Tools.Toto.Infrastructure.Items.TotoCsvDateTimeCodec` + `Juju.App.Presentation.Toto.TotoDateTimeFormatter` | 拆开 CSV 时间编解码与 UI 显示格式 |
| `TotoToolHost` | `TotoApplicationService` + `TotoReminderRuntime` + `Juju.App.Tools.Toto.TotoToolModule` | 删除旧宿主形态，不留转发 wrapper |
| `Juju.App.Views.Toto*` | `Juju.App.Presentation.Toto` | 只调用 Toto 应用服务，不直接调用 Repository |
| `Juju.Tools.Toto/toto.ico` EmbeddedResource | `Juju.App/Assets/toto.ico` Resource | 只保留当前 UI 使用的一份品牌资源，去掉迁移兼容副本 |
| `Juju.App.Bootstrap.TrayService` | `Juju.Platform.Windows.Shell.NotifyIconService` + `Juju.App.Bootstrap.TrayController` | WinForms NotifyIcon/菜单渲染由 `ITrayIconService` 端口隔离；菜单命令及应用回调留在 App |
| `SettingsDialogService.EditFolder` 的 WinForms Dialog | `Juju.Platform.Windows.Dialogs.IFolderPicker` 实现 | 设置弹层仍归 App，原生目录选择器由平台注入 |
| `DesktopWindowManager` 中的 `Screen.FromPoint` | `Juju.Platform.Windows.Windowing.IWorkAreaProvider` | App 依赖工作区查询端口，不直接引用 WinForms Screen |
| `Juju.Tools.Toto.Services.AppLog` | `ILogger<TotoReminderRuntime>` / Core Diagnostics | 去掉 Toto 自建日志实现，保留必要错误和重试日志 |

### 3.5 包依赖方向

项目级引用保持单向：

```text
Juju.App ───────> Juju.Core
   │              Juju.Tools.Json ───────> Juju.Core
   ├────────────> Juju.Tools.Toto ───────> Juju.Core
   └────────────> Juju.Platform.Windows ─> Juju.Core
```

包级引用规则（`A → B` 表示 A 可以依赖 B）：

```text
Core.Domain                         → 无产品层依赖
Core.Contracts                      → Core.Domain
Core.Policies                       → Core.Domain / Core.Contracts
Core.Infrastructure                 → Core.Contracts / Core.Domain / Core.Settings

Tool.Domain                         → Core.Domain
Tool.Application.Ports              → Tool.Domain / Core.Contracts
Tool.Application                    → Tool.Domain / Tool.Application.Ports / Core.Contracts
Tool.Infrastructure                 → Tool.Domain / Tool.Application.Ports / Core.Contracts

Platform.Windows                    → Core.Contracts / Core.Domain
App.Tools.Contracts                 → App.Contracts.Settings / App.Contracts.Windows / Core.Contracts / Core.Domain
App.Tools.Catalog                   → App.Tools.Contracts / Core.Contracts.Tools / Core.Domain
App.Tools.Routing                   → App.Tools.Contracts / App.Tools.Catalog / App.Contracts.Windows
App.Tools.<Module>                  → App.Tools.Contracts / matching Tool.Application / App.Contracts.Windows / Core.Contracts.Platform
App.Presentation.<Tool>             → App.Contracts.Windows / matching Tool.Application 公共模型
App.Presentation.Settings           → App.Contracts.Settings / 各工具公开设置 API
App.Bootstrap/Composition           → App.Tools / App.Presentation / Platform.Windows / Core.Infrastructure / Tool.Infrastructure
```

约束：

- Core 不引用 App、Platform 或任何工具模块。
- Core 不声明 WPF 窗口接口、工具打开入口或具体工具注册实现；这些属于 `Juju.App`。
- Json、Toto 两个工具模块互不引用。
- Tool Domain 不引用 Application、Infrastructure、WPF 或 Windows。
- Tool Application 通过 `Application.Ports` 使用 Repository，不引用具体 Infrastructure 实现。
- Tool Infrastructure 不能反向调用 Presentation 或 App。
- App Presentation 不直接引用 Tool Infrastructure/Repository；只有 Composition Root 和 Tool Module 可组装实现。
- Platform.Windows 不引用 App 或工具程序集；只实现 Core 定义的平台端口。
- C# 与 `editor/` 之间只通过版本化消息协议通信，不共享运行时对象。

这些 package 在第一阶段是目录/namespace 边界，不新增程序集。由于同一程序集中的 namespace 本身不能阻止反向 `using`，实施时增加 `Juju.Architecture.Tests`（采用 ArchUnitNET/等价架构测试）约束上表规则，并检查禁止依赖和循环依赖。测试项目可以引用被测产品项目，但产品项目不得引用测试项目。将来只有当包有独立发布、版本或部署需求时，才将该包目录提升为独立 `.csproj`。

### 3.6 包命名和拆分规则

- 目录名与 namespace 一一对应；类型迁移时同时调整目录、namespace 和引用，不保留旧 namespace 兼容层。
- `Domain` 仅放业务值对象和纯规则；不读文件、不使用 Dispatcher、`SystemEvents` 或 WPF 控件。
- `Application` 放用例接口/实现和编排逻辑；依赖领域类型与端口，不依赖具体 Repository。
- `Infrastructure` 放文件、网络、系统 API 等实现；实现由 Application/共享 Contracts 定义的端口。
- `Presentation` 只负责 WPF 输入输出和视图状态；不直接调用 Infrastructure。
- 一个 package 中的类只引用自身层级或依赖 DAG 中更内层的包；禁止反向引用和跨工具引用。
- 当前项目内先用文件夹/namespace 划分 package，并用 `Juju.Architecture.Tests` 验证；项目引用边界继续由 `.csproj` 保证。
- 新工具遵循模板：工具 Domain / Application / Infrastructure 包、App 工具模块适配器、Presentation 视图、测试；不得在 Core、Launcher 或其他工具包中实现工具专属规则。

### 3.7 对象职责与命名约定

- `Domain` 使用不依赖 UI/IO 的值对象和纯规则；不在 Domain 类型中加入 `Text`、`DisplayName`、`Brush` 等展示属性。
- `Application.Ports` 只放用例需要的外部能力接口，如 `ITotoItemRepository`、`ITotoSettingsStore`；具体类放在 `Infrastructure` 并以 `Repository`/`Store` 命名。
- `Application` 对外只暴露工具用例服务和稳定快照/请求模型，如 `ITotoApplicationService`、`TotoItemSnapshot`；不把 CSV 行或 EF/文件类型泄漏给 UI。
- 文件格式转换属于 `Infrastructure`：例如 Toto CSV 日期编解码；UI 日期格式化属于 `Juju.App.Presentation.Toto`，不复用数据层的显示格式类。
- WPF `*Row`、`DataTemplate`、窗口状态、工具提示、快捷键显示属于 `Presentation`；它们只能调用 Application 服务。
- `ToolModule` 是 App 层适配器：接入工具的 Application API、窗口工厂、生命周期和设置贡献，不承载事项/JSON 业务规则。
- 一个类型只保留一个明确职责；只有当前实现证明具有相同业务含义时才共享类，不为“复用”把不同工具模型合并进 Core。

### 3.8 新工具接入模板

新增一个工具（例如 `Notes`）时，目标改动集中在：

1. `Juju.Core.Domain.Tools` 增加稳定 `ToolId`（或以稳定 ID 注册工具元数据）。
2. 新建 `Juju.Tools.Notes` 项目，内部按 `Domain`、`Application`、`Infrastructure` 分包；只引用 Core Domain/Contracts。
3. 在 `Juju.App.Tools.Notes` 实现工具模块入口和生命周期适配；不让工具业务项目引用 WPF。
4. 在 `Juju.App.Presentation.Notes` 添加 WPF 窗口/视图，并只调用 Notes Application API。
5. 通过工具目录贡献名称、说明、图标键、启动单键和能力；通过模块注册贡献设置页。
6. 增加 Notes 单元/模块测试和目录注册测试；架构规则确保其不依赖 JSON、Toto、Platform.Windows 或 App Infrastructure。

启动器使用统一的目录数据和既有行模板，因而新增卡片不需要在 `LauncherWindow.xaml.cs` 里增加 `switch`，也不需要在 `DesktopWindowManager.ShowTool` 里追加类型分支。

### 3.9 包内职责和引用清单

下表把包边界落实到当前项目中的具体职责。初期这些 package 是同一 `.csproj` 内的目录/namespace；拆分程序集时保持相同 namespace 和依赖方向。

| 项目 | 包/namespace | 包内职责与类型 | 可以引用 |
| --- | --- | --- | --- |
| `Juju.Core` | `Juju.Core.Domain.Errors` | `ErrorCode` 等稳定错误标识 | 无产品项目 |
| `Juju.Core` | `Juju.Core.Domain.Tools` | `ToolId`、后台/存储能力枚举 | `Core.Domain.Errors`（如需要） |
| `Juju.Core` | `Juju.Core.Contracts.Tools` | 通用 `IToolRegistry`、`ToolDescriptor` 契约 | `Core.Domain.Tools` |
| `Juju.Core` | `Juju.Core.Contracts.Settings` | `ISettingsService` 等设置端口 | `Core.Domain` |
| `Juju.Core` | `Juju.Core.Contracts.Storage` | `IAtomicFileWriter`、`IStorageManager`、`IToolDataPaths`、目录校验结果 | `Core.Domain` |
| `Juju.Core` | `Juju.Core.Contracts.Platform` | `ISystemLifecycleEvents`、`IFolderPicker`、`IWorkAreaProvider`、`ITrayIconService` 等与具体框架无关的端口 | `Core.Domain` |
| `Juju.Core` | `Juju.Core.Contracts.Diagnostics` | `IPerformanceTelemetry`、遥测值对象 | `Core.Domain` |
| `Juju.Core` | `Juju.Core.Policies.Shortcuts` | `GlobalHotkeyPolicy` 等跨工具快捷键校验 | `Core.Domain`、`Core.Contracts` |
| `Juju.Core` | `Juju.Core.Settings` | `AppSettings` 等宿主设置模型 | `Core.Domain` |
| `Juju.Core` | `Juju.Core.Infrastructure.Settings` | `SettingsService` 文件持久化实现 | `Core.Contracts.Settings`、`Core.Contracts.Storage` |
| `Juju.Core` | `Juju.Core.Infrastructure.Storage` | `AtomicFileWriter`、`ApplicationPaths`、`DataRootService`、`DataDirectoryChangeService`、`StorageManager`、用户主动换目录时的复制设施 | `Core.Contracts.Storage`、`Core.Contracts.Settings` |
| `Juju.Core` | `Juju.Core.Infrastructure.Diagnostics` | `RollingFileLoggerProvider`、遥测实现 | `Core.Contracts.Diagnostics` |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.HotKeys` | `GlobalShortcutService` | `Core.Contracts`、`Core.Domain` |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.Startup` | Windows 登录启动实现 | `Core.Contracts` |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.Windowing` | `WindowAppearance`、工作区/屏幕信息 | `Core.Contracts` |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.Shell` | `NotifyIcon`、托盘菜单控件及 WinForms 细节，实现 `ITrayIconService` | `Core.Contracts`；不引用 App/Toto/Json |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.Dialogs` | 文件夹选择器等原生对话框适配器 | `Core.Contracts` |
| `Juju.Platform.Windows` | `Juju.Platform.Windows.Lifecycle` | `SystemEvents` 订阅并发布平台无关事件 | `Core.Contracts.Platform` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Domain.Documents` | 文档 ID、修订/元数据值对象 | `Core.Domain` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Domain.Diff` | 纯差异比较规则 | `Juju.Tools.Json.Domain` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Application.Ports` | `IJsonDocumentRepository`、metadata/revision store 端口 | `Json.Domain`、`Core.Contracts` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Application.Documents` | `IJsonDocumentService`、文档用例/编排 | `Json.Domain`、`Json.Application.Ports`、`Core.Contracts` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Application.Settings` | 设置模型、快捷键目录与校验、设置服务端口 | `Json.Domain`、`Core.Contracts` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Application.Minification` | JSON 压缩用例/算法 | `Json.Domain` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Infrastructure.Documents` | 文件读写仓储、`JsonMetadataStore`、`JsonDocumentWatcher`、修订戳实现 | `Json.Application.Ports`、`Json.Domain`、`Core.Contracts.Storage` |
| `Juju.Tools.Json` | `Juju.Tools.Json.Infrastructure.Settings` | `settings.json` 持久化实现 | `Json.Application.Settings`、`Core.Contracts.Storage` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Domain.Items` | `TodoItem`、状态、查询条件、历史页等业务类型 | `Core.Domain`（仅稳定基础类型） |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Domain.Calendar` | 节假日值对象和纯工作日规则 | `Toto.Domain` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application.Ports` | 事项、设置、日历 Repository/Store 端口 | `Toto.Domain`、`Core.Contracts` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application` | `ITotoApplicationService` 组合入口/实现，协调事项、设置和日历用例 | `Toto.Domain`、`Application.Ports`、`Core.Contracts` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application.Items` | `ITotoItemsApplicationService`、事项请求/快照、历史查询/分页模型 | `Toto.Domain`、`Application.Ports` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application.Settings` | `ITotoSettingsApplicationService`、设置用例契约 | `Application.Ports`、`Core.Contracts` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application.Calendar` | `ITotoCalendarApplicationService`、节假日快照/用例契约 | `Toto.Domain`、`Application.Ports` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Application.Reminders` | 唯一提醒调度器和 Toto 后台运行时 | `Toto.Domain`、Toto Application 端口、`Core.Contracts` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Infrastructure.Items` | `ItemRepository`、`CsvFile`、现行 CSV 编解码 | `Toto.Application.Ports`、`Toto.Domain`、`Core.Contracts.Storage` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Infrastructure.Settings` | 当前 JSON 配置文件仓储 | `Toto.Application.Settings`、`Core.Contracts.Storage` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Infrastructure.Calendar` | 日历仓储、下载器和现行 `yyyy.json` 缓存 | `Toto.Application.Calendar`、`Toto.Domain`、`Core.Contracts` |
| `Juju.Tools.Toto` | `Juju.Tools.Toto.Infrastructure.Storage` | 注入后的 Toto 专属数据路径值对象 | `Core.Contracts.Storage` |
| `Juju.App` | `Juju.App.Bootstrap` | Composition Root、应用启动/关停协调 | 所有模块及基础设施实现；全解只允许在此处发生 |
| `Juju.App` | `Juju.App.Contracts.Bootstrap` | `IAppLifecycle` 等宿主启动/关停契约 | Core 契约 |
| `Juju.App` | `Juju.App.Contracts.Windows` | `IAppWindowManager`、`IJsonWindowFactory`、`ITotoWindowFactory` 等 WPF 宿主端口 | Core Domain/Contracts |
| `Juju.App` | `Juju.App.Contracts.Settings` | `ISettingsSection`、设置贡献契约 | App 的工具 Application API |
| `Juju.App` | `Juju.App.Tools.Contracts` | `IToolManager`、`IToolModule`、可选后台生命周期契约 | Core Domain/Contracts、App.Contracts.Settings |
| `Juju.App` | `Juju.App.Tools.Catalog` | `ToolRegistry`、Launcher 显示名/说明/图标键/启动单键和状态 | Core 通用工具描述、App.Tools.Contracts |
| `Juju.App` | `Juju.App.Tools.Routing` | `IToolManager` 实现、模块查找与统一打开路由 | App.Tools.Contracts、App.Tools.Catalog、App.Contracts.Windows |
| `Juju.App` | `Juju.App.Tools.Json` / `.Toto` / `.Placeholders` | 工具模块适配器；组装 Application API 与窗口工厂 | 对应工具 `Application`、App.Contracts.Windows、Core.Contracts.Platform |
| `Juju.App` | `Juju.App.Presentation.Launcher` | `LauncherWindow` 及模板，只依赖 `IToolManager` 和工具目录数据 | App.Tools.Contracts、App.Tools.Catalog、App.Contracts.Windows |
| `Juju.App` | `Juju.App.Presentation.Settings` | `SettingsWindow`、设置视图和设置行工厂 | App.Contracts.Settings、各工具公开设置 API |
| `Juju.App` | `Juju.App.Presentation.Json` / `.Toto` / `.Shared` | JSON/Toto WPF 窗口、共享窗口外观和各工具窗口工厂实现 | 对应工具 `Application` 公共 API、App.Contracts.Windows；不得引用 Infrastructure |

`App.Tools.Catalog` 的图标键是字符串/资源键，不把 WPF `ImageSource` 放入 Core 或工具业务模型。`IAppWindowManager` 只负责 Launcher、Settings 和通用窗口协调；工具窗口通过 `IJsonWindowFactory` / `ITotoWindowFactory` 提供，不再由通用窗口管理器按 ToolId 分支创建。

Core 保留稳定 `ToolId` 和现行宿主设置所需的启动键规则，但不存放具体工具模块实现、Launcher 文案/图标资源或打开窗口的分支；具体工具目录由 App 组合。

### 3.10 强制的依赖 DAG

```text
Core.Domain
   ↑                         ↑
Core.Policies ───────────────┘
   ↑
Core.Settings
   ↑
Core.Contracts.Settings

Core.Domain → Core.Contracts.Storage / Platform / Diagnostics
Core.Contracts.Storage + Core.Settings → Core.Infrastructure.Storage
Core.Contracts.Settings + Core.Contracts.Storage → Core.Infrastructure.Settings
Core.Contracts.Diagnostics → Core.Infrastructure.Diagnostics

Tool.Domain
   ↑
Tool.Application.Ports ─ Core.Contracts
   ↑
Tool.Application ──────── Tool.Domain + Tool.Application.Ports + Core.Contracts
   ↑
Tool.Infrastructure ───── Tool.Domain + Tool.Application.Ports + Core.Contracts

Platform.Windows ─────── Core.Contracts

App.Contracts.Bootstrap ─ 无产品层依赖
App.Contracts.Windows ── Core.Contracts + Core.Domain
App.Contracts.Settings ─ 无产品层依赖
App.Tools.Contracts ──── App.Contracts.Settings + App.Contracts.Windows + Core.Contracts + Core.Domain
App.Tools.Catalog ────── App.Tools.Contracts + Core.Contracts.Tools + Core.Domain
App.Tools.Routing ────── App.Tools.Contracts + App.Tools.Catalog
App.Presentation.Launcher ─ App.Tools.Contracts + App.Tools.Catalog + App.Contracts.Windows
App.Presentation.Settings ─ App.Contracts.Settings + Tool.Application 公共模型
App.Presentation.<Tool> ─── App.Contracts.Windows + Tool.Application 公共模型
App.Tools.<Module> ────── App.Tools.Contracts + Tool.Application + App.Contracts.Windows + Core.Contracts.Platform
App.Bootstrap/Composition ─── App.Tools + App.Presentation + Platform.Windows + Core.Infrastructure + Tool.Infrastructure
```

上图是层次示意；**以下允许依赖表是执行和架构测试的权威规则**（`A -> B` 表示 A 可以引用 B）：

```text
Core.Domain.*                    -> 无产品层依赖
Core.Policies.*                  -> Core.Domain.*
Core.Settings                   -> Core.Domain.* + Core.Policies.*
Core.Contracts.Settings         -> Core.Settings
Core.Contracts.Storage          -> Core.Domain.*
Core.Contracts.Platform         -> Core.Domain.*
Core.Contracts.Diagnostics      -> Core.Domain.*
Core.Contracts.Tools            -> Core.Domain.Tools
Core.Infrastructure.Settings   -> Core.Contracts.Settings + Core.Contracts.Storage + Core.Settings
Core.Infrastructure.Storage    -> Core.Contracts.Storage + Core.Contracts.Settings + Core.Settings
Core.Infrastructure.Diagnostics -> Core.Contracts.Diagnostics

Tool.Domain                     -> Core.Domain.*
Tool.Application.Ports          -> Tool.Domain.* + Core.Contracts.*
Tool.Application                -> Tool.Domain.* + Tool.Application.Ports + Core.Contracts.*
Tool.Infrastructure             -> Tool.Domain.* + Tool.Application.Ports + Core.Contracts.*

Platform.Windows                -> Core.Contracts.* + Core.Domain.*

App.Contracts.Bootstrap         -> 无产品层依赖
App.Contracts.Windows           -> Core.Contracts.* + Core.Domain.*
App.Contracts.Settings          -> 无产品层依赖
App.Tools.Contracts             -> App.Contracts.Settings + App.Contracts.Windows + Core.Contracts.* + Core.Domain.*
App.Tools.Catalog               -> App.Tools.Contracts + Core.Contracts.Tools + Core.Domain.Tools
App.Tools.Routing               -> App.Tools.Contracts + App.Tools.Catalog + App.Contracts.Windows
App.Presentation.Launcher       -> App.Tools.Contracts + App.Tools.Catalog + App.Contracts.Windows
App.Presentation.Settings       -> App.Contracts.Settings + Tool.Application 公共模型
App.Presentation.<Tool>         -> App.Contracts.Windows + Tool.Application 公共模型
App.Tools.<Module>              -> App.Tools.Contracts + Tool.Application + App.Contracts.Windows + Core.Contracts.Platform
App.Bootstrap                   -> App.Tools + App.Presentation + Platform.Windows + Core.Infrastructure + Tool.Infrastructure
```

依赖方向规则：

1. `Domain` 不引用 `Application`、`Infrastructure`、`App`、`Platform.Windows` 或其他工具。
2. `Application` 只能向内引用同工具的 `Domain` 和 Core 契约；仓储依赖以接口/端口表达。
3. `Infrastructure` 实现内层端口，不被 Domain/Application 反向引用。
4. Json 与 Toto 之间零项目引用、零包引用；跨工具协作只能经 App 的明确协调契约。
5. `Platform.Windows` 只实现 Core 的 OS/存储/启动端口，不知道 WPF 工具窗口和 Toto 领域模型。
6. 只有 App Composition Root 可引用具体 Infrastructure 实现；表现层只引用接口/应用服务。
7. project reference DAG 与 package DAG 分别校验：项目引用由 `.csproj` 保证，项目内 namespace 规则由 `Juju.Architecture.Tests` 保证。

## 4. 可执行重构步骤

### 阶段 0：建立行为基线

- [x] 在 `docs/behavior-baseline.md` 记录启动器卡片、启动单键、全局直达键和占位工具的当前行为。
- [x] 在 `docs/behavior-baseline.md` 记录 Toto 启动、日历未就绪提示、提醒调度、锁屏/解锁、电源恢复、系统时间变化和退出行为。
- [x] 保留现有 JSON 与 Toto Repository 测试作为业务语义基线；重构前 `dotnet test Juju.sln` 为 JSON 38 项、Toto 49 项通过。
- [x] 建立 `docs/behavior-baseline.md` 手动 UI 验收清单；桌面逐项观察留待 Windows UI 验收时填写。
- [x] 新建 `Juju.Architecture.Tests`，锁定现有产品项目引用 DAG 并验证无循环引用；随每个包完成逐步开启 package/namespace 规则。

**验收：** 每阶段重构后，用户可见行为和业务数据结果均与基线一致。

### 阶段 1：建立工具模块契约与统一路由

- [x] 在 `Juju.App.Tools.Contracts` 定义工具元数据、就绪检查、打开、设置页贡献及后台启动/关停契约。
- [x] 建立 `Catalog` 与 `Routing` 包；`ToolManager` 成为真实路由，具体实现归 `Juju.App`，Core 只保留稳定 ID、能力和通用描述/注册表契约。
- [x] 为 JSON、Toto、EchoHttp 和 Kairos 注册模块。JSON 惰性打开；Toto 必需后台运行时由模块在 App 启动时启动；占位工具继续显示原提示。
- [x] 将启动器卡片、单键处理和 JSON/Toto/占位工具全局直达热键统一改为调用 `IToolManager`。
- [x] 通过 `IJsonToolWindow` / `ITotoToolWindow` 模块窗口端口打开/复用工具窗口；`DesktopWindowManager` 不再按 `ToolId` 分支执行 `ShowTool` 路由。
- [x] Launcher 从模块目录绑定 ItemsControl 行数据，复用现有行样式，并保留顺序、图标、单键、紧凑高度和键盘导航。
- [x] 工具模块通过 `SettingsSections` 提供设置页；JSON、Toto 与占位模块注册扩展负责各自模块装配。
- [x] 移除 Core 中占位 `ToolManager` / `RuntimeManager` 实现和直接 `ShowTool` 入口；App 生命周期统一启动/停止模块。
- [x] 新增 `Juju.App.Tests` 路由/目录/生命周期测试及架构规则，确保 Launcher 入口只调用 `IToolManager`、JSON/Toto 模块互不引用。

**验收：** 所有工具入口都经过同一条路由；新工具只需实现/注册模块、元数据和必要设置贡献，不在多个入口复制工具分支。

### 阶段 2：新增 Toto 应用服务，收回 Repository

- [x] 在 `Juju.Tools.Toto.Application` 定义 `ITotoApplicationService`、只读事项/历史/日历模型及新增事项请求。
- [x] 在 `Juju.Tools.Toto.Application.Ports` 定义事项、设置、日历 Repository 与运行时协调端口；将 `ItemRepository`、`SettingsRepository`、`WorkCalendarRepository`、日历下载器、CSV 文件设施和 `TotoPaths` 放入对应 `Infrastructure` 包实现/支撑这些端口。
- [x] 应用服务只依赖领域模型、Application Ports 和 Core 的快捷键策略；不直接依赖具体 Repository。
- [x] 通过包装和映射调用现有 Infrastructure，保留 Repository 的 CSV/JSON、校验、筛选、排序、分页、归档和错误语义，不重写底层规则。
- [x] Toto 数据目录由 App Composition 作为构造参数传入；JSON 工具设置文件路径也改为由 Composition 传入，工具项目不再自行查询静态 `ApplicationPaths`。
- [x] `TotoMainWindow` 活动事项读取、快捷键配置读取改为应用服务调用。
- [x] `TotoItemWindow` 新增、编辑、完成和取消改为应用服务用例。
- [x] `TotoHistoryWindow` 历史筛选、分页和详情数据改为应用服务调用。
- [x] `DesktopWindowManager` 每日待办/完成快照改为应用服务调用。
- [x] 提醒调度器留在 Toto 模块内，通过 Repository Ports 读写事项和设置；WPF 窗口只接收公开快照，不持有 Repository。
- [x] 移除 `TotoToolHost.Items`、Host 的 Repository 属性和 App 到 Repository 的直接调用。保留 `InternalsVisibleTo("Juju.App")` 仅供仍在使用的快捷键解析/显示格式内部工具，随阶段 3 搬迁这些工具后再移除。

**验收：** App 不再直接调用 `ItemRepository`；事项操作、历史查询、提醒和月归档结果保持不变。

### 阶段 3：以 Juju 模块架构重建 Toto 宿主与平台边界

- [ ] 将 `Toto.App`、`Toto.App.Data`、`Toto.App.Domain`、`Toto.App.Services` 全部重命名为 `Juju.Tools.Toto.*`，同步更新 App 和测试。
- [ ] 不保留旧 namespace alias、旧 `TotoToolHost` 转发 wrapper 或旧公开 API façade。
- [ ] 用 Toto 应用服务和模块运行时取代现有职责混合的 `TotoToolHost`；不要只改类名而保留旧 Host 结构。
- [ ] 将提醒运行时、Ready 状态、日历重试和设置用例分配给明确的 Toto Application/Runtime 服务。
- [ ] 将会话锁定/解锁、电源恢复、系统时间变化订阅移至 `Juju.Platform.Windows`；通过平台无关信号通知 Toto runtime。
- [ ] 把 WPF 线程派发留在 Juju.App：提醒运行时发布事件，App 模块将窗口显示请求派发到 UI 线程。
- [ ] 保持单一 ReminderScheduler：启动时创建一次，窗口开关不重复创建，关停时释放一次。
- [ ] 保持五分钟日历重试、锁屏暂停、恢复后的重调度以及当前错误提示。
- [ ] 将 App 的系统托盘实现拆成 Platform.Windows 的 `NotifyIconService` 与 App 的 `TrayController`，保持图标、菜单文案、命令和点击结果不变。
- [ ] 将文件夹选择和屏幕工作区查询抽为平台端口，由 Platform.Windows 提供 WinForms 实现；SettingsWindow 与目录选择结果保持不变。
- [ ] 将工具窗口工厂契约放在 `Juju.App.Contracts.Windows`，具体 JSON/Toto WPF 工厂放在各自 Presentation 包；ToolModule 只依赖工厂端口。
- [ ] 在托盘、文件夹选择、工作区查询全部迁移后移除 `Juju.App.csproj` 的 `UseWindowsForms`，Windows Forms 保留在 Platform.Windows。
- [ ] Toto 不再引用 WPF/Windows APIs 后，移除 `Juju.Tools.Toto.csproj` 的 WPF 依赖并改为平台无关 `net10.0`；App 不再直接使用 WinForms 后移除其 `UseWindowsForms`。
- [ ] 将 `toto.ico` 作为 App 展示资源单点管理，移除 Toto 程序集内重复嵌入的迁移兼容副本；更新对应资源测试。
- [ ] 将 Toto 内部日志改用共享 `ILogger`，保留失败、重试和运行状态日志语义。

**验收：** Toto 模块不引用 WPF 和 Windows `SystemEvents`；代码库中不再存在 `Toto.App.*` 或旧 Host 兼容层；应用仍执行原有提醒和窗口流程。

### 阶段 4：彻底移除旧应用兼容实现

- [ ] 全仓库审查并删除旧独立 Toto 进程/启动项处理、旧 `.toto` 目录导入、旧 INI 设置导入、旧 CSV 列格式解析和迁移专用资源。
- [ ] 清理只为旧 Toto 应用保留的分支、适配器、测试和过时注释/文档。
- [ ] 保留当前 Juju 的 `tools/toto/` 数据目录、JSON 配置和现行 CSV 格式；不做旧格式转换或迁移。
- [ ] 更新 Toto README 和架构学习文档，使项目结构、命名空间和运行时边界与代码一致。

**验收：** 搜索不到旧 Toto namespace、旧导入/格式解析和迁移兼容代码；当前 Juju 格式的数据照常读取和写入。

### 阶段 5：回归与发布验证

- [ ] `IToolManager` 单元测试：验证 ToolId 到模块的映射、就绪失败、开发中占位和后台模块的单次启动/关停。
- [ ] Toto 应用服务测试：验证其委托操作与现有 Repository 行为一致。
- [ ] Windows 事件桥接测试：验证锁屏、解锁、电源恢复、时间变化信号传递及资源释放。
- [ ] 运行 `dotnet test Juju.sln`。
- [ ] 运行 `dotnet build src/Juju.App/Juju.App.csproj -c Release` 和 Windows x64 安装包构建。
- [ ] 手动回归 Launcher 卡片/单键/全局热键、JSON/Toto 窗口、Toto 提醒、设置和应用关停。

## 5. 明确不做的重构

- 不拆分 `Juju.Core` 或新建通用 Infrastructure 项目；如后续规模确实需要，再独立评估。
- 不优先拆分 `JsonToolWindow` 内部的列表、自动保存、差异比较或 Monaco 桥接逻辑；当前重点是跨工具的应用边界。
- 不改变当前 Juju 数据、UI 和业务行为；不为任何旧 Toto 应用/API/数据格式保留兼容层。

## 6. 建议执行顺序

按照阶段编号逐阶段完成。每个阶段单独构建、运行相应测试并对照行为基线验收；阶段通过后再继续下一阶段。若某步需要改变可见行为或数据格式，应暂停并另行确认，不纳入本次架构重构。
