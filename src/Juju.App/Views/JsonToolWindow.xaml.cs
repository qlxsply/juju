using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Juju.App.Bootstrap;
using Juju.Core.Errors;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Minify;
using Juju.Tools.Json.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Juju.App.Views;

// 存储和 Windows 剪贴板所有权位于视图之外；宿主可提供此适配器，避免窗口直接依赖具体文件实现。
public interface IJsonFileClipboard
{
    Task CopySnapshotAsync(JsonDocumentSnapshot snapshot, string fileName,
        CancellationToken cancellationToken = default);

    Task CleanExpiredSnapshotsAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default);
}

// XAML 编译生成的 partial 部分提供 EditorHost、Documents 等控件字段；本文件负责交互与状态协调。
// WebView2 承载 JavaScript 编辑器，因此该窗口还充当 WPF 与网页消息协议之间的边界。
public partial class JsonToolWindow : Window
{
    private const int ProtocolVersion = 1;
    private readonly JsonDocumentService _documents;
    private readonly IJsonToolSettingsService _settings;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly SemaphoreSlim _documentGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<BridgeMessage>> _requests = [];
    private readonly Dictionary<JsonToolCommand, Func<Task>> _commands;
    private readonly CancellationTokenSource _lifetime = new();
    private WebView2? _editor;
    private JsonDocumentSnapshot? _current;
    private IReadOnlyList<DocumentListItem> _items = [];
    private string _editorContent = string.Empty;
    private SaveState _saveState = SaveState.Clean;
    private bool _jsonValid = true;
    private bool _editorReady;
    private EffectiveTheme _theme = EffectiveTheme.Light;
    private bool _initializing;
    private bool _selecting;
    private bool _closing;
    private bool _inDiffMode;
    private int _foldDepth;
    private int _generation;
    private int _request;
    private long _contentVersion;
    private System.Windows.Point _dragStart;
    private bool _largeDocument;
    private string? _pendingShortcut;
    private DateTime _pendingShortcutAt;

    // 依赖由 DesktopWindowManager 组装。文档服务负责持久化，窗口仅维护当前 UI 会话状态。
    public JsonToolWindow(JsonDocumentService documents, IJsonToolSettingsService settings)
    {
        _documents = documents;
        _settings = settings;
        _autosave.Interval = TimeSpan.FromMilliseconds(settings.Current.AutosaveDelayMilliseconds);
        _commands = new()
        {
            [JsonToolCommand.New] = NewAsync,
            [JsonToolCommand.Save] = SaveFromEditorAsync,
            [JsonToolCommand.Format] = FormatAsync,
            [JsonToolCommand.CopyText] = CopyTextAsync,
            [JsonToolCommand.CopyMinified] = CopyMinifiedAsync,
            [JsonToolCommand.CopyFile] = CopyFileAsync,
            [JsonToolCommand.FoldAll] = FoldAllAsync,
            [JsonToolCommand.UnfoldAll] = UnfoldAllAsync,
            [JsonToolCommand.UnfoldLevel] = UnfoldLevelAsync,
            [JsonToolCommand.EnterDiff] = EnterDiffAsync,
            [JsonToolCommand.ExitDiff] = ExitDiffAsync,
            [JsonToolCommand.Rename] = RenameAsync,
            [JsonToolCommand.Delete] = DeleteAsync,
        };
        InitializeComponent();
        // WPF 事件委托是 void；这些 async lambda 是事件边界，内部 await 保持 UI 响应。
        Loaded += async (_, _) => await EnsureEditorAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && !_closing) await EnsureEditorAsync();
        };
        Drop += async (_, eventArgs) => await ImportDropAsync(eventArgs);
        PreviewKeyDown += OnShortcut;
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            await SaveCurrentAsync(_editorContent);
        };
        _documents.ExternalChanged += OnExternalDocumentChanged;
    }

    // 在窗口管理器注册 Core 所有的快照实现前保持可选，视图不自行创建跨层基础设施。
    public IJsonFileClipboard? FileClipboard { get; set; }

    // Changed 事件没有 Task 返回值，故在编辑器就绪时有意 fire-and-forget；失败由异步方法自身吞并。
    public void ApplyTheme(EffectiveTheme theme)
    {
        _theme = theme;
        if (_editorReady && !_closing) _ = ApplyThemeAsync(theme);
    }

    // 显式应用关停与用户点击关闭不同：前者必须刷新内容、销毁 WebView2 并允许真正 Close。
    public async Task ShutdownAsync()
    {
        _closing = true;
        try
        {
            await FlushCurrentAsync();
            DestroyEditor();
            Close();
        }
        finally
        {
            _documents.ExternalChanged -= OnExternalDocumentChanged;
            _closing = false;
            _lifetime.Cancel();
        }
    }

    private async Task ApplyThemeAsync(EffectiveTheme theme)
    {
        try
        {
            await SendEditorCommandAsync("setTheme", new { theme = theme == EffectiveTheme.Dark ? "vs-dark" : "vs" });
        }
        catch (Exception)
        {
        }
    }

    // WebView2 创建代价高，窗口隐藏时延迟初始化；多个生命周期事件可同时触发，标志位防止重复创建。
    private async Task EnsureEditorAsync()
    {
        if (_editor is not null || _initializing || _closing || !IsVisible) return;
        _initializing = true;
        try
        {
            var editor = new WebView2();
            _editor = editor;
            EditorHost.Children.Add(editor);
            await editor.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync());
            if (!ReferenceEquals(editor, _editor)) return;
            // 虚拟 HTTPS 主机让本地编辑器资源拥有稳定 origin，同时 DenyCors 限制跨域访问。
            editor.CoreWebView2.SetVirtualHostNameToFolderMapping("juju.local",
                Path.Combine(AppContext.BaseDirectory, "Editor"), CoreWebView2HostResourceAccessKind.DenyCors);
            editor.CoreWebView2.WebMessageReceived += OnWebMessage;
            editor.CoreWebView2.ProcessFailed += OnProcessFailed;
            editor.Source = new Uri("https://juju.local/index.html");
            await RefreshDocumentsAsync();
            if (FileClipboard is not null)
            {
                try
                {
                    await FileClipboard.CleanExpiredSnapshotsAsync(TimeSpan.FromDays(3), _lifetime.Token);
                }
                catch
                {
                    /* Snapshot cleanup must not prevent opening the editor. */
                }
            }
        }
        catch (Exception ex)
        {
            SaveStateStatus.Text = "编辑器初始化失败: " + ex.Message;
            DestroyEditor();
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task RefreshDocumentsAsync()
    {
        var list = await _documents.ListAsync(_lifetime.Token);
        if (list.Count == 0) list = [await _documents.CreateAsync(_lifetime.Token)];
        _items = list.Select(item =>
            new DocumentListItem(item.Id, item.FileName, Path.GetFileNameWithoutExtension(item.FileName))).ToArray();
        Documents.ItemsSource = _items;
        var selected = _current is { } current
            ? _items.SingleOrDefault(item => item.Id == current.DocumentId)
            : _items[0];
        _selecting = true;
        Documents.SelectedItem = selected ?? _items[0];
        _selecting = false;
        if (_current is null) await OpenSelectedAsync((DocumentListItem)Documents.SelectedItem);
    }

    // SelectionChanged 是 WPF 事件入口；_selecting 排除代码恢复选中项时产生的递归处理。
    private async void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_selecting && Documents.SelectedItem is DocumentListItem document) await OpenSelectedAsync(document);
    }

    // SemaphoreSlim 串行化切换文档与保存，避免快速点击时旧异步操作覆盖新选择。
    private async Task OpenSelectedAsync(DocumentListItem document)
    {
        await _documentGate.WaitAsync(_lifetime.Token);
        try
        {
            if (_current?.DocumentId == document.Id) return;
            if (!await FlushCurrentAsync())
            {
                RestoreCurrentSelection();
                return;
            }

            _inDiffMode = false;
            _current = await _documents.OpenAsync(document.Id, _lifetime.Token);
            _editorContent = _current.Content;
            _saveState = SaveState.Clean;
            _jsonValid = IsJson(_editorContent);
            _largeDocument = Encoding.UTF8.GetByteCount(_editorContent) > 10 * 1024 * 1024;
            _foldDepth = 0;
            UpdateStatus();
            if (_largeDocument) SaveStateStatus.Text = "大文件模式：部分操作可能耗时较长";
            if (_editorReady)
                await SendEditorCommandAsync("openDocument",
                    new { documentId = _current.DocumentId.ToString(), content = _current.Content, language = "json" });
        }
        catch (Exception ex)
        {
            SaveStateStatus.Text = "打开失败: " + ex.Message;
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task<bool> FlushCurrentAsync()
    {
        _autosave.Stop();
        if (_current is null || _saveState == SaveState.Clean) return true;
        try
        {
            if (_editorReady && !_inDiffMode) _editorContent = await GetEditorContentAsync();
            await SaveCurrentAsync(_editorContent);
            return _saveState == SaveState.Clean;
        }
        catch
        {
            return false;
        }
    }

    private void RestoreCurrentSelection()
    {
        if (_current is null) return;
        _selecting = true;
        Documents.SelectedItem = _items.SingleOrDefault(item => item.Id == _current.DocumentId);
        _selecting = false;
    }

    // 网页到宿主的事件入口。协议先验证版本和消息形状，再按类型更新 WPF 会话状态。
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            if (!BridgeMessage.TryParse(document.RootElement, out var message)) throw new InvalidDataException();
            switch (message.Type)
            {
                case "ready":
                    _editorReady = true;
                    // WebMessageReceived 不能 await；初始化独立运行，错误会写入状态栏。
                    _ = InitializeBridgeAsync();
                    break;
                case "contentChanged":
                    if (TryGetContent(message.Payload, out var content) && !_inDiffMode)
                    {
                        _editorContent = content;
                        _contentVersion++;
                        _saveState = SaveState.Dirty;
                        _foldDepth = 0;
                        UpdateStatus();
                        _autosave.Stop();
                        _autosave.Start();
                    }

                    break;
                case "cursorChanged":
                    if (TryGetPosition(message.Payload, out var line, out var column))
                        CursorStatus.Text = $"Ln {line}, Col {column}";
                    break;
                case "validationChanged":
                    if (TryGetValidation(message.Payload, out var valid))
                    {
                        _jsonValid = valid;
                        UpdateStatus();
                    }

                    break;
                case "saveRequested":
                    // 编辑器协议是通知式的，不能阻塞 WebView 事件分发，因此保存有意不等待。
                    if (TryGetContent(message.Payload, out var requestedContent))
                        _ = SaveCurrentAsync(requestedContent);
                    break;
                case "shortcut":
                    if (message.Payload.TryGetProperty("combination", out var shortcut) &&
                        shortcut.ValueKind == JsonValueKind.String)
                        ExecuteShortcut(shortcut.GetString(),
                            _inDiffMode ? JsonToolShortcutContext.Diff : JsonToolShortcutContext.Editor);
                    break;
                case "commandResult":
                    if (message.RequestId is not null && _requests.Remove(message.RequestId, out var request))
                        request.TrySetResult(message);
                    break;
            }
        }
        catch
        {
            SaveStateStatus.Text = "编辑器消息无效";
        }
    }

    private async Task InitializeBridgeAsync()
    {
        try
        {
            var shortcuts = _settings.Current.Shortcuts;
            await SendEditorCommandAsync("initialize", new
            {
                theme = _theme == EffectiveTheme.Dark ? "vs-dark" : "vs",
                indentSize = _settings.Current.IndentSize,
                monacoBindings = JsonToolShortcuts.Definitions
                    .Where(definition => definition.Source == JsonToolShortcutSource.Monaco)
                    .Select(definition => new
                        { command = definition.Command.ToString(), shortcut = shortcuts[definition.Command] })
                    .ToArray(),
                jujuBindings = JsonToolShortcuts.Definitions
                    .Where(definition => definition.Source == JsonToolShortcutSource.Juju)
                    .Select(definition => new
                    {
                        command = definition.Command.ToString(), shortcut = shortcuts[definition.Command],
                        context = definition.Context.ToString()
                    })
                    .ToArray(),
            });
            if (_current is not null)
                await SendEditorCommandAsync("openDocument",
                    new { documentId = _current.DocumentId.ToString(), content = _current.Content, language = "json" });
            UpdateStatus();
        }
        catch (Exception ex)
        {
            SaveStateStatus.Text = "编辑器连接失败: " + ex.Message;
        }
    }

    // 每条命令带 requestId；TaskCompletionSource 将回调式 WebMessageReceived 转换为可 await 的 Task。
    private async Task SendEditorCommandAsync(string type, object payload)
    {
        if (!_editorReady || _editor?.CoreWebView2 is null) throw new InvalidOperationException("编辑器尚未就绪。");
        var requestId = $"{_generation}:{Interlocked.Increment(ref _request)}";
        // 避免在 WebView 回调线程内同步续接，降低重入和死锁风险，类似 Java CompletableFuture 的异步续接。
        var request = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Add(requestId, request);
        try
        {
            _editor.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                { version = ProtocolVersion, type, requestId, payload }));
            var response = await request.Task.WaitAsync(TimeSpan.FromSeconds(10), _lifetime.Token);
            if (!TryGetCommandResult(response.Payload, out var error) && error is not null)
                throw new InvalidOperationException(error);
        }
        finally
        {
            _requests.Remove(requestId);
        }
    }

    private async Task<string> GetEditorContentAsync()
    {
        if (!_editorReady || _editor?.CoreWebView2 is null) return _editorContent;
        var requestId = $"{_generation}:{Interlocked.Increment(ref _request)}";
        var request = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Add(requestId, request);
        try
        {
            _editor.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                { version = ProtocolVersion, type = "getContent", requestId, payload = new { } }));
            var response = await request.Task.WaitAsync(TimeSpan.FromSeconds(10), _lifetime.Token);
            if (!TryGetCommandResult(response.Payload, out var error))
                throw new InvalidOperationException(error ?? "编辑器未返回文本。");
            return TryGetContent(response.Payload.GetProperty("result"), out var content)
                ? content
                : throw new InvalidOperationException("编辑器返回了无效文本。");
        }
        finally
        {
            _requests.Remove(requestId);
        }
    }

    private async Task SaveFromEditorAsync()
    {
        _autosave.Stop();
        await SaveCurrentAsync(await GetEditorContentAsync());
    }

    // 保存门保证只有一个持久化操作；contentVersion 防止较早保存完成后错误标记较新编辑为干净。
    private async Task SaveCurrentAsync(string content)
    {
        var session = _current;
        if (session is null || _inDiffMode) return;
        var contentVersion = _contentVersion;
        await _saveGate.WaitAsync(_lifetime.Token);
        try
        {
            _saveState = SaveState.Saving;
            UpdateStatus();
            await _documents.SaveAsync(session.DocumentId, content, session.Revision, _lifetime.Token);
            var refreshed = await _documents.OpenAsync(session.DocumentId, _lifetime.Token);
            if (_current?.DocumentId == session.DocumentId)
            {
                _current = refreshed;
                if (_contentVersion == contentVersion)
                {
                    _editorContent = refreshed.Content;
                    _saveState = SaveState.Clean;
                }
                else
                {
                    _saveState = SaveState.Dirty;
                    _autosave.Stop();
                    _autosave.Start();
                }

                UpdateStatus();
            }
        }
        catch (JujuException ex) when (ex.Code == ErrorCode.ExternalModificationConflict)
        {
            _saveState = SaveState.Conflict;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _saveState = SaveState.SaveFailed;
            SaveStateStatus.Text = "保存失败: " + ex.Message;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task NewAsync()
    {
        if (!await FlushCurrentAsync()) return;
        var created = await _documents.CreateAsync(_lifetime.Token);
        await RefreshDocumentsAsync();
        Documents.SelectedItem = _items.Single(item => item.Id == created.Id);
    }

    private async Task FormatAsync()
    {
        if (_inDiffMode) return;
        if (!_jsonValid)
        {
            SaveStateStatus.Text = "JSON 格式错误，无法格式化";
            return;
        }

        await SendEditorCommandAsync("format", new { });
    }

    private async Task CopyTextAsync() => WpfClipboard.SetText(await GetEditorContentAsync());

    private async Task CopyMinifiedAsync()
    {
        try
        {
            WpfClipboard.SetText(JsonLexicalMinifier.Minify(await GetEditorContentAsync()));
        }
        catch (JujuException)
        {
            SaveStateStatus.Text = "JSON 格式错误，无法压缩复制";
        }
    }

    private async Task CopyFileAsync()
    {
        if (FileClipboard is null || _current is null)
        {
            SaveStateStatus.Text = "文件剪贴板需要宿主快照适配器";
            return;
        }

        if (!await FlushCurrentAsync()) return;
        await FileClipboard.CopySnapshotAsync(_current, _items.Single(item => item.Id == _current.DocumentId).FileName,
            _lifetime.Token);
        SaveStateStatus.Text = "已复制 JSON 文件";
    }

    private async Task UnfoldAllAsync()
    {
        _foldDepth = 0;
        await SendEditorCommandAsync("unfoldAll", new { });
    }

    private async Task FoldAllAsync()
    {
        _foldDepth = 0;
        await SendEditorCommandAsync("foldAll", new { });
    }

    private async Task UnfoldLevelAsync()
    {
        _foldDepth = _foldDepth == 7 ? 1 : _foldDepth + 1;
        await SendEditorCommandAsync("unfoldLevel", new { level = _foldDepth });
        SaveStateStatus.Text = $"已展开至第 {_foldDepth} 层";
    }

    // 对比模式将 Web 编辑器切为只读比较器，并暂停自动保存以保护当前文档。
    private async Task EnterDiffAsync()
    {
        if (_diffOriginal is null || _current is null || _diffOriginal.Id == _current.DocumentId)
        {
            _diffOriginal = _current is null ? null : _items.Single(item => item.Id == _current.DocumentId);
            SaveStateStatus.Text = "已选择左侧文档，请选择另一份文档后再次执行对比";
            return;
        }

        var currentContent = await GetEditorContentAsync();
        var original = await _documents.OpenAsync(_diffOriginal.Id, _lifetime.Token);
        await SendEditorCommandAsync("enterDiff",
            new
            {
                original = new { documentId = original.DocumentId.ToString(), content = original.Content },
                modified = new { documentId = _current.DocumentId.ToString(), content = currentContent }
            });
        _inDiffMode = true;
        _autosave.Stop();
        SaveStateStatus.Text = IsJson(currentContent) && IsJson(original.Content) ? "只读对比" : "存在非法 JSON，当前按原文比较";
    }

    private async Task ExitDiffAsync()
    {
        if (!_inDiffMode) return;
        await SendEditorCommandAsync("exitDiff", new { });
        _inDiffMode = false;
        if (_current is not null)
            await SendEditorCommandAsync("openDocument",
                new { documentId = _current.DocumentId.ToString(), content = _editorContent, language = "json" });
        UpdateStatus();
    }

    private async Task RenameAsync()
    {
        if (_current is null) return;
        var name = PromptForName(
            Path.GetFileNameWithoutExtension(_items.Single(item => item.Id == _current.DocumentId).FileName));
        if (name is null) return;
        try
        {
            await _documents.RenameAsync(_current.DocumentId, name, _lifetime.Token);
            await RefreshDocumentsAsync();
        }
        catch (JujuException ex)
        {
            SaveStateStatus.Text = "重命名失败: " + ex.Message;
        }
    }

    private async Task DeleteAsync()
    {
        if (_current is null ||
            WpfMessageBox.Show("删除当前文档？", "juju JSON", MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes) return;
        await _documents.DeleteAsync(_current.DocumentId, _lifetime.Token);
        _current = null;
        await RefreshDocumentsAsync();
    }

    private DocumentListItem? _diffOriginal;

    private void DocumentFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = DocumentFilter.Text.Trim();
        Documents.ItemsSource = string.IsNullOrEmpty(filter)
            ? _items
            : _items.Where(item => item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // UI 事件不能 await；命令入口统一 fire-and-forget 到 ExecuteAsync，由其集中显示异常。
    private void Documents_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _ = ExecuteAsync(JsonToolCommand.Rename);

    private void Documents_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // List bindings are handled from the window preview route so they remain configurable.
    }

    private void MenuCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string name } &&
            Enum.TryParse<JsonToolCommand>(name, out var command)) _ = ExecuteAsync(command);
    }

    // WindowChrome 将自绘标题区保留在 WPF 视觉树；未被子控件命中的区域需要显式调用 DragMove。
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Child controls opt into WindowChrome hit testing; the remaining title area drags the window.
        if (e.OriginalSource == sender && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState =
        WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ImportDropAsync(System.Windows.DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(WpfDataFormats.FileDrop) is not string[] paths) return;
        var jsonPaths = paths
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await _documents.ImportAsync(jsonPaths, _lifetime.Token);
        await RefreshDocumentsAsync();
        SaveStateStatus.Text = jsonPaths.Length == paths.Length ? "导入完成" : "已导入 JSON 文件；非 JSON 文件已忽略";
    }

    private void Documents_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(Documents);

    private void Documents_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Documents.SelectedItem is not DocumentListItem item) return;
        var position = e.GetPosition(Documents);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(Documents, new System.Windows.DataObject(typeof(DocumentListItem), item),
            System.Windows.DragDropEffects.Move);
    }

    // 拖放事件签名固定为 void；await 重排完成后才刷新绑定列表以保持显示顺序一致。
    private async void Documents_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(DocumentListItem)) ||
            e.Data.GetData(typeof(DocumentListItem)) is not DocumentListItem source) return;
        var targetElement =
            ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject) as ListBoxItem;
        var target = targetElement?.DataContext as DocumentListItem;
        if (target is null || target.Id == source.Id) return;
        await _documents.ReorderAsync(source.Id, Array.IndexOf(_items.ToArray(), target), _lifetime.Token);
        await RefreshDocumentsAsync();
        Documents.SelectedItem = _items.Single(item => item.Id == source.Id);
    }

    // 文件监控事件可能来自工作线程，先调度到 Dispatcher 再读取或更新 WPF 控件。
    private void OnExternalDocumentChanged(object? sender, JsonDocumentExternalChange change)
    {
        if (_current?.DocumentId != change.DocumentId) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_saveState == SaveState.Clean)
            {
                await ReloadDiskVersionAsync();
                SaveStateStatus.Text = "文件已在外部更新";
                return;
            }

            _saveState = SaveState.Conflict;
            UpdateStatus();
            var action = WpfMessageBox.Show("文件已被外部程序修改。是：重新加载磁盘版本；否：覆盖磁盘版本；取消：复制当前内容后重新加载。", "juju JSON",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (action == MessageBoxResult.Yes) await ReloadDiskVersionAsync();
            else if (action == MessageBoxResult.No)
            {
                await _documents.OverwriteAsync(_current.DocumentId, await GetEditorContentAsync(), _lifetime.Token);
                _current = await _documents.OpenAsync(_current.DocumentId, _lifetime.Token);
                _saveState = SaveState.Clean;
                UpdateStatus();
            }
            else if (action == MessageBoxResult.Cancel)
            {
                WpfClipboard.SetText(await GetEditorContentAsync());
                await ReloadDiskVersionAsync();
            }
        });
    }

    private async Task ReloadDiskVersionAsync()
    {
        if (_current is null) return;
        _current = await _documents.OpenAsync(_current.DocumentId, _lifetime.Token);
        _editorContent = _current.Content;
        _saveState = SaveState.Clean;
        _jsonValid = IsJson(_editorContent);
        if (_editorReady && !_inDiffMode)
            await SendEditorCommandAsync("replaceContent", new { content = _editorContent });
        UpdateStatus();
    }

    // WebView2 进程异常是同步事件，恢复过程不能阻塞其回调，因此有意后台启动。
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_closing || !ReferenceEquals(sender, _editor?.CoreWebView2)) return;
        _ = RecoverEditorAsync();
    }

    private async Task RecoverEditorAsync()
    {
        SaveStateStatus.Text = "编辑器进程异常，正在恢复...";
        _editorReady = false;
        DestroyEditor();
        await Task.Delay(250, _lifetime.Token);
        await EnsureEditorAsync();
    }

    // 用户关闭改为“保存后隐藏”，使工具可被再次打开；ShutdownAsync 设置 _closing 后才允许真正关闭。
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closing)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _ = CloseToHiddenAsync();
    }

    private async Task CloseToHiddenAsync()
    {
        _closing = true;
        try
        {
            if (!await FlushCurrentAsync()) return;
            DestroyEditor();
            Hide();
        }
        finally
        {
            _closing = false;
        }
    }

    // 递增 generation 并取消待响应请求，使旧 WebView 的迟到消息不能完成新会话的请求。
    private void DestroyEditor()
    {
        _autosave.Stop();
        _editorReady = false;
        _generation++;
        foreach (var request in _requests.Values) request.TrySetCanceled();
        _requests.Clear();
        if (_editor is not { } editor) return;
        if (editor.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessage;
            core.ProcessFailed -= OnProcessFailed;
        }

        EditorHost.Children.Remove(editor);
        editor.Dispose();
        _editor = null;
    }

    // 命令表把菜单、按钮与快捷键收敛到同一条异常处理路径。
    private async Task ExecuteAsync(JsonToolCommand command)
    {
        try
        {
            await _commands[command]();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveStateStatus.Text = "操作失败: " + ex.Message;
        }
    }

    private void UpdateStatus()
    {
        // Saving remains an internal synchronization state; the user sees unsaved until persistence succeeds.
        SaveStateStatus.Text = _saveState switch
        {
            SaveState.Clean => "已保存", SaveState.Dirty or SaveState.Saving => "未保存", SaveState.Conflict => "外部修改冲突",
            _ => "保存失败"
        };
        ValidationStatus.Text = _jsonValid ? "JSON: 有效" : "JSON: 格式错误";
    }

    private void OnShortcut(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.IsRepeat || !TryGetShortcutStroke(e, out var stroke)) return;
        var context = _inDiffMode ? JsonToolShortcutContext.Diff :
            Documents.IsKeyboardFocusWithin ? JsonToolShortcutContext.List : JsonToolShortcutContext.Editor;
        if (ExecuteShortcut(stroke, context)) e.Handled = true;
    }

    // 连续按键先尝试与上一次笔画组成 chord，超时则退回为单键匹配。
    private bool ExecuteShortcut(string? stroke, JsonToolShortcutContext context)
    {
        if (string.IsNullOrWhiteSpace(stroke)) return false;
        var now = DateTime.UtcNow;
        if (_pendingShortcut is not null && now - _pendingShortcutAt <= TimeSpan.FromSeconds(1))
        {
            var chord = $"{_pendingShortcut} {stroke}";
            _pendingShortcut = null;
            if (ExecuteShortcutCombination(chord, context, now)) return true;
        }
        else _pendingShortcut = null;

        return ExecuteShortcutCombination(stroke, context, now);
    }

    private bool ExecuteShortcutCombination(string combination, JsonToolShortcutContext context, DateTime now)
    {
        var bindings = JsonToolShortcuts.Definitions.Where(definition =>
            definition.Source == JsonToolShortcutSource.Juju && definition.Context == context).ToArray();
        var binding = bindings.FirstOrDefault(definition =>
            string.Equals(_settings.Current.Shortcuts[definition.Command], combination, StringComparison.Ordinal));
        if (binding is not null)
        {
            _ = ExecuteAsync(binding.Command);
            return true;
        }

        if (!bindings.Any(definition => _settings.Current.Shortcuts[definition.Command]
                .StartsWith(combination + " ", StringComparison.Ordinal))) return false;
        _pendingShortcut = combination;
        _pendingShortcutAt = now;
        return true;
    }

    private static bool TryGetShortcutStroke(System.Windows.Input.KeyEventArgs eventArgs, out string stroke)
    {
        var key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
        var keyName = key switch
        {
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4", Key.D5 => "5", Key.D6 => "6",
            Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
            Key.Return => "Enter", Key.Back => "Backspace", Key.Prior => "PageUp", Key.Next => "PageDown",
            Key.Up => "Up", Key.Down => "Down", Key.Left => "Left", Key.Right => "Right",
            Key.Escape => "Escape", Key.Delete => "Delete", Key.Space => "Space", Key.Tab => "Tab", Key.Home => "Home",
            Key.End => "End", Key.Insert => "Insert",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            _ => null,
        };
        if (keyName is null)
        {
            stroke = string.Empty;
            return false;
        }

        var modifiers = Keyboard.Modifiers;
        stroke = string.Join('+', new[]
        {
            modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            keyName,
        }.Where(part => part is not null));
        return true;
    }

    private static bool IsJson(string content)
    {
        try
        {
            JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetContent(JsonElement payload, out string content)
    {
        content = string.Empty;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("content", out var value) &&
               value.ValueKind == JsonValueKind.String && (content = value.GetString()!) is not null;
    }

    private static bool TryGetPosition(JsonElement payload, out int line, out int column)
    {
        line = column = 0;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("lineNumber", out var l) &&
               l.TryGetInt32(out line) && payload.TryGetProperty("column", out var c) && c.TryGetInt32(out column) &&
               line > 0 && column > 0;
    }

    private static bool TryGetValidation(JsonElement payload, out bool valid)
    {
        valid = false;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("hasErrors", out var errors) ||
            errors.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        valid = !errors.GetBoolean();
        return true;
    }

    private static bool TryGetCommandResult(JsonElement payload, out string? error)
    {
        error = null;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("ok", out var ok) ||
            ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        if (ok.GetBoolean()) return payload.TryGetProperty("result", out _);
        error = payload.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : "编辑器命令失败。";
        return false;
    }

    private static string? PromptForName(string value)
    {
        var input = new WpfTextBox { Text = value, Margin = new Thickness(12), MinWidth = 260 };
        var dialog = new Window
        {
            Title = "重命名 JSON", Owner = WpfApplication.Current.MainWindow, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Children =
                {
                    input, new WpfButton { Content = "确定", IsDefault = true, Margin = new Thickness(12, 0, 12, 12) }
                }
            }
        };
        ((WpfButton)((StackPanel)dialog.Content).Children[1]).Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void New_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.New);
    private void Save_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.Save);
    private void Format_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.Format);
    private void Copy_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.CopyText);
    private void Minify_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.CopyMinified);
    private void FileCopy_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.CopyFile);
    private void Fold_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.FoldAll);
    private void Unfold_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.UnfoldAll);
    private void FoldLevel_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.UnfoldLevel);
    private void Diff_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.EnterDiff);
    private void ExitDiff_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.ExitDiff);
    private void Rename_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.Rename);
    private void Delete_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.Delete);

    // record 适合不可变传输数据：编译器生成值相等性和只读属性，近似 Java record。
    private sealed record DocumentListItem(JsonDocumentId Id, string FileName, string DisplayName);

    private sealed record BridgeMessage(string Type, string? RequestId, JsonElement Payload)
    {
        private static readonly HashSet<string> Types =
        [
            "ready", "contentChanged", "cursorChanged", "validationChanged", "saveRequested", "shortcut",
            "editorFocused", "commandResult"
        ];

        public static bool TryParse(JsonElement root, out BridgeMessage message)
        {
            message = default!;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || version.GetInt32() != ProtocolVersion ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !Types.Contains(type.GetString()!) || !root.TryGetProperty("requestId", out var requestId) ||
                requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                !root.TryGetProperty("payload", out var payload)) return false;
            if (type.GetString() == "commandResult" && requestId.ValueKind != JsonValueKind.String) return false;
            message = new(type.GetString()!, requestId.ValueKind == JsonValueKind.String ? requestId.GetString() : null,
                payload.Clone());
            return true;
        }
    }
}