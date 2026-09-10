# juju 工具箱技术方案与开发规范

> 文档用途：作为 **juju** 项目的架构基线、开发计划和 AI 编码执行规范。  
> 当前版本目标：**Windows x64，仅实现 JSON 工具**；框架必须保留后续增加 HTTP Request、网络诊断等内置工具的能力。  
> 技术栈：**Rust + Tauri 2 + WebView2 + React + TypeScript + Monaco Editor**。  
> 本文档是当前阶段的实现依据。除非需求明确变更，开发时不要自行替换技术栈、存储模型或生命周期模型。

---

## 1. 项目概述

### 1.1 项目名称

- 产品名：`juju`
- 工程名建议：`juju`
- Windows 可执行文件：`juju.exe`
- 当前支持平台：**Windows 64 位**
- 当前编译目标：`x86_64-pc-windows-msvc`
- 架构上保留 `platform` 抽象层，以便未来增加其他桌面平台，但 **v1 不为 macOS/Linux 编写兼容代码，不为了跨平台牺牲 Windows 体验**。

### 1.2 产品定位

juju 是一个常驻 Windows 后台的开发者工具箱。

主要交互方式不是为每个工具注册大量全局快捷键，而是使用一个全局 Leader Shortcut：

```text
Ctrl + Shift + Alt + Space
             ↓
        打开 Launcher
             ↓
      1 / 2 / 3 / A / S ...
             ↓
         打开对应工具
```

当前只实现：

```text
1 -> JSON
S -> 设置
```

后续可扩展：

```text
2 -> HTTP Request
3 -> Network
4 -> Encode / Decode
...
```

只有 Leader Shortcut 是全局快捷键。`1 / 2 / A / S` 等二级按键只在 Launcher 获得焦点后作为普通键盘事件处理，**不得注册为系统全局单键快捷键**。

### 1.3 当前阶段目标

v1 必须完成：

1. juju 后台常驻框架。
2. 单实例运行。
3. 系统托盘。
4. 全局 Leader Shortcut。
5. Launcher。
6. Tool Registry / Tool Manager / Window Manager 基础框架。
7. 设置系统。
8. 可切换的数据目录。
9. 文件型 Storage Manager。
10. JSON 工具。
11. JSON 文档自动保存、外部修改检测。
12. JSON 格式化、复制、压缩复制、文件复制。
13. JSON 展开、折叠、逐层展开。
14. JSON Diff 对比。
15. 每个工具窗口单实例。
16. 后台运行能力模型保留，但 JSON 工具不支持后台运行。
17. Notification、SecretStore、Privilege 等未来能力只保留接口边界，不在 v1 做完整实现。

### 1.4 明确不做

v1 不实现：

- HTTP Request 工具。
- 网络诊断工具。
- 第三方插件系统。
- SQLite 或其他数据库。
- 云同步。
- 用户账户。
- 多端同步。
- 脚本扩展系统。
- Electron。
- Vue。
- 多个 JSON 工具窗口。
- JSON Schema 管理器。
- JSONPath/JMESPath 查询。
- JSON 语义 Diff（忽略 key 顺序等）。
- HTML 响应预览。
- 管理员权限功能。
- 自动更新，除非后续单独确定。
- 自定义 Windows 标题栏，v1 优先使用稳定的标准工具窗口；Launcher 可使用无边框浮层。

---

# 2. 技术选型

## 2.1 技术栈

### Rust / Native

```text
Rust stable
Tauri 2.x
WebView2
Tokio
serde
serde_json
toml
thiserror
notify
windows-rs
```

Tauri 插件：

```text
tauri-plugin-single-instance
tauri-plugin-global-shortcut
tauri-plugin-autostart
```

托盘、窗口、WebView 等优先使用 Tauri 2 Core API。

对于 Windows 特有能力，例如：

- 文件剪切板 `CF_HDROP`
- 更精确的多显示器处理
- Windows Credential Manager（未来）
- 按需提权 helper（未来）

统一封装在：

```text
src-tauri/src/platform/windows/
```

不得让 Windows API 调用散落在具体工具业务代码中。

### Frontend

```text
React
TypeScript
Vite
Monaco Editor
CSS Modules / 普通 CSS + CSS Variables
@tauri-apps/api
```

v1 不引入大型 UI 框架，不引入 Redux/Zustand 等全局状态库。

原则：

- 单窗口内部状态：React hooks/context。
- 跨窗口、跨工具、持久化状态：Rust Core 是事实来源。
- Monaco 仅在 JSON Tool 动态加载。
- Launcher 和 Settings 不得因为工程中存在 Monaco 而加载 Monaco bundle。

### 包管理

前端建议统一使用：

```text
pnpm
```

Rust 使用：

```text
cargo
```

版本策略：

- Tauri 必须为 `2.x`。
- 初始化项目时使用当前稳定版本。
- 提交 `Cargo.lock` 和 `pnpm-lock.yaml`。
- 不使用浮动 major 版本升级。
- 如果 AI 对具体 Tauri/Monaco API 名称不确定，必须先查当前官方文档，不得虚构 API。

---

# 3. 总体架构

## 3.1 逻辑架构

```text
                         Windows
                            │
             Ctrl + Shift + Alt + Space
                            │
                            ▼
┌──────────────────────────────────────────────────────┐
│                    juju Core                         │
│                       Rust                           │
│                                                      │
│  AppLifecycle                                        │
│  GlobalShortcutManager                              │
│  LauncherManager                                    │
│  ToolRegistry                                       │
│  ToolManager                                        │
│  RuntimeManager                                     │
│  SettingsService                                    │
│  StorageManager                                     │
│  WindowManager                                      │
│  TrayManager                                        │
│  NotificationService                                │
│                                                      │
│  platform/windows                                   │
│    ├── clipboard                                    │
│    ├── monitor                                      │
│    ├── credentials (future)                         │
│    └── privilege   (future)                         │
└──────────────┬───────────────────────────────────────┘
               │
               │ Tauri Commands / Events
               │
       ┌───────┼─────────────┐
       ▼       ▼             ▼
   Launcher   Settings     JSON Tool
   WebView2   WebView2     WebView2
                              │
                              ▼
                         Monaco Editor
                         Monaco DiffEditor
```

## 3.2 进程模型

juju v1 为：

```text
一个 juju.exe Rust 主进程
    ├── 0/1 Launcher WebView Window
    ├── 0/1 Settings WebView Window
    └── 0/1 JSON WebView Window
```

“工具独立”定义为：

- UI Window 生命周期独立。
- Tool Runtime 状态独立。
- Tool 数据目录独立。
- Tool 配置独立。
- Tool 只能存在一个窗口实例。

但不是：

- 每个 Tool 一个 OS 进程。

未来如果出现不可信插件或需要强隔离的 native 工作负载，再单独设计 child process。v1 不做。

---

# 4. 核心架构原则

## 4.1 Modular Monolith

juju 是模块化单体，不是动态插件平台。

新增工具采用：

```text
编写 Tool Module
+
注册 ToolDescriptor
```

不要设计：

- 动态加载 DLL。
- Rust ABI 插件。
- Web 插件市场。
- 第三方权限模型。

## 4.2 Tool Window 与 Tool Runtime 分离

必须区分：

```text
UI State
Runtime State
```

建议状态：

```rust
enum UiState {
    Closed,
    Opening,
    Visible,
    Minimized,
}

enum RuntimeState {
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}
```

后台能力：

```rust
enum BackgroundCapability {
    Unsupported,
    Optional,
    Required,
}
```

JSON：

```text
BackgroundCapability::Unsupported
```

未来可能：

```text
HTTP Download -> Optional
Ping Monitor  -> Optional
Clipboard Monitor -> Optional/Required
```

关闭窗口不等同于退出 juju。

## 4.3 后台允许与后台自启动分离

对于支持后台的未来工具，必须拆成两个配置：

```text
close_to_background
auto_start_runtime
```

含义分别为：

- `close_to_background`：关闭 UI 后 Runtime 是否继续。
- `auto_start_runtime`：juju 启动时是否自动启动 Runtime。

两者不能混成一个“允许后台运行”。

JSON 工具不展示这两个设置，因为其后台能力为 Unsupported。

---

# 5. Tool Registry 设计

## 5.1 ToolId

当前：

```rust
enum ToolId {
    Json,
}
```

Settings 不一定视为业务 Tool，可以由 `WindowManager` 单独管理；如果为了统一 Launcher，也可以注册为 SystemEntry，但不要把 Settings 的数据目录与普通 Tool 混在一起。

未来：

```rust
enum ToolId {
    Json,
    Http,
    Network,
    Encode,
}
```

## 5.2 ToolDescriptor

建议结构：

```rust
struct ToolDescriptor {
    id: ToolId,
    name: &'static str,
    launcher_key: LauncherKey,
    aliases: &'static [&'static str],
    route: &'static str,
    background: BackgroundCapability,
    window: WindowSpec,
}
```

`LauncherKey` 不建议直接存任意字符串，应定义成可验证类型，例如：

```rust
enum LauncherKey {
    Digit(u8),
    Letter(char),
}
```

发送给前端时转换为浏览器 `KeyboardEvent.code` 对应的逻辑值：

```text
Digit1
KeyS
```

避免 IME、Caps Lock、键盘布局造成不必要的字符语义影响。

JSON Descriptor：

```text
id: Json
name: JSON
launcher_key: Digit1
aliases: ["json", "格式化", "diff", "对比"]
background: Unsupported
default window: 1400 x 900
minimum: 900 x 600
```

ToolRegistry 必须是以下模块的唯一工具元数据来源：

- Launcher
- ToolManager
- Settings
- Tray
- 搜索功能（future）

不得分别维护工具列表。

---

# 6. 应用生命周期

## 6.1 启动流程

```text
juju.exe
  │
  ▼
Single Instance
  │
  ▼
加载 %LOCALAPPDATA%\juju\config.toml
  │
  ▼
配置迁移 / 校验
  │
  ▼
计算并校验 DataRoot
  │
  ▼
初始化 StorageManager
  │
  ▼
初始化 ToolRegistry
  │
  ▼
初始化 ToolManager / RuntimeManager
  │
  ▼
初始化 Tray
  │
  ▼
注册 Global Shortcut
  │
  ▼
启动 auto_start_runtime 的未来工具
  │
  ▼
后台驻留
```

默认启动后：

```text
无 Launcher
无 JSON WebView
无 Settings WebView
```

即没有窗口时，只保留 Rust Core + Tray + Global Shortcut。

## 6.2 单实例

必须启用 Tauri 2 single-instance 插件。

行为：

```text
第一个 juju.exe -> 正常运行
第二次启动 juju.exe -> 不创建第二个 Core
                      -> 通知已有实例
                      -> 已有实例打开 Launcher
```

未来可扩展命令行：

```text
juju.exe json
```

使已有实例直接打开 JSON Tool，但 v1 可以不提供 CLI。

## 6.3 开机启动

设置：

```text
开机自动启动 juju
默认：关闭
```

开启后使用 Tauri 2 autostart 插件。

开机自启场景：

```text
启动 Core
注册 Tray
注册 Leader Shortcut
不要自动弹 Launcher
不要自动打开 JSON
```

---

# 7. Leader Shortcut 与 Launcher

## 7.1 默认快捷键

语义：

```text
Ctrl + Shift + Alt + Space
```

实现使用 Tauri 2 `global-shortcut` 插件。

注意：具体快捷键字符串语法以当前插件版本解析器为准。AI 实现时必须根据当前官方文档/类型定义确认，不得仅凭记忆硬编码不存在的语法。

设置中必须：

- 显示当前快捷键。
- 支持修改。
- 修改时先验证。
- 新快捷键注册成功后再解除旧快捷键。
- 注册失败则保留旧快捷键。
- 明确显示“快捷键被其他程序占用/注册失败”。

## 7.2 Launcher 窗口

v1 设计：

- 小尺寸浮层。
- 无边框。
- 不显示在任务栏。
- 置顶。
- 显示在鼠标当前所在显示器。
- 1该显示器工作区居中。
- 获得键盘焦点。
- 默认超时 2000ms。
- `Esc` 关闭。
- 再次 Leader 关闭。
- 点击外部/失去焦点关闭（可做短延迟，避免刚 show 就收到 focus 变化）。
- 选择 Tool 后立即关闭 Launcher。

示意：

```text
┌─────────────────────────────────────┐
│                juju                 │
│                                     │
│      1    JSON                      │
│                                     │
│      S    Settings                  │
│                                     │
│        输入名称搜索（future）        │
└─────────────────────────────────────┘
```

第一版只展示单键入口，但 ToolDescriptor 必须提供 aliases，为未来搜索保留能力。

## 7.3 Launcher 状态机

```text
Idle
 │
 │ Leader
 ▼
Creating / Activating
 │
 │ Window ready + focus
 ▼
WaitModifiersReleased
 │
 │ Ctrl/Shift/Alt 全部释放
 ▼
Armed
 ├── Digit1 -> Open JSON -> Close
 ├── KeyS   -> Open Settings -> Close
 ├── Escape -> Close
 ├── Leader -> Close
 ├── Timeout -> Close
 └── Invalid Key -> 可忽略，不立即关闭
```

必须忽略：

```javascript
event.repeat === true
```

在 `WaitModifiersReleased` 之前不要处理单键命令。

第一版第二阶段按键使用：

```javascript
KeyboardEvent.code
```

而不是依赖字符 `event.key`。

搜索模式未来启用后，再切换成正常文本输入和 IME 模式。

## 7.4 Lazy Launcher

默认采用 Lazy WebView：

```text
平时无 Launcher WebView
Leader -> create -> ready -> show/focus
操作结束 -> destroy
```

原因：减少长期 WebView2 内存占用。

必须做性能埋点：

```text
Leader triggered
Launcher WebView created
React mounted
Launcher ready for keyboard
```

如果实测冷启动体验明显不可接受，可在后续切换为 Warm Launcher（隐藏常驻），但 `LauncherManager` 接口必须让两种策略可替换。

---

# 8. 多显示器与窗口管理

## 8.1 WindowManager

所有动态窗口由 Rust `WindowManager` 统一创建和恢复。

不要在前端自行创建 Tool Window。

窗口 label 建议：

```text
launcher
settings
tool-json
```

未来：

```text
tool-http
tool-network
```

## 8.2 Tool 窗口单实例

统一调用：

```text
ToolManager.open(Json)
```

逻辑：

```text
不存在
 -> 创建
 -> 恢复合法窗口位置
 -> 等前端 ready
 -> show + focus

已存在且隐藏
 -> show + focus

已最小化
 -> restore + focus

已可见
 -> focus
```

严禁创建第二个 `tool-json`。

## 8.3 窗口位置恢复

每个窗口单独保存：

```text
x
y
width
height
maximized
monitor information（必要时）
```

窗口状态属于本机 UI 状态，保存于：

```text
%LOCALAPPDATA%\juju\window-state.toml
```

不放入 DataRoot。

恢复时必须验证：

- 历史矩形是否仍与任意显示器工作区相交。
- 显示器是否已经移除。
- DPI/缩放变化后窗口是否仍可见。
- 无效时回退到鼠标当前显示器居中。

新建窗口：

```text
历史状态合法 -> 恢复
否则 -> 鼠标当前所在显示器工作区居中
```

再次唤醒已经存在的 Tool Window：

```text
只 restore/show/focus
不要擅自移动到当前鼠标显示器
```

## 8.4 Launcher 显示器

优先使用 Windows API 封装：

```text
GetCursorPos
MonitorFromPoint
GetMonitorInfoW
```

在 `platform/windows/monitor.rs` 内实现。

注意逻辑坐标与物理像素的 DPI 转换，不要在业务层混用。

---

# 9. Tray 设计

juju 没有传统 Main Window，Tray 是常驻入口之一。

Tray 菜单：

```text
打开启动器
JSON
设置
----------------
退出 juju
```

未来可增加：

```text
后台任务
暂停全局快捷键
```

点击 Tray 图标的默认行为可设为打开 Launcher。

`X` 关闭 Tool Window 不退出 juju。

只有：

```text
Tray -> 退出 juju
```

才真正结束进程。

---

# 10. 退出流程

退出必须由 AppLifecycle 统一控制：

```text
is_exiting = true
  ↓
停止接受 Launcher 请求
  ↓
取消全局快捷键
  ↓
flush 所有待保存数据
  ↓
请求 RuntimeManager 停止后台任务
  ↓
等待 graceful shutdown timeout
  ↓
销毁 WebViews / Windows
  ↓
退出进程
```

JSON v1 没有后台 Runtime，但仍必须遵守此流程。

未来如果有不能立即中断的重要任务，可在退出时提示，但 v1 不需要做复杂后台任务确认。

---

# 11. 配置与数据存储

## 11.1 固定应用配置目录

应用级、本机级配置固定放：

```text
%LOCALAPPDATA%\juju\
```

建议结构：

```text
%LOCALAPPDATA%\juju\
├── config.toml
├── window-state.toml
├── logs\
├── cache\
│   └── clipboard\
└── runtime\
```

这里不是用户工具数据目录。

## 11.2 默认 DataRoot

默认：

```text
%USERPROFILE%\.juju
```

即：

```text
C:\Users\<user>\.juju
```

注意：

- 必须通过系统 API/Tauri path resolver 获取用户目录。
- 不要通过字符串猜测 `C:\Users\...`。
- 用户可以在 Settings 中修改 DataRoot。

## 11.3 config.toml

建议：

```toml
schema_version = 1

data_root = "C:\\Users\\example\\.juju"

[app]
autostart = false

[launcher]
timeout_ms = 2000
shortcut = "<canonical shortcut representation>"

[storage]
watch_external_changes = true
autosave_debounce_ms = 800
```

快捷键的实际序列化格式由实现层定义，只需保证：

- 可读。
- 可验证。
- 可迁移。
- UI 可以显示为 `Ctrl + Shift + Alt + Space`。

所有配置更新使用原子写。

## 11.4 DataRoot 结构

```text
~\.juju\
│
├── juju.toml
│
└── json\
    ├── documents\
    │   ├── 未命名-1.json
    │   ├── 支付请求.json
    │   └── 测试数据.json
    │
    ├── state.toml
    │
    └── .trash\
```

`juju.toml`：

```toml
data_schema_version = 1
```

用于识别这是一个 juju DataRoot，并为未来数据迁移提供版本。

当前不使用数据库。

原则：

> 用户文档是事实数据源。任何索引都只能是可删除、可重建 Cache。

## 11.5 JSON 文档存储原则

每一条 JSON 记录就是一个普通 `.json` 文件：

```text
json/documents/<name>.json
```

不把文档内容嵌入：

```text
state.toml
SQLite
二进制索引
```

用户应可以：

- 直接打开。
- 直接复制。
- 用 VS Code 编辑。
- 用 Git 备份。
- 用任意文件同步软件同步。

`state.toml` 只保存 UI/工具状态，例如：

```toml
last_opened = "支付请求.json"
sort = "modified_desc"
```

不要把用户 JSON 内容写入 state。

---

# 12. DataRoot 切换与迁移

Settings 中提供：

```text
数据目录
C:\Users\xxx\.juju

[打开目录]
[使用已有目录]
[迁移到新目录]
```

必须区分两个操作。

## 12.1 使用已有目录

```text
用户选择目录
  ↓
检查 juju.toml
  ↓
检查数据 schema
  ↓
flush 当前工具
  ↓
切换 StorageManager root
  ↓
更新 config.toml
  ↓
刷新 Tool 数据
```

如果目录为空，可询问/允许初始化为新的 DataRoot。

如果目录含有普通文件但不是 juju root，不要静默覆盖。

## 12.2 迁移到新目录

```text
flush 所有工具
  ↓
暂停 watcher
  ↓
创建目标目录
  ↓
复制 DataRoot
  ↓
校验关键文件
  ↓
写目标 juju.toml
  ↓
切换 config
  ↓
重新初始化 watcher
```

迁移成功前不得删除源目录。

v1 可以采用：

```text
复制成功 -> 切换
源目录保留
```

并提示用户自行删除旧目录，降低迁移导致的数据丢失风险。

## 12.3 DataRoot 不可用

例如：

- 移动硬盘未连接。
- 权限丢失。
- 路径被删除。

不得静默回落到默认 `~/.juju`，否则会形成两份数据。

应显示：

```text
数据目录不可用

[重试]
[重新选择]
[打开设置]
```

Core、Tray、Launcher 可以继续工作。

---

# 13. StorageManager

## 13.1 职责

所有持久化写操作必须通过 Rust StorageManager。

Tool 不允许直接散落：

```rust
std::fs::write(...)
```

StorageManager 负责：

- DataRoot。
- 路径安全。
- 原子写。
- 文档枚举。
- 文档读写。
- revision。
- external file watcher。
- 数据 schema。
- 数据迁移。
- self-write 去重。
- 冲突检测。

## 13.2 路径安全

前端不得传入任意绝对路径访问磁盘。

JSON API 的 DocumentId 可以直接映射为“受验证的文件名”，但必须满足：

- 仅允许文件名，不允许目录分隔符。
- 不允许 `..`。
- 必须 `.json`。
- 拒绝 Windows reserved device names。
- 最终 canonical/safe parent 必须是当前 `json/documents`。
- 所有 rename/create 统一做文件名校验。

建议 Rust 定义：

```rust
struct JsonDocumentId(String);
```

构造函数负责校验。

## 13.3 新建文件命名

默认：

```text
未命名-1.json
未命名-2.json
未命名-3.json
```

生成时扫描冲突。

重命名冲突默认返回：

```text
DOCUMENT_ALREADY_EXISTS
```

不要自动覆盖已有文档。

## 13.4 原子写

禁止直接 truncate + write 最终文件。

要求：

```text
目标同目录创建临时文件
  ↓
写完整内容
  ↓
flush
  ↓
使用 Windows 安全替换策略替换目标
```

实现时优先使用可靠的 Windows 原子/近原子替换方式；如果使用 crate，必须确认 Windows 覆盖已有文件的语义。

目标：

- 应用崩溃不留下半个 JSON 文件。
- 临时文件留存可在下次启动清理。
- 写失败时保留旧文件。

## 13.5 自动保存

默认：

```text
800ms debounce
```

流程：

```text
Monaco content changed
  ↓
React dirty state
  ↓
800ms 无新输入
  ↓
json_write_document
  ↓
atomic write
```

窗口关闭/切换 DataRoot/退出进程前必须：

```text
flush pending autosave
```

JSON 可以处于暂时非法状态，仍然允许自动保存当前文本，因为该工具同时承担 JSON 文本草稿功能。

因此：

- “保存当前文本”和“JSON 是否合法”是两个概念。
- 不因非法 JSON 阻止自动保存。
- 格式化/压缩需要合法 JSON。
- 文件复制允许非法 JSON。

---

# 14. 外部文件修改

用户明确可以直接在 VS Code 等应用中编辑 DataRoot 文档，因此 external change 是正常场景。

使用 Rust `notify` crate 监听：

```text
<DataRoot>\json\documents
```

## 14.1 Revision

读取文档时返回：

```text
content
revision
```

revision 可由：

```text
mtime + size + hash（按实现需要）
```

组成。

写入请求：

```text
document_id
content
expected_revision
```

如果磁盘 revision 已变化：

```text
返回 EXTERNAL_MODIFICATION_CONFLICT
```

不要直接覆盖。

## 14.2 UI 冲突处理

Monaco 当前无未保存变化：

```text
外部修改
 -> 自动重新加载
 -> toast: 文件已在外部更新
```

Monaco 当前有 dirty 内容：

```text
外部修改
 -> 不覆盖编辑器
 -> 显示冲突提示
```

提供：

```text
[重新加载磁盘版本]
[覆盖磁盘版本]
[复制当前内容后重新加载]
```

v1 至少实现前两个。

## 14.3 Watcher 去重

juju 自己 atomic write 也会触发 watcher。

StorageManager 需要维护短期 self-write revision/operation token，避免自己的保存被误报成“外部冲突”。

Watcher 事件需要 debounce，因为 Windows 文件更新可能产生多次 create/rename/write 事件。

---

# 15. JSON Tool 产品设计

## 15.1 窗口布局

```text
┌──────────────────────────────────────────────────────────────┐
│ 新建 | 格式化 | 复制 | 压缩复制 | 文件复制 | 展开 | 折叠 ... │
├──────────────┬───────────────────────────────────────────────┤
│ 文档         │                                               │
│              │                                               │
│ 支付请求     │               Monaco Editor                   │
│ 测试数据     │                                               │
│ 未命名-1     │                                               │
│              │                                               │
├──────────────┴───────────────────────────────────────────────┤
│ JSON 状态 / 行列 / 自动保存状态                               │
└──────────────────────────────────────────────────────────────┘
```

左侧：

- 文档列表。
- 当前选中状态。
- 文件名。
- 可选搜索框（v1 可实现，成本很低）。
- Context Menu：
  - 重命名。
  - 删除。

工具栏：

```text
新建
格式化
复制
压缩复制
文件复制
展开
折叠
逐层展开
对比
```

不提供“保存”和“下载”。

## 15.2 Monaco 基础配置

要求：

- language: `json`
- 行号开启。
- folding 开启。
- minimap 默认关闭。
- automatic layout。
- 支持 Ctrl+F。
- 支持编辑器 Undo/Redo。
- 支持 JSON syntax diagnostics。
- 默认 2 空格缩进。
- `scrollBeyondLastLine` 关闭。
- 大文档时避免不必要 decorations。
- Monaco 只在 JSON Tool 模块加载时 dynamic import。

必须在 React unmount 时：

- dispose editor。
- dispose model。
- Diff Editor 同样 dispose。
- 避免反复打开 JSON 工具导致 model 泄漏。

---

# 16. JSON 功能语义

## 16.1 新建

点击：

```text
新建
```

Rust 创建：

```text
未命名-N.json
```

默认内容：

```json
{}
```

创建后：

- 添加到列表。
- 选中。
- 打开 Monaco。
- 聚焦编辑器。

## 16.2 普通复制

语义：

```text
复制当前编辑器原始文本
```

不要求 JSON 合法。

通过 Rust `ClipboardService.copy_text()` 实现，避免不同 WebView clipboard 权限差异。

成功显示短暂 toast：

```text
已复制
```

## 16.3 压缩复制

要求：

1. 先验证 JSON 合法。
2. **不允许通过 parse -> Value -> serialize 的方式改变数字字面量或重复 key。**
3. 对原始文本执行 lexical minify：仅删除字符串字面量外允许忽略的 JSON whitespace。

例如：

```json
{
  "value": 1e10,
  "text": "a b"
}
```

结果必须保持 token：

```json
{"value":1e10,"text":"a b"}
```

不能把 `1e10` 重新序列化为其他数字表示。

建议算法：

```text
先使用 serde_json 仅做语法验证
再扫描原始 UTF-8 文本
state:
  Normal
  InString
  Escape
Normal 下删除 space/tab/CR/LF
String 内原样保留
```

如果 JSON 非法：

- 不改变剪切板。
- 显示错误。
- 尽量定位 Monaco 中的错误位置。

## 16.4 文件复制

语义：

> 将当前 JSON 文档以 `.json` 文件对象放入 Windows 剪切板，用户在 Explorer / Desktop 中 `Ctrl+V` 可得到文件。

要求：

1. 先 flush 当前 autosave。
2. 如果当前 document 已有实体文件且内容与编辑器一致，可直接使用其路径。
3. 为避免用户后续修改源文档导致“复制内容”变化，**推荐生成 clipboard snapshot**。
4. snapshot 放：

```text
%LOCALAPPDATA%\juju\cache\clipboard\
```

例如：

```text
%LOCALAPPDATA%\juju\cache\clipboard\支付请求.json
```

5. 使用 Windows `CF_HDROP` / `DROPFILES` 把 snapshot 路径放入 Clipboard。
6. 设置 Copy 语义，不是 Move。
7. snapshot 不得复制到 DataRoot。

缓存清理：

- juju 启动时清理 3 天以上的 clipboard snapshot。
- 不得复制后立即删除，因为 Explorer 的剪切板数据依赖路径存在。

非法 JSON 也允许文件复制，因为它复制的是当前文本快照。

Windows 实现位置：

```text
platform/windows/clipboard.rs
```

不要在 JSON Tool 直接调用 Win32。

## 16.5 格式化

要求：

- 只在合法 JSON 上执行。
- 默认缩进 2 空格。
- 尽量保持 JSON token/value 语义与原始 key 顺序。
- 优先使用 Monaco JSON formatter 的公开能力。
- 不使用 Monaco internal/private folding/parser API。
- 格式化完成后，普通 autosave 流程负责持久化。

非法 JSON：

```text
不修改文档
显示“JSON 格式错误”
聚焦/定位第一处错误（能获取位置时）
```

## 16.6 折叠

执行 Monaco 支持的公开 Fold All action：

```text
折叠所有可折叠 JSON 节点
```

## 16.7 展开

执行 Monaco 支持的公开 Unfold All action：

```text
展开所有节点
```

## 16.8 逐层展开

产品语义固定为：

> 从较浅 JSON 层级开始，每点击一次增加一层可见深度。

建议状态：

```text
foldDepth = 1, 2, 3 ...
```

实现策略优先使用 Monaco/VS Code 公开可触发的 folding actions，例如 fold level 系列；不要访问 Monaco 内部 folding model 私有字段。

如果当前 Monaco 稳定版本仅提供有限 fold level action：

- 支持公开提供的最大层级。
- 超过最大层级后执行全部展开。
- 每次用户手工改变 folding 后，可以重置 step-expand 状态。

该功能需要单独写集成测试验证实际层级行为，不要假设 action 语义。

---

# 17. JSON Diff

## 17.1 进入方式

正常模式点击：

```text
对比
```

进入“选择对比文档”状态。

当前文档固定为：

```text
Original / Left
```

用户在左侧选择另一个文档：

```text
Modified / Right
```

然后切换到 Monaco DiffEditor。

禁止选择当前文档自身。

## 17.2 Diff UI

```text
┌──────────────┬──────────────────────┬──────────────────────┐
│ 文档列表      │ Original             │ Modified             │
│              │ 当前文档              │ 对比文档              │
│              │                      │                      │
│              │      Monaco DiffEditor                      │
│              │                      │                      │
└──────────────┴──────────────────────┴──────────────────────┘
```

Diff 默认：

- side-by-side。
- 两边只读。
- 显示行级、字符级变化。
- 支持同步滚动。
- 支持折叠未变化区域（如果当前 Monaco 公开配置支持且体验稳定，可开启；否则 v1 不强求）。

工具栏：

```text
退出对比
交换左右
```

## 17.3 Diff 输入规范

优先可读性：

- 如果两份文档都是合法 JSON：
  - 为 Diff 创建**临时格式化 projection**。
  - 不修改磁盘原文件。
  - 不修改普通编辑器内容。
- 如果任意一份非法：
  - 回退为原始文本 Diff。
  - 显示“存在非法 JSON，当前按原文比较”。

v1 是文本 Diff，不做：

```text
忽略属性顺序
JSON Pointer 语义 Diff
array key match
JSON Patch
```

这些以后可单独增加“语义对比模式”。

---

# 18. JSON 文档删除与重命名

虽然不是最核心按钮，但文档列表需要基本管理能力。

## 18.1 重命名

- 保留 `.json` 扩展。
- UI 输入只编辑主文件名。
- Rust 校验 Windows 文件名。
- 冲突返回明确错误。
- 成功后更新当前 DocumentId。
- 如果当前正在 Diff，重命名不能破坏已打开模型。

## 18.2 删除

为降低误删风险，v1 不建议永久删除。

移动到：

```text
<DataRoot>\json\.trash\
```

建议文件名：

```text
20260910-113000__支付请求.json
```

同名冲突追加序号。

v1 UI：

```text
删除确认
```

删除后：

- 关闭该文档。
- 选中列表中的下一条。
- 列表为空时自动创建 `未命名-1.json` 或显示空状态；推荐自动创建一条，保证编辑器始终可用。

恢复回收站功能可以以后增加。

---

# 19. React 前端架构

## 19.1 不使用大型 SPA 路由作为核心状态

可以只保留一个 Vite build，通过 window URL/query 决定入口。

概念：

```text
/index.html?view=launcher
/index.html?view=settings
/index.html?view=tool&tool=json
```

`main.tsx`：

```text
resolve current view
  ├── launcher -> dynamic import LauncherApp
  ├── settings -> dynamic import SettingsApp
  └── tool=json -> dynamic import JsonToolApp
```

JSON module 再：

```text
dynamic import monaco-editor
```

这样 Launcher 不会加载 Monaco。

如果实际 Tauri asset routing 对 query 有不便，可改为 Vite multi-page：

```text
launcher.html
settings.html
tool.html
```

两种方式均可，但必须保持 lazy loading。优先选实现最简单且在 Tauri 2 中稳定的方式。

## 19.2 推荐目录

```text
src/
├── main.tsx
├── app/
│   ├── ipc.ts
│   ├── errors.ts
│   ├── types.ts
│   └── theme.ts
│
├── launcher/
│   ├── LauncherApp.tsx
│   ├── launcher.module.css
│   └── useLauncherKeyboard.ts
│
├── settings/
│   ├── SettingsApp.tsx
│   ├── GeneralSettings.tsx
│   ├── StorageSettings.tsx
│   └── settings.module.css
│
├── tools/
│   └── json/
│       ├── JsonToolApp.tsx
│       ├── JsonToolbar.tsx
│       ├── DocumentSidebar.tsx
│       ├── JsonEditor.tsx
│       ├── JsonDiff.tsx
│       ├── JsonStatusBar.tsx
│       ├── hooks/
│       │   ├── useDocuments.ts
│       │   ├── useAutosave.ts
│       │   ├── useExternalChanges.ts
│       │   └── useMonaco.ts
│       └── json.module.css
│
└── shared/
    ├── Button.tsx
    ├── Dialog.tsx
    ├── Toast.tsx
    ├── EmptyState.tsx
    └── styles/
        ├── tokens.css
        └── global.css
```

## 19.3 React 状态原则

不要在 WebView 中保存跨启动的事实状态。

React state 只承担：

- 当前 UI selection。
- dialog 开关。
- editor dirty。
- compare mode。
- 临时 toast。
- Monaco instance references。

Rust/Core/文件系统承担：

- 文档数据。
- settings。
- window state。
- data root。
- tool registry。
- runtime state。

---

# 20. Rust 工程结构

建议：

```text
src-tauri/
├── Cargo.toml
├── tauri.conf.json
├── capabilities/
│   ├── launcher.json
│   ├── settings.json
│   └── json.json
│
└── src/
    ├── main.rs
    ├── lib.rs
    │
    ├── app/
    │   ├── mod.rs
    │   ├── lifecycle.rs
    │   ├── launcher.rs
    │   ├── tray.rs
    │   └── commands.rs
    │
    ├── core/
    │   ├── mod.rs
    │   ├── registry.rs
    │   ├── tool_manager.rs
    │   ├── runtime_manager.rs
    │   ├── window_manager.rs
    │   ├── settings.rs
    │   └── app_state.rs
    │
    ├── services/
    │   ├── mod.rs
    │   ├── storage/
    │   │   ├── mod.rs
    │   │   ├── atomic_write.rs
    │   │   ├── watcher.rs
    │   │   └── revision.rs
    │   ├── notification.rs
    │   ├── secret_store.rs
    │   └── logging.rs
    │
    ├── platform/
    │   ├── mod.rs
    │   └── windows/
    │       ├── mod.rs
    │       ├── clipboard.rs
    │       ├── monitor.rs
    │       ├── credentials.rs   # future stub
    │       └── privilege.rs     # future stub
    │
    └── tools/
        ├── mod.rs
        └── json/
            ├── mod.rs
            ├── commands.rs
            ├── documents.rs
            ├── minify.rs
            └── types.rs
```

v1 不拆 Cargo workspace。

等工具数量明显增长，再考虑：

```text
juju-core
juju-platform-windows
juju-http-engine
...
```

现在不要提前拆 crate。

---

# 21. AppState

Tauri Managed State 建议集中：

```rust
struct AppState {
    settings: Arc<SettingsService>,
    storage: Arc<StorageManager>,
    registry: Arc<ToolRegistry>,
    tools: Arc<ToolManager>,
    runtimes: Arc<RuntimeManager>,
    windows: Arc<WindowManager>,
    launcher: Arc<LauncherManager>,
    notifications: Arc<dyn NotificationService>,
}
```

具体实现需考虑 Tauri Send/Sync 要求。

避免：

- 大量全局 `static mut`。
- 每个 command 自己构造 service。
- 业务模块直接持有 AppHandle 作为万能依赖。

AppHandle 只在确实需要窗口/event/plugin 的 service 中持有或按调用传入。

---

# 22. RuntimeManager

虽然 JSON v1 不需要后台任务，但需要保留统一模型。

接口概念：

```rust
trait ToolRuntime {
    fn tool_id(&self) -> ToolId;
    async fn start(&self) -> Result<()>;
    async fn stop(&self) -> Result<()>;
    fn state(&self) -> RuntimeState;
}
```

JSON 不实现实际 Runtime，ToolManager 根据：

```text
BackgroundCapability::Unsupported
```

直接管理 UI 生命周期。

未来后台 task 需要：

- CancellationToken 或等价取消机制。
- TaskId/SessionId。
- graceful stop。
- runtime event。
- UI destroy 后 Rust task 可继续。
- UI 重新打开后可查询 snapshot。

注意：

> Tool Singleton != Operation Singleton。

未来 HTTP 工具窗口只有一个，但内部可以存在多个请求/tab/task。

---

# 23. Notification / Secret / Privilege 预留

## 23.1 NotificationService

v1：

```rust
trait NotificationService {
    fn send(&self, notification: Notification) -> Result<()>;
}
```

可提供：

```text
NoopNotificationService
```

后续替换成 Tauri/Windows Notification。

业务工具不得直接依赖某个 notification plugin。

## 23.2 SecretStore

v1 JSON 不使用，只定义边界或留 TODO 模块。

未来 HTTP：

```text
Authorization token
Cookie
API key
```

不能被迫明文写入可 Git 的请求文档。

未来 Windows 实现：

```text
Windows Credential Manager
```

HTTP 文档可引用：

```text
${secret:name}
```

## 23.3 PrivilegeService

主 `juju.exe` 默认永不以管理员运行。

未来网络工具需要管理员权限时，使用：

```text
juju-helper.exe
```

按需提权。

v1 不实现 helper，只保留平台边界。

---

# 24. IPC 设计

## 24.1 原则

- 前端不能获得任意文件系统能力。
- 所有 Tool 操作使用窄 API。
- Command 名称表达业务语义，不暴露通用 `read_any_file(path)`。
- Rust command 返回统一错误结构。
- serde 字段统一 `camelCase` 或统一 snake_case；建议 IPC 使用 `camelCase`。

## 24.2 建议 Commands

App：

```text
app_get_settings
app_update_settings
app_get_tool_manifest
app_change_shortcut
app_set_autostart
app_get_data_root
app_use_existing_data_root
app_migrate_data_root
app_open_data_root
app_exit
```

Tool：

```text
tool_open
tool_close
```

Window ready：

```text
window_ready
```

用于 React mount 完成后通知 Rust，再 show 窗口，减少白屏闪烁。

JSON：

```text
json_list_documents
json_create_document
json_read_document
json_write_document
json_rename_document
json_delete_document
json_copy_text
json_copy_minified
json_copy_file
```

格式化、fold/unfold、Diff UI 优先在 Monaco/前端完成，不需要每次 IPC。

## 24.3 统一错误

建议：

```rust
struct ApiError {
    code: String,
    message: String,
    details: Option<serde_json::Value>,
}
```

稳定错误码示例：

```text
INVALID_JSON
DOCUMENT_NOT_FOUND
DOCUMENT_ALREADY_EXISTS
INVALID_DOCUMENT_NAME
EXTERNAL_MODIFICATION_CONFLICT
DATA_ROOT_UNAVAILABLE
DATA_ROOT_INVALID
CLIPBOARD_FAILED
SHORTCUT_REGISTRATION_FAILED
STORAGE_IO_ERROR
```

前端判断 `code`，不要解析错误字符串。

Rust 内部使用 typed error，例如 `thiserror`，边界统一转换为 ApiError。

---

# 25. Events

需要主动通知前端的场景使用事件。

建议：

```text
app://settings-changed
app://data-root-changed

json://external-change
json://document-list-changed

tool://runtime-state
```

Payload 必须结构化。

例如：

```json
{
  "documentId": "支付请求.json",
  "revision": "...",
  "kind": "modified"
}
```

不要通过事件发送大型 JSON 文档全文；收到事件后前端再显式读取。

---

# 26. Tauri Capability / Permission

必须使用 Tauri 2 capability 做最小权限。

建议：

```text
capabilities/launcher.json
capabilities/settings.json
capabilities/json.json
```

Launcher 只需要：

```text
读取 Tool Manifest
打开 Tool
关闭 Launcher / ready
```

Settings：

```text
读取/更新 settings
修改 data root
autostart
shortcut
打开 data root
```

JSON：

```text
JSON 文档 commands
clipboard commands
自身 window 基础操作
```

不要给任何 WebView：

```text
任意 fs
任意 shell
任意 command
```

如果 `app_open_data_root` 需要调用 Explorer，应由 Rust command 做固定、安全行为，而不是让前端拥有任意 shell execute。

---

# 27. ClipboardService

统一接口：

```rust
trait ClipboardService {
    fn copy_text(&self, text: &str) -> Result<()>;
    fn copy_file(&self, path: &Path) -> Result<()>;
}
```

Windows 实现：

```text
platform/windows/clipboard.rs
```

`copy_file` 使用：

```text
CF_HDROP
DROPFILES
HGLOBAL
```

并明确是 Copy。

实现必须处理：

- `OpenClipboard` 失败。
- Clipboard 被其他程序暂时占用。
- Unicode 路径。
- `CloseClipboard`。
- Global memory ownership 转移。
- 失败路径释放资源。

可以对 Clipboard busy 做非常有限的短重试，但不要无限阻塞 UI。

---

# 28. 设置 UI

v1 Settings 页面至少包含：

## 常规

```text
开机自动启动                 [off]
全局唤醒快捷键               [Ctrl + Shift + Alt + Space]
Launcher 自动关闭时间         [2000 ms]
```

快捷键修改流程必须有录制/确认状态。

## 数据

```text
数据目录
C:\Users\xxx\.juju

[打开目录]
[使用已有目录]
[迁移到新目录]
```

显示：

```text
数据目录状态：正常 / 不可用 / 需要迁移
```

## JSON

可选 v1 配置：

```text
缩进宽度：2
自动保存延迟：800 ms
```

如果不希望第一版暴露过多设置，可固定这些默认值，仅保留内部配置能力。

---

# 29. 日志

建议 Rust 使用结构化日志：

```text
tracing
tracing-subscriber
```

输出：

```text
%LOCALAPPDATA%\juju\logs\
```

日志内容可包含：

- app lifecycle。
- window create/destroy。
- global shortcut registration。
- data root switch。
- storage error。
- watcher event summary。
- clipboard error。
- panic summary。

禁止记录：

- 完整 JSON 文档正文。
- 未来 HTTP Authorization。
- Cookie。
- Secret。
- 用户剪切板正文。

Release 默认日志级别：

```text
INFO
```

可通过开发配置提升 DEBUG。

---

# 30. 性能原则

## 30.1 最重要的资源优化

当所有工具窗口关闭时：

```text
不要保留隐藏 JSON WebView
不要保留隐藏 Monaco
默认不要保留 Launcher WebView
```

Core 继续驻留：

```text
Rust + Tray + Shortcut + Watcher
```

JSON 关闭时：

```text
flush
dispose Monaco
destroy WebView Window
```

## 30.2 Monaco

必须：

- dynamic import。
- dispose models。
- 不对整个文档每次键入重新构造 decorations。
- autosave debounce。
- Diff 退出时 dispose diff models。
- 大 JSON 不做同步深度昂贵操作。

## 30.3 性能指标

第一阶段记录而不是盲目优化：

```text
process working set
Launcher cold activation latency
Launcher warm activation latency
JSON window cold open
Monaco ready
10MB JSON load
10MB JSON format
Diff 5MB + 5MB
```

建议体验目标：

- Leader 热路径应尽可能接近即时。
- JSON 1MB 文档编辑不得明显卡顿。
- 大文件超过安全阈值时允许提示关闭部分增强能力。

不要在没有 profile 的情况下为了“Rust 更快”做复杂 premature optimization。

---

# 31. 大文件策略

Monaco 可以处理较大文本，但 JSON 格式化和 Diff 成本可能显著增长。

v1 建议定义软阈值，例如：

```text
10 MB：提示“大文件模式”
```

阈值做配置常量，不一定暴露设置。

大文件模式可：

- 保留编辑。
- 保留复制/文件复制。
- 格式化前二次确认。
- Diff 前提示。
- 禁止不必要的实时全文操作。

不要因为文件大直接拒绝打开。

后续根据实测再调整阈值。

---

# 32. 错误与恢复

## 32.1 Rust panic

关键原则：

- 普通业务错误必须返回 `Result`，不 panic。
- `.unwrap()` / `.expect()` 只允许在“绝不可能失败且有清晰 invariant”的初始化点使用。
- 文件 IO、clipboard、window、shortcut、watcher 都必须处理错误。

## 32.2 WebView 崩溃

某个 Tool WebView 失效时：

- Core 仍驻留。
- ToolManager 应允许 destroy 后重新创建。
- 不让 stale window handle 阻止重新打开。

## 32.3 未完成临时文件

juju 启动时清理：

```text
%LOCALAPPDATA%\juju\cache\clipboard\ 中过期文件
Storage atomic write 遗留 temp 文件
```

清理失败只记录日志，不阻止启动。

---

# 33. Security 基线

即使这是个人本地工具，也执行以下约束：

1. WebView 不获得通用磁盘读写。
2. Tool command 最小权限。
3. DataRoot path canonicalization。
4. 禁止 path traversal。
5. 未来 HTTP 响应 HTML 不直接注入带 Tauri IPC 权限的 DOM。
6. 未来 Secret 不明文混入可同步 DataRoot。
7. 主进程不默认管理员运行。
8. 前端输入均视为不可信，Rust command 重新校验。
9. 不记录用户 JSON 正文到 log。
10. Tauri capability 按窗口拆分。

---

# 34. Build / Release

## 34.1 开发环境

Windows x64：

```text
Rust stable + MSVC target
Node.js 当前 LTS
pnpm
Visual Studio Build Tools / Windows SDK
WebView2 Runtime
```

具体最低 Rust 版本以所使用的当前 Tauri 2 及插件要求为准。不要为了追求最低版本固定到过旧 toolchain。

## 34.2 Debug

```text
pnpm install
pnpm tauri dev
```

具体脚本以初始化项目为准。

## 34.3 Release

```text
pnpm tauri build
```

发布目标：

```text
Windows x64 installer
```

安装包策略优先选择 Tauri 官方稳定支持的一种 Windows bundle（如 NSIS）；具体配置使用当前 Tauri 2 schema。

必须在“干净 Windows VM”测试：

- WebView2 Runtime 缺失/存在。
- 安装。
- 首次启动。
- Tray。
- Shortcut。
- Autostart。
- 卸载。

不要假设所有 Windows 环境都天然具备正确 WebView2 Runtime。

---

# 35. 测试策略

## 35.1 Rust 单元测试

必须覆盖：

- JsonDocumentId 文件名校验。
- Windows reserved name。
- path traversal 拒绝。
- minify 保留字符串 whitespace。
- minify 保留数字 token。
- invalid JSON 拒绝 minify。
- unique untitled name。
- config migration。
- data root validation。
- revision conflict。
- atomic write error path。
- ToolRegistry key 冲突检测。

## 35.2 Storage 集成测试

使用临时目录：

```text
create
read
write
rename
delete -> .trash
external modify
conflict
switch data root
```

测试不能污染真实：

```text
~\.juju
```

## 35.3 Frontend 测试

建议使用：

```text
Vitest
React Testing Library
```

覆盖：

- Launcher keyboard state machine。
- modifier release。
- repeat key。
- timeout。
- JSON compare selection state。
- autosave debounce。
- external conflict UI。
- toolbar enable/disable。

## 35.4 手工 Windows 验收

重点：

- 多显示器。
- 100% / 125% / 150% DPI。
- 快捷键被其他程序占用。
- Clipboard 被占用。
- Explorer 文件粘贴。
- VS Code 外部编辑。
- DataRoot 移动硬盘断开。
- 第二次启动 juju.exe。
- 开机启动。
- 从 Tray 退出。
- JSON 窗口反复开关 50 次观察内存。

---

# 36. v1 验收标准

## juju Core

- [ ] juju 同一时间只有一个进程实例。
- [ ] 没有 Tool Window 时 juju 仍在 Tray 运行。
- [ ] 第二次启动 juju 会唤起 Launcher。
- [ ] Tray 可以打开 JSON、Settings、Launcher。
- [ ] Tray 退出可以彻底结束 juju。

## Leader / Launcher

- [ ] 默认 Leader 为 Ctrl + Shift + Alt + Space。
- [ ] Leader 可以从其他普通应用中唤起 Launcher。
- [ ] Launcher 位于鼠标所在显示器。
- [ ] Launcher 获得焦点。
- [ ] `1` 打开 JSON。
- [ ] `S` 打开 Settings。
- [ ] `Esc` 关闭。
- [ ] 2 秒超时关闭。
- [ ] 不因 modifier key release 误触发命令。
- [ ] 长按按键不重复创建工具窗口。
- [ ] 快捷键冲突能提示。

## Window

- [ ] JSON 只有一个窗口实例。
- [ ] 已存在时 Leader+1 只 focus/restore。
- [ ] 记录每个 Tool Window 位置和大小。
- [ ] 显示器拔除后窗口不会恢复到屏幕外。
- [ ] JSON `X` 关闭 WebView，不退出 juju。

## Storage

- [ ] 默认 DataRoot 是 `%USERPROFILE%\.juju`。
- [ ] DataRoot 中 JSON 是普通 `.json` 文件。
- [ ] 不使用数据库。
- [ ] Settings 可打开 DataRoot。
- [ ] 可切换已有 DataRoot。
- [ ] 可迁移 DataRoot。
- [ ] DataRoot 不可用时不静默创建第二份默认数据。
- [ ] 保存使用安全 atomic write。
- [ ] 外部修改可检测。
- [ ] 编辑冲突不会静默覆盖外部版本。

## JSON Tool

- [ ] 新建 JSON。
- [ ] 文档列表。
- [ ] 自动保存。
- [ ] Monaco JSON 高亮。
- [ ] Monaco syntax diagnostics。
- [ ] 格式化。
- [ ] 普通复制。
- [ ] 压缩复制。
- [ ] 文件复制后可在 Explorer `Ctrl+V` 得到 `.json`。
- [ ] 非法 JSON 可普通复制和文件复制。
- [ ] 非法 JSON 不允许格式化/压缩复制。
- [ ] 全部展开。
- [ ] 全部折叠。
- [ ] 逐层展开。
- [ ] Monaco DiffEditor 左右对比。
- [ ] Diff 模式可退出。
- [ ] JSON Tool 关闭后 Monaco/WebView 被销毁。

---

# 37. 开发阶段划分

## Phase 0：工程初始化

完成：

```text
Tauri 2
React
TypeScript
Vite
pnpm
Rust module skeleton
Capabilities skeleton
```

要求：

- 可编译。
- Windows x64 可运行。
- 不先写 JSON 业务。

## Phase 1：Core Residency

完成：

- single-instance。
- Tray。
- AppLifecycle。
- config.toml。
- 无窗口后台驻留。
- 正确退出。

验收：

```text
启动后只有 Tray
关闭所有窗口进程不退出
Tray 退出才退出
```

## Phase 2：Launcher

完成：

- global shortcut。
- Lazy Launcher。
- 多显示器位置。
- keyboard state machine。
- `1 / S`。
- timeout。
- launcher ready handshake。

验收：

```text
任意应用中 Leader -> Launcher -> 1
```

## Phase 3：Tool Framework

完成：

- ToolId。
- ToolDescriptor。
- ToolRegistry。
- ToolManager。
- WindowManager。
- window state persistence。
- JSON mock window。

验收：

```text
重复 Leader+1 永远只有一个 JSON Window
```

## Phase 4：Storage

完成：

- default DataRoot `~/.juju`。
- config。
- data manifest。
- atomic write。
- JSON documents CRUD。
- watcher。
- revision conflict。
- data root switch/migrate。

验收：

```text
用 VS Code 修改 documents 中的 JSON，juju 能正确感知
```

## Phase 5：JSON Editor

完成：

- React JSON UI。
- document sidebar。
- Monaco lazy loading。
- autosave。
- format。
- syntax diagnostics。
- copy。
- minify copy。
- file copy。
- folding。

## Phase 6：JSON Diff

完成：

- compare selection。
- temporary formatted projection。
- Monaco DiffEditor。
- exit/swap。
- model disposal。

## Phase 7：Settings / Hardening

完成：

- autostart。
- shortcut settings。
- data root UI。
- logging。
- error UX。
- cleanup。
- tests。
- release bundle。

---

# 38. AI 开发执行规范

后续如果使用 AI 根据本文档开发，必须遵守：

## 38.1 每次只做一个 Phase 或一个明确子任务

不要一次生成整个项目的大量未经编译代码。

推荐循环：

```text
读取现有工程
 -> 确认当前 Phase
 -> 实现最小变更
 -> cargo check
 -> frontend typecheck
 -> tests
 -> 修复
 -> 再进入下一步
```

## 38.2 必须实际编译验证

每个 Rust 变更至少运行：

```text
cargo check
```

涉及测试：

```text
cargo test
```

前端：

```text
pnpm typecheck
pnpm test
pnpm build
```

具体 script 如果工程中名称不同，以 `package.json` 为准。

不得给出“理论上可以编译”的代码后直接继续。

## 38.3 禁止虚构 API

尤其：

- Tauri 2。
- Monaco。
- windows-rs。
- Tauri Plugin。
- WebView2。
- `notify`。

如果不确定函数名、feature flag、Capability identifier：

> 查当前依赖版本的官方文档或源码，再实现。

不要根据旧 Tauri v1 API 猜测。

## 38.4 不擅自改变架构

未经明确需求变更，不允许：

- React 换 Vue。
- Tauri 换 Electron。
- 文件存储换 SQLite。
- `~/.juju` 改成数据库。
- 一个 Tool 创建多个窗口实例。
- 把工具后台等同于隐藏 WebView。
- 把所有 WebView 权限开到最大。
- 引入第三方动态插件机制。
- 让前端直接读写任意磁盘路径。

## 38.5 优先减少依赖

新增 crate/npm package 前问：

```text
标准库/Tauri/当前依赖是否已经能可靠实现？
```

只有明显降低复杂度或提高正确性时才增加依赖。

## 38.6 Windows first

如果某功能 Windows 原生实现更可靠：

```text
platform/windows/*
```

中实现。

同时通过 trait/module 边界避免 Tool 直接依赖 Windows API，以保留未来平台扩展能力。

---

# 39. 建议依赖清单

以下是“类别级”依赖建议，不在本文锁定具体 patch 版本。

## Rust

```toml
tauri = "2"
tauri-plugin-single-instance = "2"
tauri-plugin-global-shortcut = "2"
tauri-plugin-autostart = "2"

serde = { version = "1", features = ["derive"] }
serde_json = "1"
toml = "..."
thiserror = "..."
tokio = "..."
notify = "..."
windows = "..."
tracing = "..."
tracing-subscriber = "..."
```

`windows` crate 只启用真正需要的 Win32 feature，避免全量 feature。

是否增加：

```text
tempfile
tracing-appender
```

应在实现 atomic write/log rotation 时根据实际需要决定。

## Frontend

```json
{
  "dependencies": {
    "@tauri-apps/api": "2.x",
    "monaco-editor": "...",
    "react": "...",
    "react-dom": "..."
  }
}
```

开发依赖：

```text
typescript
vite
@vitejs/plugin-react
vitest
@testing-library/react
```

版本初始化时取稳定版本并由 lockfile 固定。

---

# 40. 未来工具接入规范

虽然 v1 只实现 JSON，新工具必须遵循统一过程。

例如未来增加 HTTP：

```text
1. 增加 ToolId::Http
2. 注册 ToolDescriptor
3. 添加前端 tools/http/
4. 添加 Rust tools/http/
5. 定义 capability
6. 定义 command/event
7. 如有后台任务，实现 ToolRuntime
8. 数据放 <DataRoot>/http/
9. 不影响 JSON module
```

网络工具同理。

ToolRegistry 使：

```text
Launcher
Tray
Settings
ToolManager
```

自动识别新 Tool，而不是到四处增加 `if tool == ...`。

---

# 41. 关键架构决策记录（ADR 摘要）

## ADR-001：Tauri 2 而不是 WPF/Electron

决定：

```text
Rust + Tauri 2 + WebView2
```

原因：

- Windows 系统 WebView2。
- Rust 适合系统能力与后台 task。
- 不需要 Electron 自带 Chromium。
- UI 用 Web 技术更适合 Monaco 和后续复杂工具。

## ADR-002：React + TypeScript

决定：

```text
React + TypeScript
```

原因：

- 后续 Tool UI 会变复杂。
- 组件化足够成熟。
- Monaco integration 成熟。
- 不引入额外大型状态库。

## ADR-003：文件系统作为事实数据源

决定：

```text
不使用数据库
```

原因：

- JSON/HTTP Request 天然是文档。
- 用户希望直接查看、编辑、备份。
- 不需要数据库事务/查询特征。

## ADR-004：默认 DataRoot

决定：

```text
%USERPROFILE%\.juju
```

原因：

- 稳定。
- 明确。
- 易备份。
- 用户可自行查看。
- 支持设置中迁移。

## ADR-005：单 Leader Shortcut

决定：

```text
Ctrl + Shift + Alt + Space
```

后接 Launcher 单键。

原因：

- 不占用大量系统全局快捷键。
- 工具数量增长后仍可扩展。
- 可加入搜索模式。

## ADR-006：Tool Window 单实例

决定：

```text
每个 Tool 0/1 Window
```

内部业务 tab/task 可多实例。

## ADR-007：关闭 Tool 不退出 juju

决定：

```text
Core 常驻 Tray
```

只有 Tray/明确 Exit 结束进程。

## ADR-008：后台 Runtime 与 WebView 解耦

决定：

```text
后台运行 ≠ 隐藏 WebView
```

未来 Tool 关闭 UI 时可以销毁 WebView，只保留 Rust Runtime。

## ADR-009：JSON 使用 Monaco

决定：

```text
Monaco Editor + Monaco DiffEditor
```

原因：

- JSON 高亮。
- diagnostics。
- folding。
- search。
- Diff UI。
- 成熟度高。

## ADR-010：JSON 文件复制使用 Windows File Clipboard

决定：

```text
snapshot file + CF_HDROP
```

而不是虚拟文件 COM streaming。

原因：

- 更简单。
- Explorer 兼容好。
- 符合本地工具需求。

---

# 42. 参考资料

开发实现时优先查当前官方文档：

- Tauri 2 Documentation: https://v2.tauri.app/
- Tauri Global Shortcut Plugin: https://v2.tauri.app/plugin/global-shortcut/
- Tauri Single Instance Plugin: https://v2.tauri.app/plugin/single-instance/
- Tauri Autostart Plugin: https://v2.tauri.app/plugin/autostart/
- Tauri Permissions: https://v2.tauri.app/security/permissions/
- Monaco Editor: https://github.com/microsoft/monaco-editor
- Monaco API: https://microsoft.github.io/monaco-editor/typedoc/

说明：具体函数签名、Capability identifier、Cargo feature、前端 package API 随版本可能变化；实现时以项目 lockfile 对应版本的官方类型/源码为准。

---

# 43. 最终实现基线

当前 juju 的最终 v1 基线可概括为：

```text
Windows x64
│
└── juju.exe
    │
    ├── Rust / Tauri 2 Core
    │   ├── Single Instance
    │   ├── Tray
    │   ├── Global Shortcut
    │   ├── LauncherManager
    │   ├── ToolRegistry
    │   ├── ToolManager
    │   ├── RuntimeManager
    │   ├── WindowManager
    │   ├── SettingsService
    │   └── StorageManager
    │
    ├── Launcher WebView2
    │   ├── React
    │   └── Leader second-key handling
    │
    ├── Settings WebView2
    │   └── React
    │
    └── JSON Tool WebView2
        ├── React
        ├── TypeScript
        └── Monaco
            ├── Editor
            └── DiffEditor

App config:
%LOCALAPPDATA%\juju\

Default user data:
%USERPROFILE%\.juju\
└── json\
    ├── documents\
    ├── state.toml
    └── .trash\
```

最核心的工程约束：

```text
一个 Core
一个 Leader Shortcut
每个 Tool 一个 Window
Tool UI 与 Runtime 分离
工具数据全部普通文件
默认 DataRoot = ~/.juju
数据库 = 无
第三方插件 = 无
JSON Editor = Monaco
系统集成 = Rust / Windows
UI = React + TypeScript
```

在此基线上，v1 只实现 JSON Tool；未来 HTTP、Network 等工具按同一 Tool Registry/ToolManager/Storage/Runtime 规范继续增加，不需要重构主框架。
