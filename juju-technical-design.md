# juju 技术方案与开发规范

> 文档用途：作为 **juju** 项目的架构基线、开发计划和 AI 编码执行规范。  
> 当前版本：v1。  
> 当前目标平台：**Windows 11 / Windows x64**。  
> 当前业务工具：**JSON Tool**。  
> 技术栈：**C# + .NET 10 + WPF + WebView2 + Monaco Editor**。  
> 核心原则：**Windows 原生优先、后台低资源、快捷呼出、工具模块化、数据可直接访问。**

---

# 1. 产品定位

## 1.1 产品名称

- 产品名：`juju`
- 主程序：`juju.exe`
- 当前平台：Windows x64
- 优先支持：Windows 11
- 不考虑 macOS / Linux
- 不为了跨平台牺牲 Windows 原生体验

juju 是一个长期运行于 Windows 后台的开发者工具箱。

应用的大部分生命周期处于：

```text
juju.exe
├── WPF Dispatcher
├── Tray
├── Global Shortcut
├── File Watcher
└── 必要后台 Runtime
```

默认不存在：

```text
JSON Tool Window
WebView2
Monaco
其他 Tool Window
```

只有真正使用 JSON Tool 时才加载 WebView2 和 Monaco。

---

# 2. 核心交互模型

juju 不以传统 Main Window 为中心。

主要入口：

```text
Ctrl + Shift + Alt + Space
            │
            ▼
      WPF Launcher
            │
      ┌─────┴─────┐
      │           │
      1           S
      │           │
     JSON       Settings
```

当前：

```text
1 -> JSON
S -> Settings
```

未来：

```text
2 -> HTTP Request
3 -> Network
4 -> Encode / Decode
...
```

只有 Leader Shortcut 是 Windows 全局快捷键。

Launcher 激活之后的：

```text
1
2
3
S
...
```

均为 Launcher 内普通键盘输入，不注册系统全局单键快捷键。

---

# 3. v1 功能范围

v1 必须实现：

1. 后台常驻。
2. 单实例。
3. 系统托盘。
4. 开机启动。
5. 全局 Leader Shortcut。
6. 原生 WPF Launcher。
7. Tool Registry。
8. Tool Manager。
9. Window Manager。
10. Runtime Manager 基础模型。
11. Settings。
12. Light / Dark / System Theme。
13. DataRoot。
14. 文件型 Storage。
15. JSON Tool。
16. Monaco Editor。
17. JSON 自动保存。
18. JSON 手工保存。
19. JSON 外部修改检测。
20. JSON 文档元数据管理。
21. JSON 新建、重命名、删除。
22. Explorer `.json` 文件拖入导入。
23. JSON 格式化。
24. 文本复制。
25. 压缩复制。
26. 文件复制。
27. 展开。
28. 折叠。
29. 逐层展开。
30. JSON Diff。
31. JSON Tool 窗口内快捷键。
32. 每个 Tool Window 单实例。
33. 后台 Runtime 能力模型。
34. Notification / SecretStore / Privilege / Database 能力边界预留。

---

# 4. 明确不做

v1 不实现：

- HTTP Request Tool。
- Network Tool。
- 第三方插件。
- 动态 DLL 插件。
- 脚本扩展平台。
- 云同步。
- 用户账户。
- SQLite 实际业务存储。
- CLI。
- Portable 版本。
- 多个 JSON Tool Window。
- 多端同步。
- JSON Schema Manager。
- JSONPath/JMESPath。
- JSON 语义 Diff。
- 自动更新。
- 主进程管理员运行。

---

# 5. 技术栈

## 5.1 Runtime

```text
.NET 10 LTS
C#
TargetFramework: net10.0-windows
RuntimeIdentifier: win-x64
```

仅构建 Windows x64。

---

## 5.2 UI

```text
WPF
XAML
WPF Fluent Theme
```

主要界面全部使用 WPF：

```text
Launcher
Settings
Tool Window Shell
Toolbar
Document Sidebar
StatusBar
Dialog
Toast
Context Menu
```

禁止为了简单 UI 再引入 HTML。

---

## 5.3 JSON 编辑器

只有 JSON 编辑区域使用：

```text
Microsoft Edge WebView2
        │
        ▼
Monaco Editor
Monaco DiffEditor
```

Monaco Host 使用：

```text
TypeScript
Vite
Monaco Editor
```

不使用：

```text
React
Vue
Angular
Redux
```

Monaco Host 应保持为极薄的一层 Editor Adapter。

---

# 6. 总体架构

```text
┌─────────────────────────────────────────────────┐
│                 juju.exe                        │
│               .NET 10 / C#                      │
│                                                 │
│ AppLifecycle                                    │
│ ToolRegistry                                    │
│ ToolManager                                     │
│ RuntimeManager                                  │
│ WindowManager                                   │
│ LauncherManager                                 │
│ ShortcutManager                                 │
│ SettingsService                                 │
│ ThemeService                                    │
│ StorageManager                                  │
│ ClipboardService                                │
│ TrayService                                     │
│                                                 │
│ platform/windows                                │
│ ├── HotKey                                      │
│ ├── Monitor                                     │
│ ├── Clipboard                                   │
│ ├── Startup                                     │
│ ├── Credential        future                    │
│ └── Privilege         future                    │
└─────────────────────┬───────────────────────────┘
                      │
       ┌──────────────┼──────────────┐
       ▼              ▼              ▼
   Launcher        Settings      JSON Tool
     WPF              WPF            WPF
                                      │
                        ┌─────────────┼────────────┐
                        │             │            │
                    Sidebar        Toolbar       StatusBar
                      WPF             WPF           WPF
                                      │
                                      ▼
                                   WebView2
                                      │
                                      ▼
                                    Monaco
```

原则：

> WebView2 是编辑器控件，不是 juju 应用框架。

---

# 7. 工程结构

建议：

```text
juju/
│
├── Juju.sln
│
├── src/
│   │
│   ├── Juju.App/
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   │
│   │   ├── Bootstrap/
│   │   ├── Views/
│   │   │   ├── LauncherWindow.xaml
│   │   │   ├── SettingsWindow.xaml
│   │   │   └── JsonToolWindow.xaml
│   │   │
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Themes/
│   │   └── Resources/
│   │
│   ├── Juju.Core/
│   │   ├── App/
│   │   ├── Tools/
│   │   ├── Runtime/
│   │   ├── Windows/
│   │   ├── Settings/
│   │   └── Storage/
│   │
│   ├── Juju.Platform.Windows/
│   │   ├── HotKey/
│   │   ├── Clipboard/
│   │   ├── Monitor/
│   │   ├── Startup/
│   │   ├── Credential/
│   │   └── Privilege/
│   │
│   └── Juju.Tools.Json/
│       ├── Documents/
│       ├── Metadata/
│       ├── Storage/
│       ├── Import/
│       ├── Minify/
│       ├── Diff/
│       └── Editor/
│
├── editor/
│   ├── src/
│   │   ├── main.ts
│   │   ├── editor.ts
│   │   ├── diff.ts
│   │   ├── bridge.ts
│   │   └── protocol.ts
│   ├── package.json
│   └── vite.config.ts
│
└── tests/
    ├── Juju.Core.Tests/
    └── Juju.Tools.Json.Tests/
```

虽然拆分为几个项目，但仍然是：

> Modular Monolith。

不得把 v1 设计成微服务或插件平台。

---

# 8. Dependency Injection

使用 `Microsoft.Extensions.DependencyInjection`。

核心 service 建议：

```csharp
IAppLifecycle
IToolRegistry
IToolManager
IRuntimeManager
IWindowManager
ILauncherManager
IGlobalShortcutService
ISettingsService
IThemeService
IStorageManager
IClipboardService
IStartupService
ITrayService
```

JSON：

```csharp
IJsonDocumentStore
IJsonMetadataStore
IJsonDocumentService
IJsonImportService
IJsonEditorBridge
```

原则：

- View 不直接访问文件系统。
- ViewModel 不直接调用 Win32。
- Tool 不直接操作全局 Service Locator。
- Windows API 集中封装。

---

# 9. 单实例模型

使用：

```text
Named Mutex
+
Named Pipe
```

第一个进程：

```text
获取 Mutex
→ 初始化 juju
→ 创建 Named Pipe Server
→ 后台驻留
```

第二个进程：

```text
发现 Mutex 已存在
→ 连接 Named Pipe
→ 发送 ActivateLauncher
→ 退出
```

已有实例：

```text
收到 ActivateLauncher
→ LauncherManager.Toggle()
```

不使用第二个 Core。

---

# 10. 启动流程

```text
juju.exe
   │
   ▼
Single Instance
   │
   ▼
加载 Local Settings
   │
   ▼
初始化日志
   │
   ▼
验证 DataRoot
   │
   ▼
初始化 StorageManager
   │
   ▼
初始化 ToolRegistry
   │
   ▼
初始化 RuntimeManager
   │
   ▼
初始化 Tray
   │
   ▼
注册 Global Shortcut
   │
   ▼
创建隐藏 Launcher Window
   │
   ▼
启动 auto-start Runtime
   │
   ▼
后台驻留
```

正常启动不得弹出任何窗口。

---

# 11. Launcher

Launcher 使用纯 WPF。

生命周期：

```text
程序启动
→ 创建 LauncherWindow
→ 创建 HWND
→ Hide
```

此后始终复用。

Leader Shortcut：

```text
WM_HOTKEY
→ 获取鼠标位置
→ 获取鼠标所在 Monitor
→ 计算 WorkArea
→ 移动 Launcher
→ Show
→ Activate
→ Focus
```

关闭 Launcher：

```text
Hide
```

而不是：

```text
Close
Destroy
Recreate
```

Launcher 本身不包含 WebView2。

---

# 12. Launcher 状态机

```text
Hidden
 │
 │ Leader
 ▼
Showing
 │
 ▼
WaitModifiersReleased
 │
 ▼
Armed
 ├── 1 -> Open JSON
 ├── S -> Settings
 ├── Escape -> Hide
 ├── Leader -> Hide
 └── Timeout -> Hide
```

在：

```text
Ctrl
Shift
Alt
```

全部释放前，不处理二级快捷键。

过滤 keyboard repeat。

默认 timeout：

```text
2000 ms
```

允许用户修改。

---

# 13. 全局快捷键

默认：

```text
Ctrl + Shift + Alt + Space
```

使用 Win32：

```text
RegisterHotKey
UnregisterHotKey
WM_HOTKEY
```

ShortcutManager 管理注册。

修改快捷键：

```text
用户录制新快捷键
→ 校验
→ 尝试注册新快捷键
→ 成功
→ 注销旧快捷键
→ 保存配置
```

失败：

```text
保留旧快捷键
显示 SHORTCUT_REGISTRATION_FAILED
```

---

# 14. Tray

juju 无传统 Main Window。

Tray：

```text
打开 Launcher
JSON
设置
----------------
退出 juju
```

关闭普通窗口不得退出程序。

真正退出：

```text
Tray
→ 退出 juju
```

---

# 15. Tool Registry

```csharp
enum ToolId
{
    Json
}
```

未来：

```csharp
Json
Http
Network
Encode
```

Descriptor：

```csharp
sealed record ToolDescriptor(
    ToolId Id,
    string Name,
    string LauncherKey,
    BackgroundCapability Background,
    WindowSpec Window,
    StorageCapability Storage);
```

StorageCapability：

```csharp
enum StorageCapability
{
    None,
    File,
    Database
}
```

当前：

```text
JSON
Background = Unsupported
Storage = File
```

未来：

```text
HTTP
Storage = File / Database

Network History
Storage = Database
```

---

# 16. SQLite 扩展边界

v1 **不引入 SQLite**。

但是不能假设所有未来 Tool 都必须使用普通文件。

Storage 架构：

```text
StorageManager
│
├── File Storage
│   └── JSON v1
│
└── Database Storage
    └── future SQLite
```

业务 Tool 通过自己的 Repository / Store 接口访问数据。

禁止设计：

```text
一个全局数据库保存所有 Tool 数据
```

未来建议：

```text
<DataRoot>\
└── <tool>\
    └── data.db
```

每个 Tool 自己负责自己的 storage schema。

Database 能力以后可以实现：

```text
Microsoft.Data.Sqlite
```

但 v1 不引用、不初始化、不创建数据库。

---

# 17. Tool Window 与 Runtime 分离

```csharp
enum UiState
{
    Closed,
    Opening,
    Visible,
    Minimized
}

enum RuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

enum BackgroundCapability
{
    Unsupported,
    Optional,
    Required
}
```

JSON：

```text
BackgroundCapability.Unsupported
```

未来：

```text
Download       Optional
Ping Monitor   Optional
Clipboard      Optional / Required
```

必须区分：

```text
CloseToBackground
AutoStartRuntime
```

前者表示：

> UI 关闭后 Runtime 是否继续。

后者表示：

> juju 启动时 Runtime 是否自动启动。

---

# 18. WindowManager

所有顶层窗口由 WindowManager 管理。

```text
Launcher
Settings
JSON
```

每种窗口最多一个实例。

调用：

```csharp
ToolManager.Open(ToolId.Json);
```

行为：

```text
不存在
→ Create
→ Restore Window State
→ Show
→ Focus

已最小化
→ Restore
→ Focus

已隐藏
→ Show
→ Focus

已显示
→ Focus
```

禁止创建第二个 JSON Window。

---

# 19. 窗口位置管理

保存：

```text
X
Y
Width
Height
Maximized
Monitor
```

存储：

```text
%LOCALAPPDATA%\juju\window-state.json
```

恢复时检查：

- Monitor 是否仍存在。
- Window Rect 是否与任意 WorkArea 相交。
- DPI 是否变化。
- 窗口是否仍能看到。
- 最小尺寸是否满足。

无效：

```text
鼠标当前 Monitor
→ WorkArea Center
```

已经打开的窗口再次激活时：

> 不自动移动到当前鼠标显示器。

---

# 20. Theme

v1 支持：

```text
System
Light
Dark
```

默认：

```text
System
```

统一由：

```csharp
ThemeService
```

管理。

WPF 使用 Fluent Theme。

Monaco Theme 与应用主题同步：

```text
WPF Light
→ Monaco vs

WPF Dark
→ Monaco vs-dark

WPF System
→ 根据当前系统实际主题同步 Monaco
```

主题变化时：

```text
ThemeService
├── 更新 WPF Theme
└── 通知 JsonEditorBridge
     └── Monaco setTheme
```

禁止各窗口自行判断主题。

---

# 21. 配置目录

应用本机配置：

```text
%LOCALAPPDATA%\juju\
```

建议：

```text
%LOCALAPPDATA%\juju\
├── config.json
├── window-state.json
├── logs\
├── cache\
│   └── clipboard\
├── webview2\
└── runtime\
```

这里不存放用户 JSON 正文。

---

# 22. DataRoot

默认：

```text
%USERPROFILE%\.juju
```

必须通过 Windows/.NET API 获取用户目录。

不要手工拼：

```text
C:\Users\xxx
```

用户可以修改 DataRoot。

---

# 23. DataRoot 结构

```text
~\.juju\
│
├── juju.json
│
└── json\
    │
    ├── metadata.json
    │
    ├── documents\
    │   ├── 20260911001.json
    │   ├── 20260911002.json
    │   └── request.json
    │
    └── .trash\
```

`juju.json`：

```json
{
  "schemaVersion": 1
}
```

用于：

```text
识别合法 DataRoot
数据结构版本
未来 Migration
```

---

# 24. JSON Metadata

JSON Tool 必须维护：

```text
<DataRoot>\json\metadata.json
```

它是 JSON Tool 的业务元数据。

不保存 JSON 正文。

示例：

```json
{
  "schemaVersion": 1,
  "sequence": {
    "date": "20260911",
    "last": 3
  },
  "documents": [
    {
      "id": "d6a74cf8-6dda-4f26-b96b-7beee3a6740d",
      "fileName": "20260911001.json",
      "order": 0,
      "createdAtUtc": "2026-09-11T08:00:00Z",
      "updatedAtUtc": "2026-09-11T08:12:00Z",
      "source": {
        "kind": "created"
      }
    },
    {
      "id": "...",
      "fileName": "request.json",
      "order": 1,
      "createdAtUtc": "...",
      "updatedAtUtc": "...",
      "source": {
        "kind": "imported",
        "originalFileName": "request.json"
      }
    }
  ]
}
```

---

# 25. DocumentId

不能再直接把文件名当成唯一业务 ID。

定义：

```csharp
readonly record struct JsonDocumentId(Guid Value);
```

原因：

```text
文档允许 Rename
        │
        ▼
文件名会变化
        │
        ▼
业务 ID 不应该变化
```

Metadata：

```text
DocumentId
     │
     └── FileName
```

Rename 只改变：

```text
FileName
```

不改变：

```text
DocumentId
CreatedAt
Order
```

---

# 26. Metadata 的事实源规则

JSON 正文：

```text
documents/*.json
```

仍然是内容事实数据源。

Metadata 是：

```text
Document ID
文档顺序
序号
创建时间
来源
其他 UI/业务元数据
```

的事实来源。

但 Metadata 必须具备恢复能力。

启动时执行：

```text
Read metadata
+
Scan documents directory
+
Reconcile
```

例如发现：

```text
磁盘有文件
metadata 没记录
```

则创建 Metadata Entry。

发现：

```text
metadata 有记录
文件不存在
```

则移除或标记 Missing，并记录日志。

因此：

> Metadata 损坏不能导致 JSON 正文丢失。

---

# 27. Metadata 写入

必须原子写。

```text
metadata.json.tmp
→ 完整写入
→ Flush
→ Replace metadata.json
```

写失败：

```text
保留旧 metadata
```

Metadata 更新必须序列化执行。

禁止多个 Task 同时修改 metadata.json。

使用：

```csharp
SemaphoreSlim
```

或等价串行机制。

---

# 28. JSON 新建命名规则

新建默认文档：

```text
yyyyMMdd{sequence}
```

磁盘：

```text
yyyyMMdd{sequence}.json
```

例如当天第三个：

```text
20260911003.json
```

UI：

```text
20260911003
```

Sequence：

```text
001
002
003
...
999
1000
```

采用：

```csharp
sequence.ToString("D3")
```

即最少 3 位，不限制最多 3 位。

每天独立序号：

```text
20260911 -> 001...
20260912 -> 001...
```

---

# 29. Sequence 生成规则

Metadata：

```json
"sequence": {
  "date": "20260911",
  "last": 3
}
```

新建：

```text
CurrentDate == metadata.sequence.date
→ last + 1

CurrentDate != metadata.sequence.date
→ sequence = 1
```

仍必须扫描目标文件冲突。

即：

```text
Metadata Sequence
+
Existing File Check
```

双重保证。

删除文档之后不得复用旧 Sequence。

Rename 不影响 Sequence。

Import 不消耗日期 Sequence。

---

# 30. 新建 JSON

点击：

```text
新建
```

流程：

```text
Generate sequence
→ Generate file name
→ Create {}
→ Atomic write
→ Add metadata
→ Save metadata
→ Add to document list
→ Select
→ Open Monaco
→ Focus Monaco
```

---

# 31. 手工保存 + 自动保存

两者同时存在。

## 自动保存

默认：

```text
800 ms debounce
```

流程：

```text
Monaco content changed
→ DocumentSession.Dirty = true
→ 800 ms
→ Save
```

## 手工保存

按钮：

```text
保存
```

语义：

> 立即保存当前 Monaco 内容，不等待 Autosave Debounce。

流程：

```text
取消 pending debounce
→ 获取 Monaco Current Text
→ SaveImmediately
→ 更新 Revision
→ Dirty = false
```

即使当前没有 Dirty：

```text
Save
→ no-op
→ 状态显示 已保存
```

---

# 32. 保存状态

DocumentSession：

```csharp
enum SaveState
{
    Clean,
    Dirty,
    Saving,
    SaveFailed,
    Conflict
}
```

StatusBar 显示：

```text
已保存
正在保存…
未保存
保存失败
外部修改冲突
```

窗口关闭之前：

```text
Dirty
→ Flush

Saving
→ Await

SaveFailed
→ 提示用户
```

---

# 33. 非法 JSON 保存

仍然允许保存：

```json
{
  "name":
```

保存是：

> 文本持久化。

不是：

> JSON Validation。

因此非法 JSON：

允许：

```text
Save
Auto Save
Copy
File Copy
Rename
```

禁止：

```text
Format
Minified Copy
```

Diff 可以回退为 Raw Text Diff。

---

# 34. Storage Revision

读取：

```csharp
JsonDocumentSnapshot
{
    DocumentId
    Content
    Revision
}
```

Revision 可以使用：

```text
Length
LastWriteTimeUtc
Hash
```

具体实现封装在：

```text
DocumentRevisionService
```

写入前：

```text
ExpectedRevision
vs
DiskRevision
```

不同：

```text
ExternalModificationConflict
```

不得静默覆盖。

---

# 35. 外部文件修改

使用：

```text
FileSystemWatcher
```

监听：

```text
<DataRoot>\json\documents
```

事件必须 debounce。

应用自己的保存同样会触发 watcher，因此必须维护：

```text
SelfWrite Token / Revision
```

过滤自身事件。

---

# 36. 外部修改冲突

编辑器没有 Dirty：

```text
External Change
→ Reload
→ 更新 Monaco
→ Toast "文件已在外部更新"
```

编辑器 Dirty：

```text
External Change
→ 不覆盖 Monaco
→ SaveState = Conflict
```

显示：

```text
文件已被外部程序修改

[重新加载磁盘版本]
[覆盖磁盘版本]
[复制当前内容后重新加载]
```

v1 至少完成前三种操作的完整处理。

---

# 37. Explorer 拖入 JSON

JsonToolWindow：

```text
AllowDrop = true
```

只接受：

```text
*.json
```

拖入语义不是打开原文件，而是：

> Import / Copy Into juju。

例如：

```text
D:\temp\request.json
```

拖入后：

```text
读取 request.json
      │
      ▼
<DataRoot>\json\documents\request.json
```

原始文件保持不变。

以后修改 DataRoot 中的文件不会修改导入源文件。

---

# 38. Import 文件名

源：

```text
request.json
```

磁盘文档：

```text
request.json
```

UI 显示：

```text
request
```

即 UI 不显示 `.json`。

如果文件已经存在：

```text
request.json
```

自动寻找：

```text
request-2.json
request-3.json
...
```

严禁覆盖已有文档。

Metadata 记录：

```json
{
  "source": {
    "kind": "imported",
    "originalFileName": "request.json"
  }
}
```

允许一次拖入多个 `.json` 文件，按拖入顺序依次导入。

非 JSON 文件：

```text
忽略
+
明确提示
```

拖入不要求 JSON 内容合法。

---

# 39. WebView2 Drop

不让 WebView2 自己接管文件导入业务。

整个 Import 必须经过 C#：

```text
Explorer
→ JsonToolWindow
→ JsonImportService
→ Storage
```

不能：

```text
Explorer
→ HTML / JS File API
→ Monaco
```

必要时关闭 WebView2 默认 External Drop，由 WPF Host 统一管理。

---

# 40. JSON Tool UI

```text
┌───────────────────────────────────────────────────────────────┐
│ 新建 保存 格式化 复制 压缩复制 文件复制 展开 折叠 逐层 对比 │
├────────────────┬──────────────────────────────────────────────┤
│ 文档           │                                              │
│                │                                              │
│ 20260911003    │              Monaco Editor                   │
│ request        │                                              │
│ payment        │                                              │
│                │                                              │
├────────────────┴──────────────────────────────────────────────┤
│ JSON ✓     Ln 12, Col 8                    已保存             │
└───────────────────────────────────────────────────────────────┘
```

---

# 41. 文档列表

显示：

```text
FileName without .json
```

支持：

```text
选择
Rename
Delete
拖动调整顺序
```

拖动排序后：

```text
更新 documents[].order
→ Save metadata
```

Explorer 中的文件排序不影响 juju UI 排序。

---

# 42. Rename

例如：

```text
20260911003
→ payment-request
```

磁盘：

```text
20260911003.json
→ payment-request.json
```

Metadata：

```text
DocumentId 不变
FileName 更新
Order 不变
```

禁止：

```text
\
/
:
*
?
"
<
>
|
..
Windows Reserved Name
```

冲突：

```text
DOCUMENT_ALREADY_EXISTS
```

不得自动覆盖。

---

# 43. Delete

删除不立即永久删除。

移动到：

```text
<DataRoot>\json\.trash\
```

例如：

```text
20260911-180702__payment-request.json
```

Metadata 中移除。

如果删除当前文档：

```text
选择下一文档
```

如果列表为空：

```text
自动创建一个新的 yyyyMMdd001 风格文档
```

实际 Sequence 根据当天 metadata 决定，不复用旧号。

---

# 44. Monaco Host

Monaco Host 只负责：

```text
Editor
DiffEditor
JSON Language Support
Folding
Formatting
Diagnostics
Search
Undo / Redo
```

不得负责：

```text
磁盘
DataRoot
Metadata
Windows Clipboard
File Clipboard
Settings Persistence
FileSystemWatcher
```

---

# 45. WebView2 通信

统一：

```text
C# -> JS
CoreWebView2.PostWebMessageAsJson

JS -> C#
window.chrome.webview.postMessage
```

禁止大量使用：

```text
ExecuteScriptAsync($"editor.setValue('{content}')")
```

传递用户正文。

---

# 46. Editor Protocol

C# -> Monaco：

```text
initialize
openDocument
replaceContent
setTheme
format
foldAll
unfoldAll
foldLevel
enterDiff
exitDiff
focus
getContent
```

Monaco -> C#：

```text
ready
contentChanged
saveRequested
cursorChanged
validationChanged
editorFocused
commandResult
```

所有消息必须包含：

```text
version
type
requestId
payload
```

需要返回值的操作通过 `requestId` 配对。

---

# 47. Monaco 生命周期

打开 JSON Window：

```text
Create WPF Window
→ Initialize WebView2
→ Load local editor assets
→ Initialize Monaco
→ Ready
```

关闭：

```text
Flush pending save
→ Dispose Monaco models
→ Dispose Diff models
→ Dispose Editor
→ Dispose WebView2
→ Close WPF Window
```

JSON Window 关闭之后：

> 不保留隐藏 WebView2。

---

# 48. Monaco 本地资源

Monaco JS/CSS 必须随应用安装。

禁止运行时从：

```text
CDN
Internet
```

加载。

必须允许：

```text
完全离线使用
```

WebView2 只加载 juju 自己的静态资源。

---

# 49. JSON 格式化

只有合法 JSON 才允许。

优先调用 Monaco JSON Formatter。

成功：

```text
Monaco Model Updated
→ Dirty
→ Autosave
```

格式化按钮本身不直接写文件。

非法：

```text
不改变文本
→ 显示 JSON 格式错误
→ 能定位时跳到错误位置
```

---

# 50. 普通复制

按钮：

```text
复制
```

定义：

> 复制当前文档完整原始文本。

不是 Monaco selection copy。

流程：

```text
Monaco getValue
→ C#
→ ClipboardService.CopyText
```

非法 JSON 同样允许。

---

# 51. 压缩复制

要求：

```text
Validate JSON
→ Lexical Minify
→ Clipboard
```

禁止通过：

```text
Deserialize
→ Serialize
```

实现压缩复制。

必须尽量保持：

```text
数字 token
Key 顺序
重复 Key
字符串内容
```

例如：

```json
{
  "value": 1e10,
  "text": "a b"
}
```

得到：

```json
{"value":1e10,"text":"a b"}
```

而不是重新编码数字。

---

# 52. 文件复制

语义：

> 将当前编辑器内容作为一个 `.json` 文件复制到 Windows Clipboard。

流程：

```text
Flush Current Editor
→ 创建 Clipboard Snapshot
→ Windows File Clipboard
```

Snapshot：

```text
%LOCALAPPDATA%\juju\cache\clipboard\
```

文件名使用当前文档名。

例如：

```text
payment.json
```

用户：

```text
Desktop
Ctrl+V
```

得到：

```text
payment.json
```

非法 JSON 允许文件复制。

---

# 53. Clipboard Snapshot 生命周期

Snapshot 不能复制后立即删除。

启动 juju 时：

```text
删除超过 3 天的 clipboard snapshot
```

清理失败：

```text
记录日志
不阻塞应用启动
```

---

# 54. 展开 / 折叠

按钮：

```text
展开
折叠
逐层展开
```

分别映射 Monaco 官方公开 action。

不得访问 Monaco 私有 Folding Model。

逐层展开维护：

```text
foldDepth
```

例如：

```text
1
2
3
4
...
```

用户手工折叠后允许 reset `foldDepth`。

---

# 55. JSON Diff

进入：

```text
当前文档
→ 对比
→ 选择目标文档
```

当前：

```text
Original / Left
```

选择：

```text
Modified / Right
```

进入 Monaco DiffEditor。

---

# 56. Diff 内容

两边合法 JSON：

```text
原始文档
→ Temporary Formatted Projection
→ Monaco Diff
```

Projection 不修改磁盘文件。

任意一边非法：

```text
Raw Text Diff
```

并显示：

```text
存在非法 JSON，当前按原文比较
```

Diff：

```text
Side-by-side
Read-only
Line Diff
Character Diff
Synchronized Scrolling
```

v1 不做语义 JSON Diff。

---

# 57. JSON Tool 快捷键

所有 Toolbar Command 必须具有对应：

```text
Alt + 单键
```

并优先使用左手区域。

推荐最终映射：

| 功能 | 默认快捷键 | 设计含义 |
|---|---|---|
| 新建 | `Alt + A` | Add |
| 保存 | `Alt + S` | Save |
| 格式化 | `Alt + F` | Format |
| 复制全文 | `Alt + C` | Copy |
| 压缩复制 | `Alt + X` | 紧邻 C，适合左手 |
| 文件复制 | `Alt + V` | 紧邻 C/X |
| 展开全部 | `Alt + E` | Expand |
| 折叠全部 | `Alt + R` | Reduce |
| 逐层展开 | `Alt + D` | Depth |
| 对比 | `Alt + Q` | 左上位置，避免与其他功能冲突 |

推荐手位：

```text
Q W E R
 A S D F
  Z X C V
```

所有主要操作均能左手完成。

实现层面：

> 默认识别 Left Alt + Key。

避免把 `Right Alt / AltGr` 当作业务快捷键，以免影响特殊键盘布局输入。

---

# 58. 标准编辑快捷键不能破坏

Monaco 中必须继续支持：

```text
Ctrl + C     Selection Copy
Ctrl + X
Ctrl + V
Ctrl + Z
Ctrl + Y
Ctrl + F
Ctrl + A
```

另外允许：

```text
Ctrl + S
```

作为“立即保存”的辅助传统快捷键。

但 Toolbar 显示的正式 juju Shortcut：

```text
Alt + S
```

---

# 59. 为什么复制使用 Alt+C

需要明确区分：

```text
Ctrl+C
```

含义：

> Monaco 当前 Selection Copy。

而：

```text
Alt+C
```

含义：

> juju 的“复制完整 JSON 文本”。

两者不得混淆。

---

# 60. Shortcut Router

JsonToolWindow 必须有：

```text
JsonCommandRouter
```

所有：

```text
Toolbar Button
Alt Shortcut
其他入口
```

最终调用同一个 Command。

例如：

```text
Alt+F
   │
   ├──────────────┐
Toolbar Format    │
   │              │
   ▼              ▼
     FormatJsonCommand
```

禁止 Toolbar 与 Shortcut 分别实现业务逻辑。

---

# 61. WebView2 焦点下的 Shortcut

WebView2 是 Native HWND Host。

Monaco 获得焦点后，普通 WPF Window Keyboard Event 不能作为唯一快捷键来源。

因此 Shortcut Router 必须同时接入：

```text
WPF PreviewKeyDown
+
WebView2 AcceleratorKeyPressed
```

例如 Monaco 中按：

```text
Alt + F
```

流程：

```text
WebView2 AcceleratorKeyPressed
→ JsonCommandRouter
→ FormatJsonCommand
→ Handled = true
```

否则快捷键体验会因为焦点位于 Monaco 而失效。

必须为这一行为写 Integration Test。

---

# 62. Shortcut Repeat

以下操作禁止长按重复触发：

```text
New
Save
Format
Copy
Minify Copy
File Copy
Diff
```

需要过滤：

```text
key repeat
```

Fold/Expand 同样默认过滤 repeat。

---

# 63. Settings

v1：

```text
常规
├── 开机自动启动
├── Global Shortcut
├── Launcher Timeout
└── Theme
     ├── 跟随系统
     ├── 浅色
     └── 深色

数据
├── DataRoot
├── 打开目录
├── 使用已有目录
├── 迁移到新目录
└── DataRoot 状态

JSON
├── Autosave Delay
└── Indent Size
```

默认：

```text
Autostart       true/false 根据首次安装产品决定
Theme           System
Launcher        2000 ms
Autosave        800 ms
Indent          2
```

建议首次安装：

```text
Autostart = false
```

由用户主动开启。

---

# 64. 开机启动

使用：

```text
StartupService
```

采用当前用户级启动注册。

不要求管理员权限。

设置：

```text
开启
→ 注册 juju.exe

关闭
→ 删除注册
```

开机启动：

```text
启动 Core
创建 Tray
注册 Shortcut
创建隐藏 Launcher
```

不得自动显示 Launcher 或 JSON。

---

# 65. DataRoot 切换

提供两个不同操作：

```text
使用已有目录
迁移到新目录
```

不可合并。

---

# 66. 使用已有目录

```text
选择目录
→ 验证 juju.json
→ 检查 schema
→ Flush 所有 Tool
→ 停止旧 watcher
→ 切换 root
→ 创建新 watcher
→ 更新 config
→ Tool Reload
```

如果空目录：

```text
允许初始化为新 juju DataRoot
```

普通非空目录：

> 不得静默初始化。

---

# 67. DataRoot 迁移

```text
Flush
→ Pause Watchers
→ 创建目标
→ Copy
→ Validate
→ Switch Config
→ Start Watchers
```

成功之前：

> 不删除源目录。

v1：

```text
复制
→ 校验
→ 切换
→ 原目录保留
```

---

# 68. DataRoot 不可用

例如：

```text
移动硬盘断开
权限变化
目录被删除
```

严禁：

```text
自动回到 ~/.juju
```

否则会产生两套数据。

显示：

```text
数据目录不可用

[重试]
[重新选择]
[打开设置]
```

Core、Tray 和 Launcher 继续运行。

---

# 69. 文件写入

禁止：

```csharp
File.WriteAllText(finalPath, ...)
```

直接 truncate 最终文件。

统一：

```text
temp
→ write
→ flush
→ replace
```

目标：

```text
崩溃不留下半个文件
写失败保留旧文件
```

JSON 文档和 Metadata 均采用相同原则。

---

# 70. StorageManager

StorageManager 负责：

```text
DataRoot
Path Safety
Atomic Write
Schema
Migration
File Watcher
Revision
Self-write Filtering
```

Tool 不允许散落：

```csharp
File.WriteAllText(...)
```

---

# 71. JSON 文件安全

任何文件名必须满足：

- 仅文件名。
- 无 path separator。
- 无 `..`。
- `.json`。
- 非 Windows Reserved Device Name。
- canonical parent 必须是 `json/documents`。
- Unicode 文件名允许。

---

# 72. 日志

使用：

```text
Microsoft.Extensions.Logging abstraction
```

落盘实现采用稳定的 rolling-file logging provider。

位置：

```text
%LOCALAPPDATA%\juju\logs\
```

记录：

```text
Application Lifecycle
Window Lifecycle
Shortcut
Storage Error
Watcher
WebView2 Lifecycle
Monaco Ready
Import
Clipboard Error
Unhandled Exception
```

禁止记录：

```text
JSON 正文
Clipboard 正文
Secret
Authorization
Cookie
API Key
```

---

# 73. Error Model

核心业务使用 typed exception/result。

建议统一：

```csharp
enum ErrorCode
{
    InvalidJson,
    DocumentNotFound,
    DocumentAlreadyExists,
    InvalidDocumentName,
    ExternalModificationConflict,
    DataRootUnavailable,
    DataRootInvalid,
    ClipboardFailed,
    ShortcutRegistrationFailed,
    StorageIoError,
    MetadataCorrupted,
    ImportFailed,
    WebViewInitializationFailed
}
```

UI 判断：

```text
ErrorCode
```

不得解析错误 Message。

---

# 74. Metadata Recovery

Metadata 无法读取：

```text
尝试 Backup
```

仍失败：

```text
扫描 documents/
→ Rebuild Metadata
```

恢复：

```text
DocumentId 重新生成
Order 按稳定规则恢复
Sequence 根据 yyyyMMddNNN 最大值恢复
```

正文不得删除。

恢复后明确记录：

```text
metadata-recovered
```

必要时通知用户：

```text
JSON 文档索引已恢复
```

---

# 75. 顺序恢复规则

Metadata 丢失时：

优先：

```text
CreationTime
```

无法可靠获取时：

```text
FileName ordinal
```

恢复后重新写：

```text
order = 0..N
```

正常运行过程中始终以 Metadata order 为准。

---

# 76. 删除与 Rename 的一致性

由于：

```text
JSON 文件
+
metadata.json
```

无法组成真正数据库 Transaction，因此操作必须具备补偿逻辑。

Rename：

```text
Validate
→ Rename File
→ Save Metadata
```

Metadata 保存失败：

```text
尝试 Rollback File Rename
```

仍失败：

```text
记录 Critical
→ 下次启动 Reconcile
```

不因此引入 SQLite。

---

# 77. Future Database Architecture

未来 Tool 需要 SQLite 时：

```text
Tool
→ Repository
→ SQLite Store
```

不能让：

```text
View
ViewModel
WindowManager
```

直接依赖 SQLite。

数据库生命周期属于 Tool Storage。

这样未来：

```text
JSON → File
HTTP → SQLite
Network → SQLite
Encode → None
```

可以并存。

---

# 78. Notification / Secret / Privilege

预留：

```csharp
INotificationService
ISecretStore
IPrivilegeService
```

v1 可以实现：

```text
Notification -> Noop / basic
SecretStore   -> Stub
Privilege     -> Stub
```

未来 Secret：

```text
Windows Credential Manager
```

未来管理员任务：

```text
juju.exe
    │
    └── juju-helper.exe
             UAC
```

主 juju.exe：

> 默认永不管理员启动。

---

# 79. 安装模式

只提供正常安装版本。

不提供：

```text
Portable ZIP
```

发布目标：

```text
Windows x64
Self-contained .NET deployment
Installer
```

即用户不必预先安装正确版本的 .NET Desktop Runtime。

安装程序负责：

```text
安装文件
Start Menu
卸载信息
必要运行依赖检查
```

WebView2 Runtime 应优先使用 Windows 11 已安装的 Evergreen Runtime，并对缺失场景提供清晰安装错误。

---

# 80. 性能策略

最重要：

> 后台低资源不能通过保留隐藏 WebView2 实现。

后台：

```text
WPF Core
Tray
Shortcut
Watchers
Runtime
```

JSON 关闭：

```text
No WebView2
No Monaco
```

Launcher：

```text
常驻隐藏 WPF Window
```

由于非常轻，不为了节省极少资源反复销毁。

---

# 81. 性能埋点

至少记录：

```text
Process Working Set
Private Memory
App Startup
Leader Trigger
Launcher Visible
JSON Window Created
WebView2 Initialized
Monaco Ready
Document Loaded
```

大文件：

```text
1 MB edit
10 MB load
10 MB format
5 MB + 5 MB diff
```

不设硬性的 idle memory 验收数字。

先 Profile，再优化。

---

# 82. 大文件模式

建议软阈值：

```text
10 MB
```

超过后：

```text
显示大文件提示
```

仍允许：

```text
打开
编辑
保存
复制
文件复制
```

Format：

```text
允许，但提示
```

Diff：

```text
允许，但提示
```

禁止对每次输入执行昂贵全文解析。

---

# 83. WebView2 Crash

WebView2 / Monaco 异常退出：

```text
WPF Core 继续存在
```

JsonToolWindow 应可以：

```text
Destroy WebView2
→ Reinitialize
→ Reload Current Document
```

Storage 不依赖 WebView 生命周期。

---

# 84. Shutdown

Tray Exit：

```text
isExiting = true
       │
       ▼
停止 Launcher
       │
       ▼
Unregister Global Shortcut
       │
       ▼
Flush Dirty Documents
       │
       ▼
Save Metadata
       │
       ▼
Stop Runtime
       │
       ▼
Stop Watchers
       │
       ▼
Dispose WebView2
       │
       ▼
Dispose Tray
       │
       ▼
Exit
```

普通关闭 Tool Window：

```text
不退出 juju
```

---

# 85. Security 基线

即使本地个人工具仍必须：

1. WebView2 不获得任意文件系统权限。
2. Monaco 只加载本地可信代码。
3. WebView2 不导航互联网。
4. C# 端重新验证来自 JS 的所有 Message。
5. DocumentId 不等于外部任意路径。
6. 防止 Path Traversal。
7. DataRoot canonicalization。
8. 不记录 JSON 正文。
9. 主程序不以 Administrator 启动。
10. Future Secret 不存储在普通 DataRoot。
11. 外部拖入文件只能做受控 Copy。
12. 不允许 HTML/JS 直接执行 Shell Command。

---

# 86. 测试

## Core Unit Tests

至少：

```text
JSON File Name Validation
Sequence Generation
Date Sequence Reset
Metadata Serialization
Metadata Recovery
Metadata Reconcile
Rename Conflict
Import Conflict
Atomic Write
Revision Conflict
Lexical Minify
DataRoot Validation
```

## Integration Tests

至少：

```text
Create Document
Manual Save
Autosave
Close Flush
Rename
Delete
Import
External Modification
Self-write Watcher Dedup
DataRoot Switch
Clipboard File
```

## UI Tests / Manual Verification

重点：

```text
Global Shortcut
Multi-monitor
Different DPI
Launcher Focus
Alt shortcuts
Alt shortcut while Monaco focused
Theme Switching
Monaco Theme Sync
WebView2 Dispose/Reopen
Explorer Drag & Drop
```

---

# 87. v1 推荐开发顺序

Phase 1：

```text
.NET/WPF Skeleton
DI
Logging
Single Instance
Tray
Global Shortcut
Launcher
WindowManager
Settings
Theme
```

Phase 2：

```text
DataRoot
Atomic File Storage
Metadata
Sequence
Document CRUD
FileSystemWatcher
Revision
```

Phase 3：

```text
JsonToolWindow
Sidebar
Toolbar
StatusBar
WebView2
Monaco Bridge
```

Phase 4：

```text
Autosave
Manual Save
Formatting
Clipboard
File Clipboard
Fold
Diff
```

Phase 5：

```text
Import
Drag reorder
External conflict
Theme sync
Shortcut Router
WebView shortcut integration
```

Phase 6：

```text
Installer
Autostart
Performance Profile
Crash Recovery
Integration Tests
```

---

# 88. AI 编码约束

任何 AI 执行开发时必须遵守：

1. 不自行改技术栈。
2. 不把整个 UI 改为 Web。
3. 不加入 React。
4. 不引入 Electron。
5. 不加入 SQLite 到 JSON Tool。
6. 不把 JSON 正文写进 Metadata。
7. 不绕过 StorageManager 写 DataRoot。
8. 不让 WebView2直接访问用户磁盘。
9. 不把文件名当永久 DocumentId。
10. 不隐藏 WebView2 长期常驻。
11. 不通过反序列化再序列化实现压缩复制。
12. 不覆盖外部修改冲突。
13. 不删除用户导入源文件。
14. 不因为 JSON 非法拒绝保存文本。
15. 不在 Rename / Import 冲突时静默覆盖。
16. 不假设 WPF Keyboard Event 能覆盖 Monaco 焦点。
17. 不使用 Monaco 私有 API。
18. 对具体 WebView2 / Monaco API 不确定时检查当前锁定版本官方 API。
19. 所有 IO 都必须处理异常。
20. 所有异步后台任务必须支持 CancellationToken。

---

# 89. 最终 v1 架构基线

```text
Windows 11 x64
│
└── juju.exe
    │
    ├── .NET 10 / C#
    │
    ├── WPF Core
    │   ├── AppLifecycle
    │   ├── SingleInstance
    │   ├── Tray
    │   ├── GlobalShortcut
    │   ├── LauncherManager
    │   ├── ToolRegistry
    │   ├── ToolManager
    │   ├── RuntimeManager
    │   ├── WindowManager
    │   ├── SettingsService
    │   ├── ThemeService
    │   └── StorageManager
    │
    ├── Launcher
    │   └── Pure WPF / persistent hidden window
    │
    ├── Settings
    │   └── Pure WPF
    │
    └── JSON Tool
        │
        ├── WPF Toolbar
        ├── WPF Document Sidebar
        ├── WPF StatusBar
        │
        └── WebView2
            └── Monaco
                ├── Editor
                └── DiffEditor
```

Storage：

```text
%LOCALAPPDATA%\juju\
├── config.json
├── window-state.json
├── logs\
├── cache\
└── webview2\
```

用户数据：

```text
%USERPROFILE%\.juju\
│
├── juju.json
│
└── json\
    ├── metadata.json
    ├── documents\
    │   ├── 20260911001.json
    │   ├── 20260911002.json
    │   └── request.json
    └── .trash\
```

最终原则：

```text
Windows 原生 Shell
+
轻量后台 Core
+
按需创建 WebView2
+
Monaco 只负责编辑能力
+
普通文件保存用户 JSON
+
Metadata 管理 JSON 文档状态
+
Repository/Storage 边界预留未来 SQLite
```

这作为 juju v1 的技术实现基线。