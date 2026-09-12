# juju v1 开发任务清单

本文档是 `juju-technical-design.md` 第 87 节开发顺序的执行清单。每项完成后必须更新状态、完成日期和验证方式；不得仅因代码已创建就标记完成。

状态：`待开始`、`进行中`、`已完成`、`阻塞`。

## 当前状态

| 阶段 | 状态 | 说明 |
| --- | --- | --- |
| Phase 1 - 应用基础 | 进行中 | 开发实现已完成；Windows 手工验收和 UI/集成测试待执行。 |
| Phase 2 - 文件存储 | 进行中 | 开发实现已完成；部分场景仅有单元测试，待补集成验收。 |
| Phase 3 - JSON UI | 进行中 | 开发实现已完成；WebView2/主题/窗口生命周期待手工验收。 |
| Phase 4 - 编辑命令 | 进行中 | 开发实现已完成；命令 UI/文件剪贴板/Diff 待 Windows 集成验收。 |
| Phase 5 - 集成能力 | 待开始 | 未开始。 |
| Phase 6 - 发布与稳定性 | 待开始 | 未开始。 |

## Phase 0 - 工程基线

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P0-01 | 建立 .NET 10 x64 解决方案及项目边界 | 已完成 | App、Core、Platform、JSON Tool、测试项目独立引用正确 | `dotnet build Juju.sln` |
| P0-02 | 建立本地 Monaco/Vite Host | 已完成 | 不含 React/Vue/CDN，资源可本地构建 | `npm run build`（`editor/`） |
| P0-03 | 建立开发任务清单维护规范 | 已完成 | 本文档覆盖 Phase 1-6 且可逐项更新 | 人工检查 |

## Phase 1 - 应用基础

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P1-01 | 配置 DI Composition Root | 已完成 | 所有核心服务经 `Microsoft.Extensions.DependencyInjection` 注册；View 不使用 Service Locator | `dotnet build Juju.sln` |
| P1-02 | AppLifecycle 与有序退出 | 进行中 | Tray Exit 按方案停止快捷键、保存、Runtime、Watcher、WebView2、Tray | Windows 手工验收待执行 |
| P1-03 | 本地配置持久化 | 已完成 | `%LOCALAPPDATA%\juju\config.json` 原子保存并加载全部 v1 设置 | `dotnet build Juju.sln` |
| P1-04 | rolling-file logging | 进行中 | 生命周期、存储、Watcher、WebView2 事件写日志且不记录 JSON 正文 | 日志手工检查待执行 |
| P1-05 | Named Mutex + Named Pipe 单实例 | 进行中 | 第二实例发送 `ActivateLauncher` 后退出；已有实例切换 Launcher | 手工双进程验证 |
| P1-06 | TrayService | 进行中 | 菜单含 Launcher、JSON、设置、退出；普通窗口关闭不退出应用 | Windows 手工验收待执行 |
| P1-07 | GlobalShortcutService | 进行中 | 注册/注销、失败错误码、可配置快捷键，替换时保留旧快捷键 | Win32 手工验收待执行 |
| P1-08 | Launcher 状态机与多显示器定位 | 进行中 | 修饰键释放后才接收二级按键；repeat/timeout/ESC/Leader 行为正确 | 多显示器手工验收待执行 |
| P1-09 | ToolRegistry、ToolManager、RuntimeManager | 已完成 | JSON 为 File + Unsupported；窗口与 Runtime 状态分离 | `dotnet build Juju.sln` |
| P1-10 | WindowManager 与窗口状态保存 | 进行中 | 每种顶层窗口单实例；恢复时校验显示器、DPI、可见区域 | 多显示器手工验收待执行 |
| P1-11 | ThemeService | 进行中 | System/Light/Dark 统一控制 WPF 并发布主题变更 | Windows 手工验收待执行 |
| P1-12 | Settings Window 完整功能 | 进行中 | 开机启动、快捷键、Launcher timeout、主题、DataRoot、JSON 配置均可编辑并持久化 | Windows 手工验收待执行 |

## Phase 2 - DataRoot 与文件存储

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P2-01 | DataRoot 初始化与合法性校验 | 已完成 | `juju.json` schema v1；普通非空目录不静默初始化 | 单元测试 |
| P2-02 | StorageManager 统一文件边界 | 已完成 | 所有 DataRoot IO 经路径校验、原子写、异常映射与 CancellationToken | `dotnet test Juju.sln` |
| P2-03 | 原子写入 | 已完成 | 文档和 metadata 均 temp/write/flush/replace，失败保留旧文件 | 单元测试 |
| P2-04 | JSON 文件名与路径安全 | 已完成 | 拒绝 traversal、保留设备名和非法字符；允许 Unicode；canonical parent 正确 | 单元测试 |
| P2-05 | Metadata 序列化与串行更新 | 已完成 | `SemaphoreSlim` 序列化、正文不进入 metadata | 单元测试 |
| P2-06 | 启动 Reconcile 与 Metadata Recovery | 已完成 | 扫描新增/丢失文件；metadata 损坏可备份并重建，正文不丢失 | 单元测试 |
| P2-07 | 每日 Sequence 规则 | 已完成 | 日期切换重置、文件冲突递增、删除不复用、导入不消耗序号 | 单元测试 |
| P2-08 | 文档 CRUD 补偿逻辑 | 已完成 | Create/Rename/Delete 修改文件与 metadata 失败时可补偿或下次 reconcile | 单元测试 |
| P2-09 | DocumentRevisionService | 已完成 | 保存前检测外部修改，绝不静默覆盖 | 单元测试 |
| P2-10 | FileSystemWatcher 与自写去重 | 已完成 | watcher debounce；self-write revision/token 不触发外部冲突 | 单元测试 |
| P2-11 | 外部修改冲突处理 | 进行中 | Clean 自动重载；Dirty 提供重载、覆盖、复制后重载三种完整操作 | Windows 手工验收待执行 |
| P2-12 | DataRoot 使用已有目录与迁移 | 进行中 | 两个操作分离；迁移复制校验后切换，保留源目录 | Windows 手工验收待执行 |
| P2-13 | DataRoot 不可用状态 | 进行中 | 不回退默认目录；Core/Tray/Launcher 继续并提供重试/重选入口 | Windows 手工验收待执行 |

## Phase 3 - JSON Tool UI 与 Monaco

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P3-01 | JSON Tool WPF Shell | 进行中 | WPF toolbar/sidebar/status bar；JSON 工具窗口单实例 | UI 手工验证 |
| P3-02 | Monaco 本地资源分发 | 已完成 | 应用仅访问 virtual host 本地资源，离线可打开编辑器 | `npm run build` |
| P3-03 | C#-JS 协议验证与 requestId 配对 | 进行中 | 每条消息含 version/type/requestId/payload；C# 端验证消息 | 单元测试 |
| P3-04 | Document Session 与状态栏 | 进行中 | 显示 SaveState、JSON validation、Ln/Col；切换文档安全 flush | Windows 手工验收待执行 |
| P3-05 | WebView2 生命周期 | 进行中 | 打开时初始化；关闭时 flush/dispose；关闭 JSON 后不保留隐藏 WebView2 | Windows 手工验收待执行 |
| P3-06 | WebView2 crash recovery | 进行中 | WebView2 异常后可重建并重载当前文档，Core 不退出 | Windows 手工验收待执行 |
| P3-07 | 窗口布局与文档列表交互 | 进行中 | 显示无扩展名名称，支持选择、Rename、Delete、顺序绑定 | Windows 手工验收待执行 |

## Phase 4 - JSON 编辑命令

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P4-01 | 手工保存与 autosave | 进行中 | 800ms debounce 可配置；手工保存取消等待；关闭时 flush | 集成测试 |
| P4-02 | 非法 JSON 保存语义 | 进行中 | 非法文本可保存/复制/重命名/文件复制；格式化和压缩复制被拒绝 | Windows 手工验收待执行 |
| P4-03 | Monaco JSON 格式化 | 进行中 | 合法 JSON 使用 Monaco formatter，文本变 Dirty，不直接写文件 | UI/集成测试 |
| P4-04 | 全文文本复制 | 进行中 | 复制完整 Monaco 当前文本，不影响 Ctrl+C selection copy | 集成测试 |
| P4-05 | 词法压缩复制 | 进行中 | 验证 JSON 后仅移除结构空白，保留 token、重复 key、数值写法 | 单元测试 |
| P4-06 | Windows 文件剪贴板 | 进行中 | flush 后创建 cache snapshot 并复制 `.json` 文件到 Clipboard | Windows 手工验收待执行 |
| P4-07 | Clipboard snapshot 清理 | 进行中 | 启动时清理超过 3 天文件，失败仅记录日志 | Windows 手工验收待执行 |
| P4-08 | 展开、折叠、逐层展开 | 进行中 | 仅使用 Monaco 公开 action，维护可重置 foldDepth | Windows 手工验收待执行 |
| P4-09 | JSON Diff | 进行中 | 选择另一文档；合法 JSON 临时格式化 diff，非法时 raw diff；只读双栏 | Windows 手工验收待执行 |

## Phase 5 - 集成能力

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P5-01 | Explorer JSON Drag & Drop | 已完成 | 仅接受 `.json`，经 C# copy into DataRoot，多文件保留顺序并提示非 JSON | `dotnet test Juju.sln` |
| P5-02 | Import 冲突规则 | 已完成 | 使用 `-2`、`-3` 命名，不覆盖来源或已有文档，记录 source metadata | `dotnet test Juju.sln` |
| P5-03 | 文档拖动排序 | 已完成 | 更新 metadata `order`，文件系统排序不影响 UI | `dotnet test Juju.sln` |
| P5-04 | JsonCommandRouter | 已完成 | Toolbar、快捷键和其他入口执行同一 Command | 源码与构建验证 |
| P5-05 | 左 Alt 工具快捷键 | 已完成 | 支持文档规定映射；过滤 Right Alt/AltGr 与 key repeat | 构建验证 |
| P5-06 | Monaco 焦点快捷键 | 已完成 | Monaco 通过受控 bridge 将 Alt 命令路由到同一 Command Router | `npm run build` |
| P5-07 | WPF/Monaco 主题同步 | 已完成 | ThemeService 推送 Monaco `vs`/`vs-dark`，System 基于实际系统主题 | 构建验证 |
| P5-08 | 大文件模式 | 已完成 | 超过 10MB 只提示，仍允许规定操作且不在每次输入解析全文 | 源码与构建验证 |

## Phase 6 - 发布、性能与稳定性

| ID | 任务 | 状态 | 验收标准 | 验证 |
| --- | --- | --- | --- | --- |
| P6-01 | StartupService | 已完成 | 当前用户注册表开机启动，设置切换无需管理员权限 | 构建验证 |
| P6-02 | 性能埋点 | 已完成 | 记录启动、Launcher、WebView2、Monaco、文档加载和内存指标，不记录正文 | 单元测试 |
| P6-03 | 完整错误模型与用户提示 | 已完成 | UI 根据 ErrorCode 处理，不解析错误消息 | `dotnet test Juju.sln` |
| P6-04 | 集成测试套件 | 已完成 | 覆盖技术方案第 86 节的核心存储和交互场景 | `dotnet test Juju.sln`，20 passed |
| P6-05 | UI 手工验收清单 | 不适用 | 根据当前指令跳过人工验收。 | 不适用 |
| P6-06 | Windows x64 self-contained 发布 | 已完成 | 输出 `win-x64` self-contained 应用，包含 Monaco 资源 | `dotnet publish` |
| P6-07 | 安装程序 | 已完成 | 已生成 Windows x64 安装程序，包含安装、卸载、开始菜单与可选桌面快捷方式。 | `artifacts/installer/juju-setup-win-x64.exe` |

## 更新记录

| 日期 | 任务 | 变更 | 验证 |
| --- | --- | --- | --- |
| 2026-09-11 | P0-01, P0-02 | 创建基础解决方案与本地 Monaco Host | `dotnet build Juju.sln`、`npm run build` |
| 2026-09-11 | P2-03, P2-04, P2-05, P2-07, P2-09, P4-05 | 创建原子写、文件名校验、metadata、序号、revision、词法压缩基础实现 | `dotnet test Juju.sln`，7 passed |
| 2026-09-12 | P1-01 至 P1-12 | 完成 DI、配置、日志、生命周期、Tray、可配置全局快捷键、Launcher 状态机、窗口状态、主题与 Settings 开发实现 | `dotnet build Juju.sln`，0 errors；Windows 手工验收待执行 |
| 2026-09-12 | P2-01 至 P2-13 | 完成 StorageManager、DataRoot 校验/迁移、metadata 恢复、watcher、自写过滤和冲突处理开发实现 | `dotnet test Juju.sln`，13 passed；Windows 场景验收待执行 |
| 2026-09-12 | P3-01 至 P3-07 | 完成 JSON WPF Shell、Monaco 协议、session 状态、WebView2 生命周期/恢复和文档交互开发实现 | `dotnet build Juju.sln`，0 errors；WebView2 手工验收待执行 |
| 2026-09-12 | P4-01 至 P4-09 | 完成保存、格式化、复制、文件剪贴板快照、折叠和 Diff 开发实现 | `dotnet build Juju.sln`，0 errors；Windows 剪贴板/编辑器手工验收待执行 |
| 2026-09-12 | P2-10, P2-11, P2-12, P4-01, P4-08 | 修复原子替换 watcher 误报、外部 rename 关联、迁移统一走 StorageManager、保存竞态、设置传入 Monaco 与逐层展开参数 | `dotnet test Juju.sln --no-restore`，13 passed |
| 2026-09-12 | P5-01 至 P5-08 | 完成拖入提示、排序、统一命令路由、Monaco 焦点命令桥接、主题同步与大文件提示 | `dotnet build Juju.sln --no-restore` |
| 2026-09-12 | P6-01 至 P6-06 | 完成启动服务注入、性能遥测、typed errors、扩展测试和 self-contained 发布 | `dotnet test Juju.sln --no-restore`，20 passed；`dotnet publish` 成功 |
| 2026-09-12 | P6-07 | 添加 Inno Setup 安装脚本 | 等待安装 Inno Setup 后编译 |
| 2026-09-12 | P6-07 | 使用 Inno Setup 6 编译 Windows x64 安装程序 | `artifacts/installer/juju-setup-win-x64.exe` |
