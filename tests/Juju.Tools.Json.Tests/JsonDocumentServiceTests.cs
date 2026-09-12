using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Core.Telemetry;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Minify;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Tests;

public sealed class JsonDocumentServiceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "juju-tests", Guid.NewGuid().ToString("N"));
    private JsonDocumentService _documents = null!;

    public async Task InitializeAsync()
    {
        var writer = new AtomicFileWriter();
        await new DataRootService(writer).EnsureInitializedAsync(_root);
        _documents = new JsonDocumentService(_root, new JsonMetadataStore(_root, writer), writer, new DocumentRevisionService());
    }

    public async Task DisposeAsync()
    {
        await _documents.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Create_uses_daily_sequence_and_persists_empty_object()
    {
        var created = await _documents.CreateAsync();
        Assert.EndsWith("001.json", created.FileName, StringComparison.Ordinal);
        var opened = await _documents.OpenAsync(created.Id);
        Assert.Equal("{}", opened.Content);
    }

    [Fact]
    public async Task Save_rejects_external_revision_change()
    {
        var created = await _documents.CreateAsync();
        var opened = await _documents.OpenAsync(created.Id);
        var path = Path.Combine(_root, "json", "documents", created.FileName);
        await File.WriteAllTextAsync(path, "{\"external\":true}");
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.SaveAsync(created.Id, "{}", opened.Revision));
        Assert.Equal(ErrorCode.ExternalModificationConflict, error.Code);
    }

    [Fact]
    public async Task Rename_preserves_document_identity_and_delete_moves_file_to_trash()
    {
        var created = await _documents.CreateAsync();
        await _documents.RenameAsync(created.Id, "payment-request");
        Assert.Equal("payment-request.json", (await _documents.ListAsync()).Single().FileName);
        await _documents.DeleteAsync(created.Id);
        Assert.Empty(await _documents.ListAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "json", ".trash")));
    }

    [Fact]
    public async Task Reorder_persists_metadata_order_without_renaming_files()
    {
        var first = await _documents.CreateAsync();
        var second = await _documents.CreateAsync();
        await _documents.ReorderAsync(second.Id, 0);
        var ordered = await _documents.ListAsync();
        Assert.Equal([second.Id, first.Id], ordered.Select(item => item.Id));
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal(1, ordered[1].Order);
        Assert.True(File.Exists(Path.Combine(_root, "json", "documents", first.FileName)));
        Assert.True(File.Exists(Path.Combine(_root, "json", "documents", second.FileName)));
    }

    [Fact]
    public async Task Reorder_rejects_an_order_outside_the_document_list()
    {
        var created = await _documents.CreateAsync();
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.ReorderAsync(created.Id, 1));
        Assert.Equal(ErrorCode.InvalidDocumentOrder, error.Code);
    }

    [Theory]
    [InlineData("{ \"value\": 1e10, \"text\": \"a b\" }", "{\"value\":1e10,\"text\":\"a b\"}")]
    public void Minify_preserves_json_tokens(string input, string expected) => Assert.Equal(expected, new JsonLexicalMinifier().Minify(input));

    [Fact]
    public void Minify_invalid_json_uses_a_typed_error()
    {
        var error = Assert.Throws<JujuException>(() => new JsonLexicalMinifier().Minify("{ invalid"));
        Assert.Equal(ErrorCode.InvalidJson, error.Code);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("../escape")]
    [InlineData("name/child")]
    public void Document_name_rejects_unsafe_values(string name) => Assert.Throws<JujuException>(() => JsonDocumentName.Normalize(name));

    [Fact]
    public async Task Data_root_rejects_non_empty_unmarked_directory()
    {
        var root = Path.Combine(_root, "not-a-root");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "user-file.txt"), "not juju data");
        var error = await Assert.ThrowsAsync<JujuException>(() => new DataRootService(new AtomicFileWriter()).EnsureInitializedAsync(root));
        Assert.Equal(ErrorCode.DataRootInvalid, error.Code);
        Assert.False(File.Exists(Path.Combine(root, "juju.json")));
    }

    [Fact]
    public async Task Reconcile_recovers_documents_after_metadata_corruption()
    {
        var first = await _documents.CreateAsync();
        var documentPath = Path.Combine(_root, "json", "documents", first.FileName);
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "metadata.json"), "not metadata");
        var recovered = await _documents.ListAsync();
        Assert.Single(recovered);
        Assert.Equal(first.FileName, recovered[0].FileName);
        Assert.True(File.Exists(documentPath));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_root, "json"), "metadata.json.corrupt-*"));
    }

    [Fact]
    public async Task Reconcile_adds_external_file_and_removes_missing_metadata_entry()
    {
        var first = await _documents.CreateAsync();
        File.Delete(Path.Combine(_root, "json", "documents", first.FileName));
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "documents", "external.json"), "{\"external\":true}");
        var list = await _documents.ListAsync();
        Assert.Single(list);
        Assert.Equal("external.json", list[0].FileName);
        Assert.Equal("recovered", list[0].Source.Kind);
    }

    [Fact]
    public async Task Import_uses_incrementing_suffixes_and_does_not_overwrite_existing_documents()
    {
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

        Assert.Equal(["import-2.json", "import-3.json"], imported.Select(item => item.FileName));
        Assert.Equal("{}", (await _documents.OpenAsync(existing.Id)).Content);
        Assert.Equal("{\"first\":true}", await File.ReadAllTextAsync(Path.Combine(_root, "json", "documents", "import-2.json")));
        Assert.Equal("{\"second\":true}", await File.ReadAllTextAsync(Path.Combine(_root, "json", "documents", "import-3.json")));
        Assert.All(imported, item => Assert.Equal("imported", item.Source.Kind));
        Assert.All(imported, item => Assert.Equal("import.json", item.Source.OriginalFileName));
    }

    [Fact]
    public async Task Import_missing_source_uses_a_typed_error_without_creating_a_document()
    {
        var missing = Path.Combine(_root, "missing.json");
        var error = await Assert.ThrowsAsync<JujuException>(() => _documents.ImportAsync([missing]));
        Assert.Equal(ErrorCode.ImportFailed, error.Code);
        Assert.Empty(await _documents.ListAsync());
    }

    [Fact]
    public async Task Storage_manager_migrates_by_copy_and_keeps_source()
    {
        var destination = _root + "-migrated";
        var writer = new AtomicFileWriter();
        try
        {
            await using var storage = new StorageManager(new DataRootService(writer), writer);
            await storage.InitializeAsync(_root);
            await storage.WriteTextAsync(Path.Combine("json", "documents", "migrate.json"), "{} ");
            await storage.MigrateToAsync(destination);
            Assert.Equal(Path.GetFullPath(destination), storage.DataRoot);
            Assert.True(File.Exists(Path.Combine(_root, "json", "documents", "migrate.json")));
            Assert.True(File.Exists(Path.Combine(destination, "json", "documents", "migrate.json")));
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
    }

    [Fact]
    public async Task Document_watcher_reports_external_change_but_filters_self_save()
    {
        var created = await _documents.CreateAsync();
        // Let the create operation's watcher event settle before observing the save operation.
        await Task.Delay(400);
        var observed = new TaskCompletionSource<JsonDocumentExternalChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        _documents.ExternalChanged += (_, change) => observed.TrySetResult(change);
        var opened = await _documents.OpenAsync(created.Id);
        await _documents.SaveAsync(created.Id, "{\"self\":true}", opened.Revision);
        await Task.Delay(400);
        Assert.False(observed.Task.IsCompleted);
        await File.WriteAllTextAsync(Path.Combine(_root, "json", "documents", created.FileName), "{\"external\":true}");
        var change = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(created.Id, change.DocumentId);
        Assert.Equal(ExternalDocumentChangeKind.Changed, change.Kind);
    }

    [Fact]
    public async Task Save_with_current_revision_persists_invalid_json_text()
    {
        var created = await _documents.CreateAsync();
        var opened = await _documents.OpenAsync(created.Id);
        const string invalidJson = "{ not valid JSON";

        await _documents.SaveAsync(created.Id, invalidJson, opened.Revision);

        Assert.Equal(invalidJson, (await _documents.OpenAsync(created.Id)).Content);
    }

    [Fact]
    public async Task Document_load_records_content_safe_performance_measurement()
    {
        var telemetry = new RecordingTelemetry();
        await _documents.DisposeAsync();
        var writer = new AtomicFileWriter();
        _documents = new JsonDocumentService(_root, new JsonMetadataStore(_root, writer), writer, new DocumentRevisionService(), telemetry);
        var created = await _documents.CreateAsync();

        await _documents.OpenAsync(created.Id);

        var measurement = Assert.Single(telemetry.Measurements, item => item.Operation == PerformanceOperation.DocumentLoad);
        Assert.True(measurement.Duration >= TimeSpan.Zero);
        Assert.True(measurement.WorkingSetBytes > 0);
        Assert.True(measurement.ManagedMemoryBytes > 0);
    }

    [Fact]
    public async Task Atomic_write_cancellation_preserves_existing_file()
    {
        var path = Path.Combine(_root, "atomic.txt");
        var writer = new AtomicFileWriter();
        await writer.WriteTextAsync(path, "old");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteTextAsync(path, "new", cancellation.Token));
        Assert.Equal("old", await File.ReadAllTextAsync(path));
    }

    private sealed class RecordingTelemetry : IPerformanceTelemetry
    {
        public List<PerformanceMeasurement> Measurements { get; } = [];
        public IDisposable Measure(PerformanceOperation operation) => new Measurement(operation, Measurements);
        public void RecordMemory(PerformanceOperation operation) => Measurements.Add(new(operation, TimeSpan.Zero, 1, 1));

        private sealed class Measurement(PerformanceOperation operation, List<PerformanceMeasurement> measurements) : IDisposable
        {
            public void Dispose() => measurements.Add(new(operation, TimeSpan.Zero, 1, 1));
        }
    }
}
