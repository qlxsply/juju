# Juju C# / WPF 学习笔记

> 面向有 Java Web 经验、刚开始接触 Windows 桌面应用的开发者。本文只描述当前仓库已有的代码与行为。

## 1. 先建立整体认知

Juju 是一个 Windows 桌面工具箱。目前真正可用的工具是 JSON 编辑器；Launcher 中的 EchoHttp 与 Kairos 只有入口和“正在开发中”提示。

与典型 Java Web 应用相比，最重要的变化是：

| Java Web | 当前 Juju WPF 应用 |
| --- | --- |
| Spring Boot `main` 启动 Web 容器 | WPF 框架创建 `App`，调用 `OnStartup` |
| HTTP 请求触发 Controller | 鼠标、键盘、窗口、托盘、文件系统事件触发处理器 |
| 浏览器渲染 HTML | WPF 根据 XAML 创建原生 Windows 控件 |
| 浏览器请求携带短生命周期状态 | 进程与窗口常驻，窗口可 `Hide()` 后复用 |
| Spring IoC 自动扫描常见 | `App.OnStartup` 显式 `AddSingleton` 注册依赖 |
| 服务通常运行在服务端线程池 | UI 控件只能由 WPF Dispatcher/UI 线程访问 |

本项目不是 MVVM 架构：窗口主要采用 **XAML + code-behind**。XAML 声明控件与布局，`*.xaml.cs` 保存事件处理、临时 UI 状态与对业务服务的调用。该写法对学习 WPF 很直观；如果 JSON 窗口继续增长，应逐步把状态和命令提取到 ViewModel。

## 2. 项目结构与依赖方向

```text
Juju.App                  WPF 宿主、窗口、托盘、主题、UI 适配器
  ├─ Juju.Platform.Windows Windows 注册表、全局快捷键等平台能力
  ├─ Juju.Tools.Json       JSON 文档和工具业务逻辑
  └─ Juju.Core             跨工具契约、配置、存储、错误、日志

tests/Juju.Tools.Json.Tests  JSON 领域服务的 xUnit 测试
editor/                      TypeScript + Monaco，打包后被 WebView2 加载
```

### 2.1 各项目职责

| 项目 | 作用 | 代表文件 |
| --- | --- | --- |
| `src/Juju.Core` | 不依赖 WPF 的通用业务基础设施和抽象接口 | `App/AppServices.cs`、`Storage/AtomicFileWriter.cs`、`Settings/SettingsService.cs` |
| `src/Juju.Tools.Json` | JSON 文档的创建、保存、导入、重命名、排序、监听和设置 | `Documents/JsonDocumentService.cs`、`Metadata/JsonMetadataStore.cs` |
| `src/Juju.Platform.Windows` | 仅 Windows 才有的注册表与 Win32 API | `Startup/StartupService.cs`、`HotKey/GlobalShortcutService.cs` |
| `src/Juju.App` | 唯一引用 WPF/WebView2 的桌面宿主，组装依赖并实现 UI | `App.xaml.cs`、`Views/*.xaml`、`Bootstrap/*.cs` |
| `editor` | Monaco 编辑器、编辑器快捷键和跨 WebView 消息协议 | `src/main.ts`、`src/editor.ts`、`src/bridge.ts` |

这是一种由内向外的依赖设计：`Core` 不知道窗口是什么，`Tools.Json` 不知道 WPF 是什么；最外层的 `App` 才把服务和具体窗口连接起来。它对应 Java 中“domain/service 不依赖 Spring MVC”的分层思路。

### 2.2 构建方式

- `src/Juju.App/Juju.App.csproj` 使用 `UseWPF`，并引用 WebView2、DI 和 Logging 包。
- `Juju.App.csproj` 的 `BuildEditor` Target 在 .NET 构建前执行 `editor/npm run build`，把 Vite 输出复制到应用输出目录的 `Editor/`。
- `Juju.Platform.Windows/Juju.Platform.Windows.csproj` 设置 `AllowUnsafeBlocks=true`，因为 `[LibraryImport]` 会生成含指针的 Win32 互操作代码。
- 常用验证命令：

```powershell
dotnet build "Juju.sln" --no-restore
dotnet test "tests\Juju.Tools.Json.Tests\Juju.Tools.Json.Tests.csproj" --no-restore
```

## 3. 应用启动、单实例和退出

入口是 `src/Juju.App/App.xaml` 与 `src/Juju.App/App.xaml.cs`，两者共同组成 `partial class App`。

### 3.1 启动过程

`App.OnStartup` 的当前执行顺序：

1. 用命名 `Mutex` 检查是否为第一个进程。
2. 若已有实例，通过 `NamedPipeClientStream` 发出 `ActivateLauncher`，当前进程退出。
3. 首实例加载设置、创建 `data` 根目录和 JSON 文档服务。
4. 在 `ServiceCollection` 中注册单例，调用 `BuildServiceProvider()`。
5. 加载窗口位置、应用主题、创建并隐藏 Launcher、创建托盘图标。
6. 在后台循环监听命名管道；收到激活消息时经 `Dispatcher.Invoke` 显示 Launcher。

单实例的关键代码在 `App.xaml.cs`：

```csharp
_mutex = new Mutex(true, InstanceName, out var firstInstance);
if (!firstInstance)
{
    await ActivateExistingInstanceAsync();
    Shutdown();
    return;
}
```

这不是 Web 中依赖负载均衡/数据库锁的多实例部署，而是本机进程间协调。命名管道是 Windows 本地 IPC；它相当于“第二个进程通知已运行进程”，而不是 HTTP 请求。

### 3.2 依赖注入 Composition Root

`App.OnStartup` 的 `ServiceCollection` 是 Composition Root，即唯一集中 `new` 基础设施并注册依赖的位置。例如：

```csharp
services.AddSingleton<ISettingsService>(settings);
services.AddSingleton<ISettingsSection, SystemSettingsSection>();
services.AddSingleton<ISettingsSection, JsonSettingsSection>();
services.AddSingleton<IWindowManager>(provider =>
    provider.GetRequiredService<DesktopWindowManager>());
```

与 Spring 的对应关系：

| 这里 | Spring 中的近似概念 |
| --- | --- |
| `ServiceCollection` | `ApplicationContext` 的注册阶段 |
| `AddSingleton<T>()` | 单例 `@Bean` |
| 构造函数参数 | 构造器注入 |
| `GetRequiredService<T>()` | 从容器按类型获取 Bean |
| `ISettingsSection` 的多个注册 | 按接口注入 `List<ISettingsSection>` |

当前代码故意没有使用属性扫描或注解。显式注册能让依赖图直接在 `App.xaml.cs` 中可见。

### 3.3 显式退出与资源释放

`App.xaml` 设置 `ShutdownMode="OnExplicitShutdown"`。所以关闭/隐藏最后一个窗口不会自动结束进程，托盘图标仍能工作；只有 `ExitAsync` 调用 `Application.Shutdown()` 才真正退出。

`DesktopAppLifecycle.ShutdownAsync` 负责统一关停：保存窗口位置与系统设置、停止工具运行时、释放托盘图标、释放 DI 容器。`Interlocked.Exchange(ref _stopping, 1)` 使多次退出请求只执行一次。

## 4. 当前功能如何工作

### 4.1 Launcher、托盘与全局快捷键

- `LauncherWindow.xaml` 是小型工具启动面板，显示 JSON、EchoHttp、Kairos 与设置按钮。
- `LauncherWindow.xaml.cs` 处理工具按钮、按键、自动隐藏和窗口定位。
- `TrayService.cs` 使用 WinForms 的 `NotifyIcon` 创建 Windows 托盘菜单；WPF 没有内置托盘控件，因此这里混用了 WinForms。
- `GlobalShortcutService.cs` 通过 Win32 `RegisterHotKey` 注册全局快捷键，`LauncherWindow.WindowProcedure` 接收 `WM_HOTKEY` 后调用 `Toggle()`。
- `StartupService.cs` 写入当前用户的 `HKCU\...\Run` 注册表项，实现开机启动。

Launcher 显示与隐藏的逻辑不是“重新打开页面”：`DesktopWindowManager.InitializeLauncher()` 创建一次窗口后立即 `Hide()`，后续 `ShowLauncher()` 复用同一个实例。这样全局快捷键的窗口句柄和窗口状态都能保留。

### 4.2 设置窗口

`SettingsWindow.xaml` 将 UI 分为左侧 `TreeView` 与右侧 `ContentControl`。`SettingsWindow.xaml.cs` 接收 DI 注入的 `IEnumerable<ISettingsSection>`，构建“基础”和“工具”两层树；选择叶子节点时将 `section.View` 放到右侧。

设置页是插件式组合：

- `SystemSettingsSection.cs`：开机启动、Launcher 全局快捷键、超时、主题。
- `JsonSettingsSection.cs`：自动保存延迟、缩进、JSON 工具快捷键表格。

新增一个设置页的当前步骤是：实现 `ISettingsSection`，在 `App.xaml.cs` 注册为 `AddSingleton<ISettingsSection, XxxSettingsSection>()`。窗口本身无需添加 `switch` 分支。

### 4.3 JSON 文档管理

用户文件与元数据位于安装目录下：

```text
<应用目录>/config.json                 宿主设置
<应用目录>/data/app/window-state.json  窗口位置
<应用目录>/data/json/settings.json     JSON 工具设置
<应用目录>/data/json/metadata.json     文档元数据
<应用目录>/data/json/documents/*.json  文档内容
<应用目录>/data/json/.trash/           删除后的回收目录
```

`JsonDocumentService` 是 JSON 工具的应用服务。它实现：

- `CreateAsync`：按日期生成 `yyyyMMddNNN.json`，写入 `{}`，再保存元数据。
- `OpenAsync`：返回文本与 `DocumentRevision` 版本戳。
- `SaveAsync`：先比较版本戳，避免覆盖外部程序修改；冲突时抛出 `JujuException(ErrorCode.ExternalModificationConflict)`。
- `RenameAsync`、`DeleteAsync`、`ReorderAsync`：同步维护文件系统和 `metadata.json`。删除移动到 `.trash`，而非直接删除。
- `ImportAsync`：仅导入 `.json`，同名时加 `-2`、`-3` 后缀。
- `ListAsync`：调用 `ReconcileAsync`，以磁盘真实文件修复缺失或损坏的元数据。

`AtomicFileWriter.WriteTextAsync` 使用“写同目录临时文件 -> 强制落盘 -> `File.Replace`/`File.Move` 发布”的方式写文件。这样进程崩溃或取消时，旧文件不会被截断成半个 JSON。

`JsonDocumentWatcher` 包装 `FileSystemWatcher`：它会去抖 150ms、忽略自身原子保存带来的通知，并对真正外部修改发出 `ExternalChanged` 事件。JSON 窗口收到事件后，如果当前内容已保存就自动重载；未保存则提示用户重载、覆盖或复制到剪贴板后重载。

### 4.4 JSON 窗口、WebView2 与 Monaco

`JsonToolWindow.xaml` 负责原生壳：标题栏菜单、文档列表、筛选框、状态栏和 `EditorHost`。`EditorHost` 是空 `Grid`，实际编辑器由 `JsonToolWindow.xaml.cs` 动态加入：

```csharp
var editor = new WebView2();
EditorHost.Children.Add(editor);
await editor.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync());
editor.CoreWebView2.SetVirtualHostNameToFolderMapping(
    "juju.local", Path.Combine(AppContext.BaseDirectory, "Editor"),
    CoreWebView2HostResourceAccessKind.DenyCors);
editor.Source = new Uri("https://juju.local/index.html");
```

这不是远程网页：`SetVirtualHostNameToFolderMapping` 将本地打包后的 `Editor/` 映射为虚拟 `https://juju.local` 源，WebView2 加载其中的 Vite/Monaco 资源。

两端通过 JSON 消息协议通信：

```text
C# JsonToolWindow              TypeScript editor/main.ts
-----------------              --------------------------
PostWebMessageAsJson  ------>  window.chrome.webview message
WebMessageReceived    <------  window.chrome.webview.postMessage
```

- 协议声明在 `editor/src/protocol.ts`。
- TypeScript 的 `Bridge` 在 `editor/src/bridge.ts` 负责收发和运行时 envelope 校验。
- `editor/src/main.ts` 验证命令 payload，分发 `initialize`、`openDocument`、`format`、`enterDiff` 等命令。
- `editor/src/editor.ts` 创建 Monaco model，监听内容、光标和校验标记，并将事件通知 C#。
- C# 的 `SendEditorCommandAsync` 以 `requestId` 建立 `TaskCompletionSource`，把 WebView 的回调事件转成可 `await` 的任务，并设定 10 秒超时。

JSON 窗口的典型保存链路：

```text
Monaco 文本变化
  -> Bridge 发送 contentChanged
  -> JsonToolWindow 更新 _editorContent、SaveState.Dirty，重置 DispatcherTimer
  -> 800ms 后 SaveCurrentAsync
  -> JsonDocumentService.SaveAsync 做版本检查和原子写入
  -> OpenAsync 取得新 Revision，状态栏显示“已保存”
```

菜单、快捷键和按钮最终都进入 `_commands` 字典和 `ExecuteAsync`，避免每种入口复制一套业务调用。Monaco 自己处理保存、格式化和折叠等编辑器命令；Juju 处理新建、复制文件、重命名、删除、对比等跨编辑器功能。

## 5. WPF UI 如何与业务逻辑交互

### 5.1 XAML 是声明式控件树

以 `JsonToolWindow.xaml` 为例：

- `DockPanel` 固定顶部标题栏、底部状态栏，中间内容填充剩余空间。
- 内层 `Grid` 用 `ColumnDefinition Width="220"` 和 `Width="*"` 形成左侧导航、右侧编辑器。
- `x:Name="Documents"` 令编译器在 `JsonToolWindow` 的另一半 `partial` 类中生成强类型字段。
- `ItemsSource = _items` 绑定文档列表；`DataTemplate` 将 `DocumentListItem.DisplayName` 显示为文本。
- `Click="MenuCommand_Click"` 等属性将 WPF 路由事件关联到 code-behind 方法。
- `DynamicResource JujuBackground` 读取应用资源，可在 `ThemeService.Apply` 替换画刷后刷新。

`App.xaml` 的隐式 `Style TargetType="Button"` 会作用于未指定其他样式的所有按钮。窗口自身定义的带 `x:Key` 样式只在该窗口资源范围内有效。

### 5.2 事件驱动而非 Controller

下面是当前项目常见的事件来源与处理位置：

| 事件来源 | 处理位置 | 后续动作 |
| --- | --- | --- |
| `Button.Click` | `LauncherWindow.Tool_Click`、`SettingsWindow.Save_Click` | 解析工具/页面，调用窗口管理或保存服务 |
| `ListBox.SelectionChanged` | `JsonToolWindow.Documents_SelectionChanged` | 保存旧文档，再打开新文档 |
| 文本编辑 | `JsonToolWindow.OnWebMessage` 的 `contentChanged` | 标脏、启动自动保存定时器 |
| WebView2 回调 | `JsonToolWindow.OnWebMessage` | 校验协议、完成请求或更新状态栏 |
| Windows 热键消息 | `LauncherWindow.WindowProcedure` | 切换 Launcher 可见性 |
| 文件系统事件 | `JsonDocumentWatcher` -> `OnExternalDocumentChanged` | 重载/冲突恢复 |
| 托盘菜单 | `TrayService` | 显示窗口、打开工具或退出 |

WPF 事件处理器通常是 `void`，因此项目中可见 `async void` 或 `_ = SomeAsyncMethod()`。这在普通业务方法中应避免，但在框架要求 `void` 的事件边界是合理的。实际异步逻辑被放入 `Task` 方法，例如 `SaveCurrentAsync`、`OpenSelectedAsync`，以保留异常处理和可组合性。

### 5.3 UI 线程与 Dispatcher

WPF 控件不是线程安全的，只能由创建它们的 Dispatcher/UI 线程操作。文件监听和命名管道是后台回调，所以要切回 UI 线程：

```csharp
await Dispatcher.InvokeAsync(async () =>
{
    SaveStateStatus.Text = "文件已在外部更新";
    await ReloadDiskVersionAsync();
});
```

位置：`JsonToolWindow.OnExternalDocumentChanged`。这相当于 JavaFX 的 `Platform.runLater` 或 Swing 的 `SwingUtilities.invokeLater`，不是 Spring Web 里“随意在工作线程写响应对象”。

## 6. C# 语法速查：与 Java 的区别

### 6.1 `record`、`with` 和值相等性

位置：`AppSettings.cs`、`JsonDocumentModels.cs`、`ToolModels.cs`、`WindowStateService.cs`。

```csharp
public sealed record AppSettings(bool StartAtLogin = false, ...);
var value = _settings.Current with { Theme = ThemePreference.Dark };
```

- C# `record` 很接近 Java `record`：编译器生成只读属性、构造函数、基于内容的 `Equals`/`GetHashCode`。
- `with` 会创建副本并只替换指定属性；不修改原对象。
- `record struct` 是值类型版本，例如 `JsonDocumentId`。它通常避免对象堆分配，但应避免把很大的可变数据放入结构体。
- `init` 属性只能在对象创建阶段或 `with` 中赋值；见 `JsonToolSettings.Shortcuts`。

### 6.2 主构造函数

位置：`DesktopAppLifecycle.cs`、`DesktopWindowManager.cs`、`JsonMetadataStore.cs`、`JsonSettingsSection.cs`。

```csharp
public sealed class DesktopAppLifecycle(
    IServiceProvider services,
    DesktopWindowManager windows,
    ILogger<DesktopAppLifecycle> log) : IAppLifecycle
{ ... }
```

这是 C# 的 primary constructor。构造参数直接写在类型名后，类体可以直接使用它们。Java 没有完全相同的普通 class 语法；可理解为把“构造器参数 + private final 字段赋值”的样板代码压缩了。

### 6.3 Nullable Reference Types 与模式匹配

位置：`JsonToolWindow.xaml.cs`、`LauncherWindow.xaml.cs`、`App.xaml.cs`。

```csharp
private WebView2? _editor;
if (_editor is not null) { ... }
if (sender is Button { Tag: string value } &&
    Enum.TryParse<ToolId>(value, out var tool)) { ... }
```

- `?` 表示引用可能为 null，编译器进行静态空引用分析；它不同于 Java 的 `Optional`，运行时通常仍是普通引用。
- `is not null`、`is Type variable`、`Button { Tag: string value }` 是模式匹配，可同时判断类型、null 和属性结构。
- `null!` 位于测试字段，告诉编译器“我保证后续初始化”，只抑制警告，不会在运行时创建对象。

### 6.4 集合表达式、范围和 LINQ

位置：`JsonToolSettings.cs`、`JsonDocumentService.cs`、`JsonToolWindow.xaml.cs`。

```csharp
IReadOnlyList<DocumentListItem> items = [];
var copy = [.. metadata.Documents, document];
var modifiers = parts[..^1];
var names = list.Where(x => x.Enabled).Select(x => x.Name).ToArray();
```

- `[]` 是 C# 12 collection expression，可创建目标类型集合。
- `[.. source]` 表示展开/复制集合。
- `[..^1]` 是范围表达式，表示“从开头到倒数第一个之前”；`^1` 是从尾部计数的 Index。
- LINQ 的 `Where/Select/OrderBy/SingleOrDefault` 类似 Java Stream，但常直接对 `IEnumerable<T>` 惰性执行。需要快照时调用 `ToArray()` 或 `ToList()`。

### 6.5 异步、取消与并发控制

位置：`JsonDocumentService.cs`、`AtomicFileWriter.cs`、`App.xaml.cs`。

```csharp
public async Task SaveAsync(..., CancellationToken cancellationToken = default)
{
    await _operations.WaitAsync(cancellationToken);
    try { ... }
    finally { _operations.Release(); }
}
```

- `Task` 对应 Java `CompletableFuture<Void>`；`Task<T>` 对应 `CompletableFuture<T>`。
- `async`/`await` 不是新线程。文件 I/O 等待期间通常不阻塞 UI 线程，完成后默认恢复到原同步上下文。
- `CancellationToken` 是协作式取消：调用方发出取消信号，底层 API 或代码必须主动检查/传递它；它不像 `Thread.interrupt()` 那样强行停止执行。
- `SemaphoreSlim` 是可跨 `await` 持有的异步锁。不要在 `await` 周围使用 `lock`；`JsonDocumentService` 用它保护“文件内容 + 元数据”的跨文件不变量。
- `TaskCompletionSource<T>` 可把回调风格 API 转成 `await`；`JsonToolWindow.SendEditorCommandAsync` 用它等待 WebView 回复。
- `TaskCreationOptions.RunContinuationsAsynchronously` 避免设置结果的线程同步执行等待方续体，降低重入和死锁风险。
- `Interlocked.Exchange` 是无锁原子操作，见 `DesktopAppLifecycle` 的一次性关停。

### 6.6 资源管理：`using`、`await using`、`IDisposable`

位置：`AtomicFileWriter.cs`、`TrayService.cs`、`JsonDocumentService.cs`。

```csharp
using var stream = ...;
await using var pipe = ...;
await _documents.DisposeAsync();
```

- `using` / `using var` 对应 Java try-with-resources，调用 `IDisposable.Dispose()`。
- `await using` 对应异步版本，调用 `IAsyncDisposable.DisposeAsync()`。
- WPF `WebView2`、托盘图标、文件流、`FileSystemWatcher` 等均持有操作系统资源，必须在关闭路径释放。
- `JsonDocumentService` 实现 `IAsyncDisposable`，释放观察器后再释放异步锁。

### 6.7 事件、委托与 lambda

位置：`ThemeService.cs`、`JsonDocumentService.cs`、各窗口的构造函数。

```csharp
public event Action<EffectiveTheme>? Changed;
_autosave.Tick += async (_, _) => await SaveCurrentAsync(_editorContent);
Changed?.Invoke(theme);
```

- 委托是类型安全的函数引用；`Action<T>` 表示无返回值的回调，`Func<T, TResult>` 表示有返回值的回调。
- `event` 限制外部只能 `+=`/`-=` 订阅，只有声明类可触发；语义接近 Java 的 listener 列表封装。
- `?.Invoke(...)` 表示订阅者不为 null 才调用。
- `_` 是未使用参数的约定名称，不是特殊关键字。

### 6.8 `partial`、别名和静态类

位置：`App.xaml.cs`、所有 `*.xaml.cs`、`GlobalShortcutService.cs`。

- `partial class` 允许同一类型分布在多个源文件。XAML 编译器生成一半 `App`/`Window` 类，手写 C# 是另一半，`InitializeComponent()` 就来自生成部分。
- `using WpfClipboard = System.Windows.Clipboard;` 是类型别名，用来解决同名类型冲突，类似 Java 中无法 import 同名类时只能使用全限定名的改进写法。
- `static class ApplicationPaths` 不能实例化，作用接近 Java 工具类；`AppContext.BaseDirectory` 给出应用输出目录。

### 6.9 对象初始化器、switch 表达式、元组与 `out var`

位置：`TrayService.cs`、`JsonToolWindow.xaml.cs`、`WindowStateService.cs`、`GlobalShortcutService.cs`。

```csharp
var icon = new NotifyIcon { Text = "juju", Visible = true };

SaveStateStatus.Text = _saveState switch
{
    SaveState.Clean => "已保存",
    SaveState.Dirty or SaveState.Saving => "未保存",
    _ => "保存失败"
};

foreach (var (key, window) in windows) { ... }
if (Enum.TryParse<ToolId>(value, out var tool)) { ... }
```

- 对象初始化器 `{ Property = value }` 在构造后设置可写属性，避免 Java Bean 的多次 setter 调用；`JsonSettingsSection` 也用它动态创建 WPF 控件。
- `switch` 表达式会产生一个值。`or` 是模式组合，`_` 是默认分支；它通常比 Java 传统 `switch` 赋值更紧凑。
- `(string Key, Window? Window)` 是具名值元组；`foreach (var (key, window) in windows)` 是解构。见 `WindowStateService.CaptureAsync`。
- `out var tool` 在调用点声明 out 参数变量，见 `LauncherWindow.Tool_Click`；Java 通常以返回包装对象或可变参数实现类似需求。
- `nameof(Shortcut)` 生成重命名安全的成员名字符串，见 `JsonSettingsSection.ShortcutRow` 的属性变更通知。

### 6.10 异常过滤器和领域错误码

位置：`JsonDocumentService.cs`、`JsonMetadataStore.cs`、`JsonToolWindow.xaml.cs`。

```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    throw new JujuException(ErrorCode.DocumentWriteFailed, "...", ex);
}
```

`when` 是 catch filter。它只拦截指定情况，其他异常如 `OperationCanceledException` 会自然向上传播。`JujuException + ErrorCode` 类似 Java 中自定义业务异常 + 错误码枚举，UI 可针对“外部修改冲突”和“普通保存失败”展示不同恢复动作。

### 6.11 Win32 P/Invoke

位置：`GlobalShortcutService.cs`。

```csharp
[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
```

- `[LibraryImport]` 是 C# attribute，源生成器据此产生调用 DLL 的互操作代码。
- `IntPtr` 表示原生指针/句柄大小的整数，类似 Java JNA/JNI 中处理 native handle 的场景。
- `SetLastError=true` 后立刻调用 `Marshal.GetLastWin32Error()` 读取 Windows 错误码。
- 此类声明为 `partial`，因为互操作源生成器会补上方法实现。

## 7. XAML / WPF 概念速查

| 概念 | 当前代码 | Java 侧类比 |
| --- | --- | --- |
| `Window` | `LauncherWindow.xaml`、`JsonToolWindow.xaml` | JavaFX `Stage`，但有 Windows 原生窗口语义 |
| `Grid` | 各窗口布局根节点 | CSS Grid 的列/行布局 |
| `DockPanel` | JSON 窗口的标题栏和状态栏 | BorderLayout：Top/Bottom/Center |
| `StackPanel` | Launcher 工具按钮、设置控件 | 垂直/水平 BoxLayout |
| `TreeView`、`ListBox` | 设置导航、JSON 文档列表 | JavaFX TreeView/ListView |
| `{Binding Name}` | `SettingsWindow.xaml`、文档项模板 | 数据绑定表达式，不是字符串插值 |
| `DataTemplate` | `JsonToolWindow.xaml` 的列表项 | JavaFX cell factory 的声明式版本 |
| `Style` / `ControlTemplate` | Launcher/JSON 的自绘按钮 | CSS 主题 + 自定义组件渲染结构 |
| `DynamicResource` | `LauncherWindow.xaml` 的背景 | 可运行时更新的主题变量 |
| `WindowChrome` | Launcher/JSON 窗口 | 自绘标题栏，同时保留系统拖动、缩放、贴靠 |
| 路由事件 | `Click`、`PreviewKeyDown` | 事件捕获/冒泡；`Preview*` 类似 capture 阶段 |

当前窗口不是纯绑定式 UI：例如 `JsonToolWindow` 直接设置 `SaveStateStatus.Text`，并直接写 `Documents.ItemsSource`。这在快速构建桌面工具时可行；若迁移到 MVVM，下一步应是让 `DocumentListItem`、保存状态和命令成为 ViewModel 属性，通过 `INotifyPropertyChanged` 和 `ICommand` 绑定，而非直接查找/设置控件。

## 8. 从一次点击到一次保存：完整调用链

以用户在 JSON 工具中选择文件并编辑为例：

1. WPF `ListBox.SelectionChanged` 调用 `JsonToolWindow.Documents_SelectionChanged`。
2. `OpenSelectedAsync` 先 `FlushCurrentAsync`，必要时保存旧文件，再调用 `_documents.OpenAsync`。
3. `JsonDocumentService.OpenAsync` 读取文本和 `DocumentRevision`，返回 `JsonDocumentSnapshot`。
4. 窗口调用 `SendEditorCommandAsync("openDocument", ...)`。
5. C# 将带 `version/type/requestId/payload` 的 JSON 投递给 WebView2。
6. `editor/src/main.ts` 校验 payload 并调用 `EditorAdapter.openDocument`。
7. Monaco 创建带 `inmemory://` URI 的 JSON model 并显示内容。
8. 用户输入后，Monaco 的 `onDidChangeModelContent` 发 `contentChanged`。
9. C# 标记 `Dirty`，重启 `DispatcherTimer`。延迟到期或用户保存后，`SaveCurrentAsync` 进入 `JsonDocumentService.SaveAsync`。
10. 服务使用版本戳进行乐观锁检查、原子写入内容、更新元数据、登记自身写入版本，窗口重新打开文件取得新版本戳并展示“已保存”。

这条链路的职责边界：

- XAML：控件放在哪里、事件由谁接收。
- `JsonToolWindow`：UI 会话状态、用户交互、WebView 协议。
- `JsonDocumentService`：持久化业务规则与一致性。
- `JsonMetadataStore` / `AtomicFileWriter`：底层文件可靠性。
- TypeScript `EditorAdapter`：浏览器内 Monaco 行为。

## 9. 当前测试覆盖什么

`tests/Juju.Tools.Json.Tests/JsonDocumentServiceTests.cs` 使用 xUnit 和真实临时目录测试 JSON 领域层，而不是测试 WPF 视觉界面。覆盖内容包括：

- 创建、保存的外部版本冲突、重命名、删除到回收目录和排序。
- 安全文件名、导入重名后缀、导入失败不留下半成品。
- 损坏元数据备份和磁盘-元数据协调恢复。
- 文件观察器过滤自身保存、报告真实外部修改。
- 快捷键规范化、上下文冲突和保留组合键。
- 原子写入在取消后保留旧文件。

当前没有自动化 WPF UI 或 WebView2 端到端测试。因此 UI 修改后仍应手动验证：Launcher 全局快捷键、托盘菜单、设置保存/隐藏/再次打开、JSON 编辑器加载、自动保存、外部修改冲突、拖放导入和对比模式。

## 10. 推荐阅读顺序

1. `src/Juju.App/App.xaml.cs`：理解 WPF 启动、DI 和单实例。
2. `src/Juju.App/Bootstrap/DesktopWindowManager.cs`：理解桌面窗口为什么创建一次、隐藏复用。
3. `src/Juju.App/Views/LauncherWindow.xaml` 和 `.xaml.cs`：从最简单的 XAML 事件开始。
4. `src/Juju.App/Views/SettingsWindow.xaml` 和 `.xaml.cs`：学习绑定、模板和动态组合设置页。
5. `src/Juju.Tools.Json/Documents/JsonDocumentModels.cs`：学习 `record`、`enum` 和领域数据。
6. `src/Juju.Tools.Json/Documents/JsonDocumentService.cs`：学习异步 I/O、取消、锁、错误码和一致性。
7. `src/Juju.App/Views/JsonToolWindow.xaml`：学习复杂窗口布局和 `WindowChrome`。
8. `src/Juju.App/Views/JsonToolWindow.xaml.cs`：学习事件到业务服务的完整协调。
9. `editor/src/protocol.ts`、`bridge.ts`、`editor.ts`：理解 WebView2 与 Monaco 的另一端。
10. `tests/Juju.Tools.Json.Tests/JsonDocumentServiceTests.cs`：通过测试反向确认领域规则。

## 11. 新增功能时放在哪里

| 新需求 | 优先位置 |
| --- | --- |
| 新 JSON 文档规则/文件操作 | `Juju.Tools.Json` 的 `Documents`、`Metadata`、`Storage` |
| 所有工具都可用的存储/设置/错误能力 | `Juju.Core` |
| Windows 注册表、热键、系统 API | `Juju.Platform.Windows` |
| 新窗口、WPF 控件、托盘菜单、WebView 宿主 | `Juju.App` |
| Monaco 编辑器内部行为和协议 | `editor/src` |
| JSON 业务行为回归测试 | `tests/Juju.Tools.Json.Tests` |

判断原则是“依赖方向”：若代码必须 `using System.Windows`，它不应进入 `Core` 或 `Tools.Json`；若代码是 JSON 规则且不需要控件，就不要放进 `JsonToolWindow.xaml.cs`。
