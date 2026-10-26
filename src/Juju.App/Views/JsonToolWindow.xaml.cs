using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using System.Windows.Threading;
using Juju.App.Bootstrap;
using Juju.Core.Errors;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Minify;
using Juju.Tools.Json.Settings;
using Juju.Platform.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
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
    private JsonViewMode _mode = JsonViewMode.Edit;
    private JsonDocumentId? _compareLeft;
    private JsonDocumentId? _compareRight;
    private DocumentListItem? _renaming;
    private ListBoxItem? _dropMarker;
    private bool _dropAfter;
    private bool _syncingDiff;
    private bool _comparing;
    private bool _deleting;
    private bool _creating;
    private int _foldDepth;
    private int _generation;
    private int _request;
    private long _contentVersion;
    private Point _dragStart;
    private DocumentListItem? _dragCandidate;
    private bool _largeDocument;
    private string? _pendingShortcut;
    private DateTime _pendingShortcutAt;

    private bool _inDiffMode => _mode != JsonViewMode.Edit;

    private TextBlock SaveStateStatus => GetToolStatus("StatusLeftText");
    private TextBlock ValidationStatus => GetToolStatus("StatusCenterText");
    private TextBlock CursorStatus => GetToolStatus("StatusRightText");

    private TextBlock GetToolStatus(string name)
    {
        ApplyTemplate();
        return (TextBlock)(Template.FindName(name, this) ??
            throw new InvalidOperationException($"工具窗口模板缺少状态栏元素 {name}。"));
    }

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
        UpdateMenuGestures(settings.Current);
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new WindowInteropHelper(this).Handle);
        // WPF 事件委托是 void；这些 async lambda 是事件边界，内部 await 保持 UI 响应。
        Loaded += async (_, _) => await EnsureEditorAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && !_closing) await EnsureEditorAsync();
        };
        Drop += async (_, eventArgs) => await ImportDropAsync(eventArgs);
        PreviewKeyDown += OnShortcut;
        InputManager.Current.PreProcessInput += NewShortcut_PreProcessInput;
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            await SaveCurrentAsync(_editorContent);
        };
        _documents.ExternalChanged += OnExternalDocumentChanged;
        _settings.Changed += OnSettingsChanged;
        RenderMode();
    }

    // 在窗口管理器注册 Core 所有的快照实现前保持可选，视图不自行创建跨层基础设施。
    public IJsonFileClipboard? FileClipboard { get; set; }

    public void FocusForShortcuts()
    {
        if (!IsVisible) return;
        Focus();
        if (_editorReady && _mode == JsonViewMode.Edit) _ = FocusEditorAsync();
    }

    private async Task FocusEditorAsync()
    {
        try { await SendEditorCommandAsync("focus", new { }); }
        catch (Exception) { /* The WPF window remains focused while the editor is initializing. */ }
    }

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
            _settings.Changed -= OnSettingsChanged;
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
        _autosave.Interval = TimeSpan.FromMilliseconds(_settings.Current.AutosaveDelayMilliseconds);
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

    private async Task RefreshDocumentsAsync(JsonDocumentId? preferredDocumentId = null)
    {
        var list = await _documents.ListAsync(_lifetime.Token);
        if (list.Count == 0) list = [await _documents.CreateAsync(_lifetime.Token)];
        _items = list.Select(item =>
            new DocumentListItem(item.Id, item.FileName, Path.GetFileNameWithoutExtension(item.FileName))).ToArray();
        FileCount.Text = $"{_items.Count} 个";
        UpdateCompareSelections();
        ApplyDocumentFilter();
        if (_current is null && _mode == JsonViewMode.Edit)
        {
            var target = preferredDocumentId is { } id
                ? _items.FirstOrDefault(item => item.Id == id) ?? _items[0]
                : _items[0];
            if (!string.IsNullOrWhiteSpace(DocumentFilter.Text) &&
                !target.DisplayName.Contains(DocumentFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                DocumentFilter.Clear();
            await OpenSelectedAsync(target);
            Documents.ScrollIntoView(target);
        }
        else if (_mode == JsonViewMode.Edit) UpdateEditorHeader();
    }

    // SelectionChanged 是 WPF 事件入口；_selecting 排除代码恢复选中项时产生的递归处理。
    private async void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting || Documents.SelectedItem is not DocumentListItem document) return;
        if (_mode != JsonViewMode.Edit)
        {
            ChooseCompareFile(document);
            _selecting = true;
            Documents.SelectedItem = null;
            _selecting = false;
            return;
        }
        await OpenSelectedAsync(document);
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

            _current = await _documents.OpenAsync(document.Id, _lifetime.Token);
            _editorContent = _current.Content;
            _saveState = SaveState.Clean;
            _jsonValid = IsJson(_editorContent);
            _largeDocument = Encoding.UTF8.GetByteCount(_editorContent) > 10 * 1024 * 1024;
            _foldDepth = 0;
            UpdateStatus();
            UpdateEditorHeader();
            if (_mode == JsonViewMode.Edit && Documents.Items.Contains(document))
            {
                _selecting = true;
                Documents.SelectedItem = document;
                _selecting = false;
            }
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
                        _autosave.Interval = TimeSpan.FromMilliseconds(_settings.Current.AutosaveDelayMilliseconds);
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
                    {
                        var combination = shortcut.GetString();
                        if (!string.IsNullOrWhiteSpace(combination) && !TryHandleNewShortcut(combination))
                            ExecuteShortcut(combination,
                                _inDiffMode ? JsonToolShortcutContext.Diff : JsonToolShortcutContext.Editor);
                    }
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
            if (IsActive && _mode == JsonViewMode.Edit && !Documents.IsKeyboardFocusWithin &&
                !DocumentFilter.IsKeyboardFocusWithin && Keyboard.FocusedElement is not MenuItem)
                await FocusEditorAsync();
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
        if (_creating) return;
        _creating = true;
        try
        {
            if (_renaming is not null)
            {
                var renaming = _renaming;
                _renaming = null;
                renaming.IsRenaming = false;
            }
            if (_mode != JsonViewMode.Edit) ClearCompareState();
            await _documentGate.WaitAsync(_lifetime.Token);
            _documentGate.Release();
            if (!await FlushCurrentAsync()) return;
            var created = await _documents.CreateAsync(_lifetime.Token);
            if (!string.IsNullOrEmpty(DocumentFilter.Text)) DocumentFilter.Clear();
            await RefreshDocumentsAsync();
            var item = _items.Single(document => document.Id == created.Id);
            await OpenSelectedAsync(item);
            Documents.ScrollIntoView(item);
            if (_editorReady && IsActive) await FocusEditorAsync();
        }
        finally { _creating = false; }
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

    // The existing editor remains intact; compare panes are WPF views of persisted snapshots.
    private async Task EnterDiffAsync()
    {
        if (_mode != JsonViewMode.Edit) return;
        await _documentGate.WaitAsync(_lifetime.Token);
        _documentGate.Release();
        if (!await FlushCurrentAsync()) return;
        _mode = JsonViewMode.SelectCompare;
        _compareLeft = null;
        _compareRight = null;
        _autosave.Stop();
        RenderMode();
        Documents.Focus();
    }

    private async Task ExitDiffAsync()
    {
        if (_mode == JsonViewMode.Edit) return;
        ClearCompareState();
        if (_current is not null && _editorReady)
            await SendEditorCommandAsync("openDocument", new
            {
                documentId = _current.DocumentId.ToString(), content = _editorContent, language = "json"
            });
    }

    private void ClearCompareState()
    {
        _mode = JsonViewMode.Edit;
        _compareLeft = null;
        _compareRight = null;
        LeftDiffRows.ItemsSource = null;
        RightDiffRows.ItemsSource = null;
        RenderMode();
        UpdateStatus();
    }

    private void ChooseCompareFile(DocumentListItem document)
    {
        if (_compareLeft == document.Id) _compareLeft = null;
        else if (_compareRight == document.Id) _compareRight = null;
        else if (_compareLeft is null) _compareLeft = document.Id;
        else if (_compareRight is null) _compareRight = document.Id;
        else { _compareLeft = _compareRight; _compareRight = document.Id; }

        if (_mode == JsonViewMode.CompareResult)
        {
            _mode = JsonViewMode.SelectCompare;
            LeftDiffRows.ItemsSource = null;
            RightDiffRows.ItemsSource = null;
        }
        RenderMode();
    }

    private async void StartCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_comparing || _mode == JsonViewMode.Edit || _compareLeft is not { } left ||
            _compareRight is not { } right || left == right) return;
        _comparing = true;
        StartCompareButton.IsEnabled = false;
        try
        {
            var original = await _documents.OpenAsync(left, _lifetime.Token);
            var modified = await _documents.OpenAsync(right, _lifetime.Token);
            var lines = JsonLineDiff.Compare(original.Content, modified.Content);
            if (_mode == JsonViewMode.Edit || _compareLeft != left || _compareRight != right) return;
            LeftDiffRows.ItemsSource = lines.Select(line => new DiffDisplayLine(
                line.LeftNumber?.ToString() ?? "", line.LeftText, line.LeftKind.ToString())).ToArray();
            RightDiffRows.ItemsSource = lines.Select(line => new DiffDisplayLine(
                line.RightNumber?.ToString() ?? "", line.RightText, line.RightKind.ToString())).ToArray();
            LeftCompareName.Text = _items.First(item => item.Id == left).FileName;
            RightCompareName.Text = _items.First(item => item.Id == right).FileName;
            LeftDiffScroll.ScrollToTop();
            RightDiffScroll.ScrollToTop();
            _mode = JsonViewMode.CompareResult;
            RenderMode();
            ValidationStatus.Text = lines.All(line => line.LeftKind == JsonDiffKind.Same)
                ? "文件内容一致" : $"发现 {lines.Count(line => line.LeftKind != JsonDiffKind.Same)} 处差异行";
            CursorStatus.Text = "A ↔ B";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveStateStatus.Text = "对比失败: " + ex.Message;
        }
        finally
        {
            _comparing = false;
            RenderMode();
        }
    }

    private void ExitCompare_Click(object sender, RoutedEventArgs e) => _ = ExecuteAsync(JsonToolCommand.ExitDiff);

    private void RenderMode()
    {
        if (CompareBar is null) return;
        FileMenu.IsEnabled = EditMenu.IsEnabled = ViewMenu.IsEnabled = _mode == JsonViewMode.Edit;
        CompareBar.Visibility = _mode == JsonViewMode.Edit ? Visibility.Collapsed : Visibility.Visible;
        NormalWorkspace.Visibility = _mode == JsonViewMode.Edit ? Visibility.Visible : Visibility.Collapsed;
        CompareSelectionWorkspace.Visibility = _mode == JsonViewMode.SelectCompare ? Visibility.Visible : Visibility.Collapsed;
        CompareResultWorkspace.Visibility = _mode == JsonViewMode.CompareResult ? Visibility.Visible : Visibility.Collapsed;
        CompareHint.Text = _mode == JsonViewMode.CompareResult ? "对比完成 · 可重新选择文件" :
            _compareLeft is not { } left ? "请在左侧选择第一个文件（A）" :
            _compareRight is not { } right ? $"已选择 A：{_items.First(item => item.Id == left).DisplayName}，请选择第二个文件（B）" :
            $"A：{_items.First(item => item.Id == left).DisplayName} → B：{_items.First(item => item.Id == right).DisplayName}";
        StartCompareButton.Content = _mode == JsonViewMode.CompareResult ? "重新对比" : "开始对比";
        StartCompareButton.IsEnabled = !_comparing && _compareLeft is not null && _compareRight is not null;
        UpdateCompareSelections();
        if (_mode == JsonViewMode.SelectCompare)
        {
            ValidationStatus.Text = "选择两个文件";
            CursorStatus.Text = "";
        }
        if (_mode == JsonViewMode.Edit) UpdateEditorHeader();
    }

    private void UpdateCompareSelections()
    {
        foreach (var item in _items)
        {
            item.IsCompareSelection = _mode != JsonViewMode.Edit;
            item.CompareSlot = item.Id == _compareLeft ? "A" : item.Id == _compareRight ? "B" : "";
        }
        if (_mode != JsonViewMode.Edit) Documents.SelectedItem = null;
    }

    private void DiffScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingDiff || _mode != JsonViewMode.CompareResult || e.VerticalChange == 0) return;
        _syncingDiff = true;
        (ReferenceEquals(sender, LeftDiffScroll) ? RightDiffScroll : LeftDiffScroll).ScrollToVerticalOffset(e.VerticalOffset);
        _syncingDiff = false;
    }

    private void UpdateEditorHeader()
    {
        CurrentFileName.Text = _current is null ? "" : _items.FirstOrDefault(item => item.Id == _current.DocumentId)?.FileName ?? "";
        JsonValidity.Text = _jsonValid ? "JSON 有效" : "JSON 无效";
        JsonValidity.Foreground = (Brush)new BrushConverter().ConvertFromString(_jsonValid ? "#4B74D9" : "#C04D4D")!;
    }

    private Task RenameAsync()
    {
        if (_mode == JsonViewMode.Edit && _current is { } current)
            BeginRename(_items.First(item => item.Id == current.DocumentId));
        return Task.CompletedTask;
    }

    private async Task DeleteAsync()
    {
        if (_deleting || _mode != JsonViewMode.Edit || _current is null) return;
        _deleting = true;
        try
        {
            var restoreListFocus = Documents.IsKeyboardFocusWithin;
            await _documentGate.WaitAsync(_lifetime.Token);
            _documentGate.Release();
            if (_mode != JsonViewMode.Edit || _current is null) return;
            var deletedId = _current.DocumentId;
            var index = Array.FindIndex(_items.ToArray(), item => item.Id == deletedId);
            JsonDocumentId? nextId = index >= 0 && index + 1 < _items.Count ? _items[index + 1].Id :
                index > 0 ? _items[index - 1].Id : null;
            await _documents.DeleteAsync(deletedId, _lifetime.Token);
            _current = null;
            await RefreshDocumentsAsync(nextId);
            if (restoreListFocus) Documents.Focus();
        }
        finally { _deleting = false; }
    }

    private void DocumentFilter_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyDocumentFilter();

    private void ApplyDocumentFilter()
    {
        if (DocumentFilter is null || Documents is null) return;
        var filter = DocumentFilter.Text.Trim();
        _selecting = true;
        Documents.ItemsSource = string.IsNullOrEmpty(filter)
            ? _items
            : _items.Where(item => item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        Documents.SelectedItem = _mode == JsonViewMode.Edit && _current is { } current
            ? _items.FirstOrDefault(item => item.Id == current.DocumentId) : null;
        _selecting = false;
    }

    private void BeginRename(DocumentListItem item)
    {
        if (_mode != JsonViewMode.Edit) return;
        if (!string.IsNullOrEmpty(DocumentFilter.Text) &&
            !item.DisplayName.Contains(DocumentFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            DocumentFilter.Clear();
        if (_renaming is not null) _renaming.IsRenaming = false;
        _renaming = item;
        item.RenameText = item.DisplayName;
        item.IsRenaming = true;
        Documents.ScrollIntoView(item);
        Documents.UpdateLayout();
        if (Documents.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;
        container.UpdateLayout();
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_renaming, item)) return;
            var input = FindVisualChild<WpfTextBox>(container);
            if (input is null) return;
            input.Focus();
            input.SelectAll();
        }, DispatcherPriority.Input);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void Documents_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_mode != JsonViewMode.Edit || _renaming is not null) return;
        if (ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject) is ListBoxItem
            { DataContext: DocumentListItem item })
        {
            BeginRename(item);
            e.Handled = true;
        }
    }

    private void RenameField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var renaming = _renaming;
            _renaming = null;
            if (renaming is not null) renaming.IsRenaming = false;
            Documents.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            e.Handled = true;
            _ = CommitRenameAsync(sender as WpfTextBox);
        }
    }

    private void RenameField_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_renaming is not null) _ = CommitRenameAsync(sender as WpfTextBox);
    }

    private async Task CommitRenameAsync(WpfTextBox? input)
    {
        if (_renaming is not { } item) return;
        _renaming = null;
        item.IsRenaming = false;
        var name = input?.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { SaveStateStatus.Text = "名称不能为空"; return; }
        try
        {
            await _documents.RenameAsync(item.Id, name, _lifetime.Token);
            await RefreshDocumentsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveStateStatus.Text = "重命名失败: " + ex.Message;
        }
    }

    private void Documents_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_mode != JsonViewMode.Edit || _renaming is not null || e.OriginalSource is WpfTextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down)
        {
            var count = Documents.Items.Count;
            if (count == 0) return;

            var index = Documents.SelectedIndex;
            var next = index < 0
                ? e.Key == Key.Down ? 0 : count - 1
                : (index + (e.Key == Key.Down ? 1 : -1) + count) % count;
            Documents.SelectedIndex = next;
            if (Documents.SelectedItem is { } selected) Documents.ScrollIntoView(selected);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = ExecuteAsync(JsonToolCommand.Delete);
        }
        else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = ExecuteAsync(JsonToolCommand.Rename);
        }
    }

    private void MenuCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } &&
            Enum.TryParse<JsonToolCommand>(name, out var command) &&
            (command == JsonToolCommand.EnterDiff || _mode == JsonViewMode.Edit)) _ = ExecuteAsync(command);
    }

    private async Task ImportDropAsync(DragEventArgs eventArgs)
    {
        if (_mode != JsonViewMode.Edit) return;
        if (eventArgs.Data.GetData(WpfDataFormats.FileDrop) is not string[] paths) return;
        var jsonPaths = paths
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await _documents.ImportAsync(jsonPaths, _lifetime.Token);
        await RefreshDocumentsAsync();
        SaveStateStatus.Text = jsonPaths.Length == paths.Length ? "导入完成" : "已导入 JSON 文件；非 JSON 文件已忽略";
    }

    private void Documents_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = _mode == JsonViewMode.Edit && _renaming is null && e.OriginalSource is not WpfTextBox
            ? (ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject)
                as ListBoxItem)?.DataContext as DocumentListItem
            : null;
        if (_dragCandidate is not null) _dragStart = e.GetPosition(Documents);
    }

    private void Documents_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragCandidate = null;

    private void Documents_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode != JsonViewMode.Edit || _renaming is not null || e.LeftButton != MouseButtonState.Pressed ||
            _dragCandidate is not { } item || !ReferenceEquals(Documents.SelectedItem, item) ||
            e.OriginalSource is WpfTextBox ||
            ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject) is not ListBoxItem) return;
        var position = e.GetPosition(Documents);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragCandidate = null;
        try { DragDrop.DoDragDrop(Documents, new DataObject(typeof(DocumentListItem), item), DragDropEffects.Move); }
        finally { ClearDropMarker(); }
    }

    private void Documents_DragOver(object sender, DragEventArgs e)
    {
        if (_mode != JsonViewMode.Edit || !e.Data.GetDataPresent(typeof(DocumentListItem))) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        var target = ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (ReferenceEquals(target, _dropMarker) && target is not null)
        {
            var after = e.GetPosition(target).Y > target.ActualHeight / 2;
            if (after == _dropAfter) return;
        }
        ClearDropMarker();
        _dropMarker = target;
        if (target is null) return;
        _dropAfter = e.GetPosition(target).Y > target.ActualHeight / 2;
        target.Tag = _dropAfter ? "After" : "Before";
    }

    private void Documents_DragLeave(object sender, DragEventArgs e)
    {
        if (!Documents.IsMouseOver) ClearDropMarker();
    }

    private void ClearDropMarker()
    {
        if (_dropMarker is not null) _dropMarker.Tag = null;
        _dropMarker = null;
    }

    // 拖放事件签名固定为 void；await 重排完成后才刷新绑定列表以保持显示顺序一致。
    private async void Documents_Drop(object sender, DragEventArgs e)
    {
        if (_mode != JsonViewMode.Edit || !e.Data.GetDataPresent(typeof(DocumentListItem)) ||
            e.Data.GetData(typeof(DocumentListItem)) is not DocumentListItem source) return;
        var targetElement = _dropMarker ??
            ItemsControl.ContainerFromElement(Documents, e.OriginalSource as DependencyObject) as ListBoxItem;
        var target = targetElement?.DataContext as DocumentListItem;
        var after = _dropMarker is null ? targetElement is not null && e.GetPosition(targetElement).Y > targetElement.ActualHeight / 2 : _dropAfter;
        ClearDropMarker();
        if (target is null || target.Id == source.Id || !await FlushCurrentAsync()) return;
        var from = Array.FindIndex(_items.ToArray(), item => item.Id == source.Id);
        var to = Array.FindIndex(_items.ToArray(), item => item.Id == target.Id);
        var destination = to + (after ? 1 : 0) - (from < to ? 1 : 0);
        if (destination == from) return;
        await _documents.ReorderAsync(source.Id, destination, _lifetime.Token);
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

    // 重写 Window.OnClosing，接管系统关闭请求和自绘关闭按钮调用 Close() 后的统一生命周期。
    // 普通关闭先保存；工具设置决定复用隐藏窗口还是实际释放并在下次打开时重建。
    // ShutdownAsync 先设置 _closing=true，应用退出时才允许基类完成真正关闭和窗口资源销毁。
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closing)
        {
            // 显式应用关停路径：不取消 Closing，并让 Window 基类继续执行标准关闭逻辑。
            base.OnClosing(e);
            return;
        }

        // 先取消同步 Closing，再等待异步保存；成功后按工具设置执行 Hide 或真正 Close。
        e.Cancel = true;
        _ = CloseToolAsync();
    }

    private void OnSettingsChanged(JsonToolSettings settings)
    {
        void Apply()
        {
            UpdateMenuGestures(settings);
            _autosave.Interval = TimeSpan.FromMilliseconds(settings.AutosaveDelayMilliseconds);
            if (_editorReady && !_closing) _ = RefreshEditorConfigurationAsync();
        }
        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.BeginInvoke(Apply);
    }

    private void UpdateMenuGestures(JsonToolSettings settings)
    {
        NewMenuItem.InputGestureText = settings.Shortcuts[JsonToolCommand.New];
        SaveMenuItem.InputGestureText = settings.Shortcuts[JsonToolCommand.Save];
        RenameMenuItem.InputGestureText = settings.Shortcuts[JsonToolCommand.Rename];
        DeleteMenuItem.InputGestureText = settings.Shortcuts[JsonToolCommand.Delete];
    }

    private async Task RefreshEditorConfigurationAsync()
    {
        try
        {
            if (!await FlushCurrentAsync()) return;
            DestroyEditor();
            await EnsureEditorAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SaveStateStatus.Text = "编辑器设置应用失败：" + ex.Message;
        }
    }

    private async Task CloseToolAsync()
    {
        _closing = true;
        try
        {
            if (!await FlushCurrentAsync()) return;
            if (_mode != JsonViewMode.Edit)
            {
                _mode = JsonViewMode.Edit;
                _compareLeft = null;
                _compareRight = null;
                LeftDiffRows.ItemsSource = null;
                RightDiffRows.ItemsSource = null;
                RenderMode();
                UpdateStatus();
            }
            DestroyEditor();
            if (_settings.Current.KeepWindowOnClose) Hide();
            else
            {
                // 不保留窗口时真正关闭；窗口管理器的 Closed 回调会清空缓存，下一次重新创建。
                _documents.ExternalChanged -= OnExternalDocumentChanged;
                _settings.Changed -= OnSettingsChanged;
                _lifetime.Cancel();
                Close();
            }
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
        if (_mode == JsonViewMode.Edit) UpdateEditorHeader();
    }

    private void NewShortcut_PreProcessInput(object sender, PreProcessInputEventArgs args)
    {
        if (!IsVisible || (!IsActive && !FileMenu.IsSubmenuOpen && !EditMenu.IsSubmenuOpen &&
            !ViewMenu.IsSubmenuOpen && !ToolMenu.IsSubmenuOpen) || _closing ||
            args.StagingItem.Input is not KeyEventArgs e ||
            e.RoutedEvent != Keyboard.PreviewKeyDownEvent || e.Handled || e.IsRepeat ||
            !TryGetShortcutStroke(e, out var stroke)) return;
        if (TryHandleNewShortcut(stroke)) e.Handled = true;
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.IsRepeat || !TryGetShortcutStroke(e, out var stroke)) return;
        if (TryHandleNewShortcut(stroke)) { e.Handled = true; return; }
        if (_renaming is not null || e.OriginalSource is WpfTextBox ||
            Keyboard.FocusedElement is WpfTextBox) return;
        var context = _inDiffMode ? JsonToolShortcutContext.Diff :
            Documents.IsKeyboardFocusWithin ? JsonToolShortcutContext.List : JsonToolShortcutContext.Editor;
        if (ExecuteShortcut(stroke, context)) e.Handled = true;
    }

    private bool TryHandleNewShortcut(string stroke)
    {
        var shortcut = _settings.Current.Shortcuts[JsonToolCommand.New];
        if (_pendingShortcut is { } previous &&
            DateTime.UtcNow - _pendingShortcutAt <= TimeSpan.FromSeconds(1) &&
            string.Equals(shortcut, $"{previous} {stroke}", StringComparison.OrdinalIgnoreCase))
        {
            _pendingShortcut = null;
            _ = ExecuteAsync(JsonToolCommand.New);
            return true;
        }
        if (string.Equals(shortcut, stroke, StringComparison.OrdinalIgnoreCase))
        {
            _pendingShortcut = null;
            _ = ExecuteAsync(JsonToolCommand.New);
            return true;
        }
        if (!shortcut.StartsWith(stroke + " ", StringComparison.OrdinalIgnoreCase)) return false;
        _pendingShortcut = stroke;
        _pendingShortcutAt = DateTime.UtcNow;
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        InputManager.Current.PreProcessInput -= NewShortcut_PreProcessInput;
        base.OnClosed(e);
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
            if (binding.Command == JsonToolCommand.Delete && !Documents.IsKeyboardFocusWithin) return false;
            _ = ExecuteAsync(binding.Command);
            return true;
        }

        if (!bindings.Any(definition => _settings.Current.Shortcuts[definition.Command]
                .StartsWith(combination + " ", StringComparison.Ordinal))) return false;
        _pendingShortcut = combination;
        _pendingShortcutAt = now;
        return true;
    }

    private static bool TryGetShortcutStroke(KeyEventArgs eventArgs, out string stroke)
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

    private enum JsonViewMode { Edit, SelectCompare, CompareResult }

    private sealed record DiffDisplayLine(string Number, string Text, string Kind);

    private sealed class DocumentListItem(JsonDocumentId id, string fileName, string displayName) : INotifyPropertyChanged
    {
        private string _renameText = displayName;
        private string _compareSlot = "";
        private bool _isRenaming;
        private bool _isCompareSelection;

        public JsonDocumentId Id { get; } = id;
        public string FileName { get; } = fileName;
        public string DisplayName { get; } = displayName;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string RenameText
        {
            get => _renameText;
            set { _renameText = value; PropertyChanged?.Invoke(this, new(nameof(RenameText))); }
        }
        public string CompareSlot
        {
            get => _compareSlot;
            set { _compareSlot = value; PropertyChanged?.Invoke(this, new(nameof(CompareSlot))); }
        }
        public bool IsRenaming
        {
            get => _isRenaming;
            set { _isRenaming = value; PropertyChanged?.Invoke(this, new(nameof(IsRenaming))); }
        }
        public bool IsCompareSelection
        {
            get => _isCompareSelection;
            set { _isCompareSelection = value; PropertyChanged?.Invoke(this, new(nameof(IsCompareSelection))); }
        }
    }

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
