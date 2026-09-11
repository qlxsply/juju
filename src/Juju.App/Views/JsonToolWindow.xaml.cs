using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Juju.Core.Errors;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Minify;

namespace Juju.App;

public partial class JsonToolWindow : Window
{
    private static JsonToolWindow? _instance;
    private readonly JsonDocumentService _documents;
    private JsonDocumentSnapshot? _current;
    private bool _editorReady;
    private int _request;
    private readonly Dictionary<string, Action<string>> _contentRequests = [];
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private string _editorContent = string.Empty;

    private JsonToolWindow(JsonDocumentService documents)
    {
        _documents = documents;
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _instance = null;
        Drop += async (_, eventArgs) => await ImportDropAsync(eventArgs);
        PreviewKeyDown += OnShortcut;
        _autosave.Tick += async (_, _) => { _autosave.Stop(); await SaveEditorContentAsync(); };
    }

    public static void ShowOrActivate(JsonDocumentService documents)
    {
        _instance ??= new JsonToolWindow(documents);
        if (!_instance.IsVisible) _instance.Show();
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "juju", "webview2");
            await Editor.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(userDataFolder: userData));
            var assets = Path.Combine(AppContext.BaseDirectory, "Editor");
            Editor.CoreWebView2.SetVirtualHostNameToFolderMapping("juju.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            Editor.CoreWebView2.WebMessageReceived += OnWebMessage;
            Editor.Source = new Uri("https://juju.local/index.html");
            await RefreshDocumentsAsync();
        }
        catch (Exception ex) { Status.Text = "编辑器初始化失败: " + ex.Message; }
    }

    private async Task RefreshDocumentsAsync()
    {
        var list = await _documents.ListAsync();
        if (list.Count == 0) list = [await _documents.CreateAsync()];
        Documents.ItemsSource = list;
        if (_current is null || !list.Any(item => item.Id == _current.DocumentId)) Documents.SelectedItem = list[0];
    }

    private async void Documents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Documents.SelectedItem is not JsonDocumentMetadata document) return;
        _current = await _documents.OpenAsync(document.Id);
        if (_editorReady) OpenCurrent();
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var type = message.RootElement.GetProperty("type").GetString();
            if (type == "ready") { _editorReady = true; Send("initialize", new { theme = "vs", indentSize = 2 }); if (_current is not null) OpenCurrent(); Status.Text = "已保存"; }
            else if (type == "contentChanged")
            {
                _editorContent = message.RootElement.GetProperty("payload").GetProperty("content").GetString() ?? string.Empty;
                Status.Text = "未保存";
                _autosave.Stop(); _autosave.Start();
            }
            else if (type == "saveRequested") _ = SaveContentAsync(message.RootElement.GetProperty("payload").GetProperty("content").GetString() ?? string.Empty);
            else if (type == "commandResult" && message.RootElement.GetProperty("payload").GetProperty("ok").GetBoolean() && message.RootElement.GetProperty("payload").TryGetProperty("result", out var result) && result.TryGetProperty("content", out var content))
            {
                var requestId = message.RootElement.GetProperty("requestId").GetString();
                if (requestId is not null && _contentRequests.Remove(requestId, out var action)) action(content.GetString() ?? string.Empty);
            }
        }
        catch { Status.Text = "编辑器消息无效"; }
    }

    private void OpenCurrent() => Send("openDocument", new { documentId = _current!.DocumentId.ToString(), content = _current.Content, language = "json" });
    private string Send(string type, object payload)
    {
        var requestId = (++_request).ToString();
        Editor.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { version = 1, type, requestId, payload }));
        return requestId;
    }
    private void New_Click(object sender, RoutedEventArgs e) => _ = NewAsync();
    private async Task NewAsync() { var created = await _documents.CreateAsync(); await RefreshDocumentsAsync(); Documents.SelectedItem = (Documents.ItemsSource as IReadOnlyList<JsonDocumentMetadata>)?.Single(item => item.Id == created.Id); }
    private void Save_Click(object sender, RoutedEventArgs e) { _autosave.Stop(); RequestContent(content => _ = SaveContentAsync(content)); }
    private void RequestContent(Action<string> action) => _contentRequests[Send("getContent", new { })] = action;
    private async Task SaveEditorContentAsync() => await SaveContentAsync(_editorContent);
    private async Task SaveContentAsync(string content)
    {
        if (_current is null) return;
        try { await _documents.SaveAsync(_current.DocumentId, content, _current.Revision); _current = await _documents.OpenAsync(_current.DocumentId); Status.Text = "已保存"; }
        catch (JujuException ex) when (ex.Code == ErrorCode.ExternalModificationConflict) { Status.Text = "外部修改冲突"; }
        catch (Exception ex) { Status.Text = "保存失败: " + ex.Message; }
    }
    private void Format_Click(object sender, RoutedEventArgs e) => Send("format", new { });
    private void Copy_Click(object sender, RoutedEventArgs e) => RequestContent(content => { System.Windows.Clipboard.SetText(content); Status.Text = "已复制全文"; });
    private void Minify_Click(object sender, RoutedEventArgs e) => RequestContent(content =>
    {
        try { System.Windows.Clipboard.SetText(new JsonLexicalMinifier().Minify(content)); Status.Text = "已复制压缩文本"; }
        catch (JujuException) { Status.Text = "JSON 格式错误，无法压缩复制"; }
    });
    private void Fold_Click(object sender, RoutedEventArgs e) => Send("foldAll", new { });
    private async void Delete_Click(object sender, RoutedEventArgs e) { if (_current is null) return; await _documents.DeleteAsync(_current.DocumentId); _current = null; await RefreshDocumentsAsync(); }
    private async Task ImportDropAsync(System.Windows.DragEventArgs eventArgs) { if (eventArgs.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return; await _documents.ImportAsync(paths); await RefreshDocumentsAsync(); Status.Text = "导入完成"; }
    private void OnShortcut(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Alt) return;
        if (e.Key == System.Windows.Input.Key.A) New_Click(sender, e); else if (e.Key == System.Windows.Input.Key.S) Save_Click(sender, e); else if (e.Key == System.Windows.Input.Key.F) Format_Click(sender, e); else if (e.Key == System.Windows.Input.Key.C) Copy_Click(sender, e); else return;
        e.Handled = true;
    }
}
