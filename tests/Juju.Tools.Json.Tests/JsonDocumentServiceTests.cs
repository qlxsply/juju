using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Core.Telemetry;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Minify;
using Juju.Tools.Json.Settings;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Tests;

// xUnit 为每个测试方法创建一个此类实例；IAsyncLifetime 在测试前后异步建立并清理独立的数据根目录。
public sealed class JsonDocumentServiceTests : IAsyncLifetime
{
    // 每个实例使用带随机 GUID 的临时目录，避免并行测试、历史残留或本机文件互相干扰。
    private readonly string _root = Path.Combine(Path.GetTempPath(), "juju-tests", Guid.NewGuid().ToString("N"));

    // 初始化阶段构造服务；null! 仅告诉可空性分析器该字段会在每个测试执行前赋值。
    private JsonDocumentService _documents = null!;

    // xUnit 在每个 [Fact]/[Theory] 前调用：先创建必要的数据目录，再组装使用真实文件系统的被测服务。
    public async Task InitializeAsync()
    {
        var writer = new AtomicFileWriter();
        await new DataRootService(writer).EnsureInitializedAsync(_root);
        _documents = new JsonDocumentService(_root, new JsonMetadataStore(_root, writer), writer,
            new DocumentRevisionService());
    }

    // xUnit 在每个测试结束后调用：释放文件监视器等异步资源，并删除该测试独占的临时目录。
    public async Task DisposeAsync()
    {
        await _documents.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Create_uses_daily_sequence_and_persists_empty_object()
    {
        // 创建第一个文档应采用当天递增序号，而非随机文件名。
        var created = await _documents.CreateAsync();
        Assert.EndsWith("001.json", created.FileName, StringComparison.Ordinal);
        // 重新打开验证创建操作已落盘，且新文档的合法 JSON 初始内容是空对象。
        var opened = await _documents.OpenAsync(created.Id);
        Assert.Equal("{}", opened.Content);
    }

    [Fact]
    public async Task Save_rejects_external_revision_change()
    {
        // 保存前读取版本；随后绕过服务直接改写文件，模拟其他程序修改同一文档。
        var created = await _documents.CreateAsync();
        var opened = await _documents.OpenAsync(created.Id);
        var path = Path.Combine(_root, "json", "documents", created.FileName);
        await File.WriteAllTextAsync(path, "{\"external\":true}");
        // 旧 Revision 保存必须抛出领域异常，不能静默覆盖外部改动。
        var error = await Assert.ThrowsAsync<JujuException>(() =>
            _documents.SaveAsync(created.Id, "{}", opened.Revision));
        // 断言具体错误码，确保调用方可以区分版本冲突和其他保存失败。
        Assert.Equal(ErrorCode.ExternalModificationConflict, error.Code);
    }

    [Fact]
    public async Task Rename_preserves_document_identity_and_delete_moves_file_to_trash()
    {
        // 重命名只改变文件名，仍通过创建时的 Id 操作文档，验证身份没有随名称变化。
        var created = await _documents.CreateAsync();
        await _documents.RenameAsync(created.Id, "payment-request");
        Assert.Equal("payment-request.json", (await _documents.ListAsync()).Single().FileName);
        // 删除后列表不再包含文档，但物理文件应转移到回收目录而非直接丢失。
        await _documents.DeleteAsync(created.Id);
        Assert.Empty(await _documents.ListAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "json", ".trash")));
    }

    [Fact]
    public async Task Reorder_persists_metadata_order_without_renaming_files()
    {
        // 将第二项移动到索引 0，验证顺序是元数据属性而不是文件名排序结果。
        var first = await _documents.CreateAsync();
        var second = await _documents.CreateAsync();
        await _documents.ReorderAsync(second.Id, 0);
        var ordered = await _documents.ListAsync();
        // 同时断言列表顺序及每个持久化 Order 值，避免仅显示顺序正确但元数据错误。
        Assert.Equal([second.Id, first.Id], ordered.Select(item => item.Id));
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal(1, ordered[1].Order);
        // 重排不得重命名或删除已有的文档文件。
        Assert.True(File.Exists(Path.Combine(_root, "json", "documents", first.FileName)));
        Assert.True(File.Exists(Path.Combine(_root, "json", "documents", second.FileName)));
    }

    [Fact]
    public async Task Reorder_rejects_an_order_outside_the_document_list()
    {
        // 单个文档只有索引 0 合法；索引 1 必须被显式拒绝。
        var created = await _documents.CreateAsync();
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.ReorderAsync(created.Id, 1));
        // 使用领域错误码让 UI 能向用户说明是无效排序位置。
        Assert.Equal(ErrorCode.InvalidDocumentOrder, error.Code);
    }

    [Theory]
    // 此输入特意包含科学计数法和字符串内部空格，防止压缩器错误修改 JSON token 内容。
    [InlineData("{ \"value\": 1e10, \"text\": \"a b\" }", "{\"value\":1e10,\"text\":\"a b\"}")]
    public void Minify_preserves_json_tokens(string input, string expected) =>
        // 只去除结构性空白，输出必须与预期紧凑 JSON 完全一致。
        Assert.Equal(expected, JsonLexicalMinifier.Minify(input));

    [Fact]
    public void Minify_invalid_json_uses_a_typed_error()
    {
        // 不合法 JSON 不能以通用解析异常泄漏给调用者。
        var error = Assert.Throws<JujuException>(() => JsonLexicalMinifier.Minify("{ invalid"));
        // 断言稳定的领域错误码，供界面显示校验错误。
        Assert.Equal(ErrorCode.InvalidJson, error.Code);
    }

    [Fact]
    public void Json_shortcuts_default_to_the_overlap_plan()
    {
        // null 表示没有用户配置，Normalize 应生成完整默认快捷键方案。
        var shortcuts = JsonToolShortcuts.Normalize(null);

        // 先校验命令总数，再校验代表性编辑器、列表和对比模式快捷键及其元数据来源/上下文。
        Assert.Equal(JsonToolShortcuts.Commands.Count, shortcuts.Count);
        Assert.Equal("Ctrl+S", shortcuts[JsonToolCommand.Save]);
        Assert.Equal("Ctrl+K Ctrl+0", shortcuts[JsonToolCommand.FoldAll]);
        Assert.Equal("Escape", shortcuts[JsonToolCommand.ExitDiff]);
        Assert.Equal(JsonToolShortcutSource.Monaco, JsonToolShortcuts.GetDefinition(JsonToolCommand.Save).Source);
        Assert.Equal(JsonToolShortcutContext.List, JsonToolShortcuts.GetDefinition(JsonToolCommand.Rename).Context);
    }

    [Fact]
    public void Json_shortcuts_normalize_combinations_and_allow_cross_context_duplicates()
    {
        // 同一按键组合在不同上下文可共存；输入中的大小写与额外空白会被标准化。
        var shortcuts = JsonToolShortcuts.Defaults.ToDictionary(pair => pair.Key, pair => pair.Value);
        shortcuts[JsonToolCommand.New] = " control + alt + n ";
        shortcuts[JsonToolCommand.Rename] = "ctrl+alt+n";

        var normalized = JsonToolShortcuts.Normalize(shortcuts);

        // 两项最终使用统一的规范格式，说明规范化不因上下文不同而改变组合本身。
        Assert.Equal("Ctrl+Alt+N", normalized[JsonToolCommand.New]);
        Assert.Equal("Ctrl+Alt+N", normalized[JsonToolCommand.Rename]);
    }

    [Theory]
    // 覆盖重复组合、保留系统组合、过多修饰键和错误加号写法等不可接受的快捷键输入。
    [InlineData(JsonToolCommand.New, "Ctrl+S")]
    [InlineData(JsonToolCommand.New, "Alt+F4")]
    [InlineData(JsonToolCommand.New, "Ctrl+Alt+Delete")]
    [InlineData(JsonToolCommand.New, "Ctrl+Shift+Alt+Space")]
    [InlineData(JsonToolCommand.New, "Ctrl++N")]
    public void Json_shortcuts_reject_duplicate_reserved_or_invalid_combinations(JsonToolCommand command,
        string shortcut)
    {
        var shortcuts = JsonToolShortcuts.Defaults.ToDictionary(pair => pair.Key, pair => pair.Value);
        shortcuts[command] = shortcut;

        // 所有这些输入都必须在配置规范化时失败，防止运行时注册无效快捷键。
        Assert.Throws<InvalidOperationException>(() => JsonToolShortcuts.Normalize(shortcuts));
    }

    [Theory]
    // 覆盖 Windows 保留设备名、父目录穿越和路径分隔符，确保文档名不能逃逸目标目录。
    [InlineData("CON")]
    [InlineData("../escape")]
    [InlineData("name/child")]
    public void Document_name_rejects_unsafe_values(string name) =>
        // 任一不安全名称均应映射为统一的领域异常。
        Assert.Throws<JujuException>(() => JsonDocumentName.Normalize(name));

    [Fact]
    public async Task Data_root_rejects_non_empty_unmarked_directory()
    {
        // 预先创建含用户文件但没有 juju 标记文件的目录，模拟误选的数据根目录。
        var root = Path.Combine(_root, "not-a-root");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "user-file.txt"), "not juju data");
        var error = await Assert.ThrowsAsync<JujuException>(() =>
            new DataRootService(new AtomicFileWriter()).EnsureInitializedAsync(root));
        // 服务必须拒绝该目录，且不得在拒绝过程中写入标记文件污染用户数据。
        Assert.Equal(ErrorCode.DataRootInvalid, error.Code);
        Assert.False(File.Exists(Path.Combine(root, "juju.json")));
    }

    [Fact]
    public async Task Reconcile_recovers_documents_after_metadata_corruption()
    {
        // 损坏元数据文件，同时保留真实文档文件，验证列表加载触发恢复路径。
        var first = await _documents.CreateAsync();
        var documentPath = Path.Combine(_root, "json", "documents", first.FileName);
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "metadata.json"), "not metadata");
        var recovered = await _documents.ListAsync();
        // 恢复后文档应可见且原文件保持存在，损坏的元数据另存为带 .corrupt- 前缀的备份。
        Assert.Single(recovered);
        Assert.Equal(first.FileName, recovered[0].FileName);
        Assert.True(File.Exists(documentPath));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_root, "json"), "metadata.json.corrupt-*"));
    }

    [Fact]
    public async Task Reconcile_adds_external_file_and_removes_missing_metadata_entry()
    {
        // 删除已登记文件并手工加入未登记文件，模拟文件系统与元数据双向不一致。
        var first = await _documents.CreateAsync();
        File.Delete(Path.Combine(_root, "json", "documents", first.FileName));
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "documents", "external.json"), "{\"external\":true}");
        var list = await _documents.ListAsync();
        // 协调结果应移除失效条目、发现外部文件，并标记其来源为 recovered。
        Assert.Single(list);
        Assert.Equal("external.json", list[0].FileName);
        Assert.Equal("recovered", list[0].Source.Kind);
    }

    [Fact]
    public async Task Import_uses_incrementing_suffixes_and_does_not_overwrite_existing_documents()
    {
        // 已存在 import.json，再从两个不同目录导入同名文件，覆盖名称冲突递增策略。
        var existing = await _documents.CreateAsync();
        await _documents.RenameAsync(existing.Id, "import.json");
        var sourceDirectory = Path.Combine(_root, "sources");
        Directory.CreateDirectory(sourceDirectory);
        var firstSource = Path.Combine(sourceDirectory, "import.json");
        var secondDirectory = Path.Combine(sourceDirectory, "second");
        Directory.CreateDirectory(secondDirectory);
        var secondSource = Path.Combine(secondDirectory, "import.json");
        await File.WriteAllTextAsync(firstSource, "{\"first\":true}");
        await File.WriteAllTextAsync(secondSource, "{\"second\":true}");

        var imported = await _documents.ImportAsync([firstSource, secondSource]);

        // 导入项必须获得连续后缀，原始文档内容不能被覆盖，两个源文件内容须分别复制到目标。
        Assert.Equal(["import-2.json", "import-3.json"], imported.Select(item => item.FileName));
        Assert.Equal("{}", (await _documents.OpenAsync(existing.Id)).Content);
        Assert.Equal("{\"first\":true}",
            await File.ReadAllTextAsync(Path.Combine(_root, "json", "documents", "import-2.json")));
        Assert.Equal("{\"second\":true}",
            await File.ReadAllTextAsync(Path.Combine(_root, "json", "documents", "import-3.json")));
        // 导入元数据记录来源类型和原始文件名，便于 UI 追溯文档来源。
        Assert.All(imported, item => Assert.Equal("imported", item.Source.Kind));
        Assert.All(imported, item => Assert.Equal("import.json", item.Source.OriginalFileName));
    }

    [Fact]
    public async Task Import_missing_source_uses_a_typed_error_without_creating_a_document()
    {
        // 不存在的源路径是原子性失败场景，不能产生半创建的文档记录。
        var missing = Path.Combine(_root, "missing.json");
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.ImportAsync([missing]));
        // 断言可处理的导入错误码以及最终列表为空。
        Assert.Equal(ErrorCode.ImportFailed, error.Code);
        Assert.Empty(await _documents.ListAsync());
    }

    [Fact]
    public async Task Storage_manager_migrates_by_copy_and_keeps_source()
    {
        // 迁移到独立临时目标，验证它是复制迁移而非移动迁移。
        var destination = _root + "-migrated";
        var writer = new AtomicFileWriter();
        try
        {
            await using var storage = new StorageManager(new DataRootService(writer), writer);
            await storage.InitializeAsync(_root);
            await storage.WriteTextAsync(Path.Combine("json", "documents", "migrate.json"), "{} ");
            await storage.MigrateToAsync(destination);
            // 管理器切换到规范化后的新根目录，但新旧根中的文件均需存在。
            Assert.Equal(Path.GetFullPath(destination), storage.DataRoot);
            Assert.True(File.Exists(Path.Combine(_root, "json", "documents", "migrate.json")));
            Assert.True(File.Exists(Path.Combine(destination, "json", "documents", "migrate.json")));
        }
        finally
        {
            // 此目录不属于测试夹具的 _root，需在 finally 中即使断言失败也清理。
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task Document_watcher_reports_external_change_but_filters_self_save()
    {
        // 文档创建也会触发文件监视事件，先等待其稳定，以免误观察为本测试的保存事件。
        var created = await _documents.CreateAsync();
        await Task.Delay(400);
        // 异步完成源把事件回调转为可等待任务；RunContinuationsAsynchronously 避免回调线程同步执行测试续体。
        var observed =
            new TaskCompletionSource<JsonDocumentExternalChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        _documents.ExternalChanged += (_, change) => observed.TrySetResult(change);
        var opened = await _documents.OpenAsync(created.Id);
        await _documents.SaveAsync(created.Id, "{\"self\":true}", opened.Revision);
        await Task.Delay(400);
        // 服务自身保存产生的文件事件必须被过滤，避免 UI 误报外部修改。
        Assert.False(observed.Task.IsCompleted);
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "documents", created.FileName), "{\"external\":true}");
        var change = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // 真实外部写入应在超时内发出事件，并携带正确文档 Id 和 Changed 类型。
        Assert.Equal(created.Id, change.DocumentId);
        Assert.Equal(ExternalDocumentChangeKind.Changed, change.Kind);
    }

    [Fact]
    public async Task Save_with_current_revision_persists_invalid_json_text()
    {
        // 保存服务负责持久化与版本控制，而 JSON 语法校验由编辑器另行处理，因此允许保存未完成的输入。
        var created = await _documents.CreateAsync();
        var opened = await _documents.OpenAsync(created.Id);
        const string invalidJson = "{ not valid JSON";

        await _documents.SaveAsync(created.Id, invalidJson, opened.Revision);

        // 重开后内容必须逐字保留，证明服务没有隐式格式化或拒绝该文本。
        Assert.Equal(invalidJson, (await _documents.OpenAsync(created.Id)).Content);
    }

    [Fact]
    public async Task Document_load_records_content_safe_performance_measurement()
    {
        // 用记录型遥测替换默认实现；先释放夹具创建的服务，避免文件监视器和服务实例重叠。
        var telemetry = new RecordingTelemetry();
        await _documents.DisposeAsync();
        var writer = new AtomicFileWriter();
        _documents = new JsonDocumentService(_root, new JsonMetadataStore(_root, writer), writer,
            new DocumentRevisionService(), telemetry);
        var created = await _documents.CreateAsync();

        await _documents.OpenAsync(created.Id);

        // 读取操作应恰好记录一次加载指标，且耗时与两个内存数值是可供诊断的非负/正值。
        var measurement = Assert.Single(telemetry.Measurements,
            item => item.Operation == PerformanceOperation.DocumentLoad);
        Assert.True(measurement.Duration >= TimeSpan.Zero);
        Assert.True(measurement.WorkingSetBytes > 0);
        Assert.True(measurement.ManagedMemoryBytes > 0);
    }

    [Fact]
    public async Task Atomic_write_cancellation_preserves_existing_file()
    {
        // 先写入旧内容，再传入已取消的令牌，以覆盖原子写入在替换前被取消的路径。
        var path = Path.Combine(_root, "atomic.txt");
        var writer = new AtomicFileWriter();
        await writer.WriteTextAsync(path, "old");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // 取消应以标准取消异常结束，随后验证原文件内容没有被部分写入的新内容破坏。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.WriteTextAsync(path, "new", cancellation.Token));
        Assert.Equal("old", await File.ReadAllTextAsync(path));
    }

    // 最小的内存遥测替身：测试只需观察服务上报了什么，不需要引入真实监控基础设施。
    private sealed class RecordingTelemetry : IPerformanceTelemetry
    {
        // 收集每次 Measure 结束或 RecordMemory 产生的测量值，供断言查询。
        public List<PerformanceMeasurement> Measurements { get; } = [];

        // 返回一次性测量对象；Dispose 时代表操作结束并记录结果。
        public IDisposable Measure(PerformanceOperation operation) => new Measurement(operation, Measurements);

        // 直接记录内存采样；使用 1 作为确定性的正数占位，以满足被测契约而非测量真实进程内存。
        public void RecordMemory(PerformanceOperation operation) =>
            Measurements.Add(new(operation, TimeSpan.Zero, 1, 1));

        private sealed class Measurement(PerformanceOperation operation, List<PerformanceMeasurement> measurements)
            : IDisposable
        {
            // 服务的 using/Dispose 结束测量范围时写入一条确定性的性能记录。
            public void Dispose() => measurements.Add(new(operation, TimeSpan.Zero, 1, 1));
        }
    }
}